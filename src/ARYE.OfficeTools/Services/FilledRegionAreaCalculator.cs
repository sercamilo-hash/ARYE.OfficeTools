#nullable disable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace ARYE.OfficeTools.Services
{
    /// <summary>
    /// Result of the geometric area computation of a single FilledRegion.
    /// All areas are in Revit internal units (square feet).
    /// </summary>
    public class RegionAreaResult
    {
        public bool Ok = true;
        public string Error = string.Empty;

        /// <summary>Net area (outer loops minus holes), square feet.</summary>
        public double AreaFeet2;

        /// <summary>Sum of the signed loop areas, as Revit orients them. Used as a cross-check.</summary>
        public double SignedSumFeet2;

        public int LoopCount;
        public int OuterLoops;
        public int HoleLoops;

        /// <summary>
        /// True when the nesting analysis and the signed-sum disagree. The nesting result is used,
        /// but the row is flagged so the user can inspect the region manually.
        /// </summary>
        public bool OrientationAmbiguous;

        /// <summary>True when at least one boundary curve is not a Line (arc / spline).</summary>
        public bool HasCurvedEdges;

        /// <summary>True when a boundary curve is neither Line nor Arc (area is then approximated by tessellation).</summary>
        public bool HasApproximatedEdges;

        public string Warning = string.Empty;
    }

    /// <summary>
    /// Computes the true area of a FilledRegion from its boundary loops.
    ///
    /// Why geometry instead of a built-in parameter: the Area shown for a filled region is
    /// read-only and its availability depends on the Revit version. Computing it from
    /// FilledRegion.GetBoundaries() works in every version and gives an exact value:
    /// polygon shoelace over the chord polygon plus the exact circular-segment correction
    /// for every Arc. Only non Line/Arc curves (splines, ellipses) fall back to tessellation.
    ///
    /// Holes are detected by nesting (point-in-polygon depth), not by trusting the loop
    /// orientation, so multi-loop and multi-island regions are handled correctly.
    /// </summary>
    public static class FilledRegionAreaCalculator
    {
        private const double AREA_EPS = 1e-7;      // ft2
        private const double REL_EPS = 1e-6;

        public static RegionAreaResult Compute(FilledRegion region)
        {
            RegionAreaResult res = new RegionAreaResult();

            if (region == null)
            {
                res.Ok = false;
                res.Error = "Null element.";
                return res;
            }

            IList<CurveLoop> loops;
            try
            {
                loops = region.GetBoundaries();
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Error = "GetBoundaries() failed: " + ex.Message;
                return res;
            }

            if (loops == null || loops.Count == 0)
            {
                res.Ok = false;
                res.Error = "Region has no boundary loops.";
                return res;
            }

            // ── Reference frame: all loops of a filled region are coplanar (they live in a view's
            //    sketch plane), so the first planar loop defines the UV frame for every loop.
            XYZ origin = null, xVec = null, yVec = null;
            foreach (CurveLoop cl in loops)
            {
                try
                {
                    Plane pl = cl.GetPlane();
                    if (pl != null)
                    {
                        origin = pl.Origin;
                        xVec = pl.XVec.Normalize();
                        yVec = pl.YVec.Normalize();
                        break;
                    }
                }
                catch { /* try the next loop */ }
            }

            if (origin == null)
            {
                // Degenerate fallback: use the world XY frame. Correct for every plan/detail view region.
                origin = XYZ.Zero;
                xVec = XYZ.BasisX;
                yVec = XYZ.BasisY;
                res.Warning = AppendWarning(res.Warning, "No planar frame; world XY assumed");
            }

            List<LoopData> data = new List<LoopData>();

            foreach (CurveLoop cl in loops)
            {
                LoopData ld = BuildLoop(cl, origin, xVec, yVec, res);
                if (ld == null) continue;
                if (Math.Abs(ld.Signed) < AREA_EPS) continue;   // zero-area / degenerate loop
                data.Add(ld);
            }

            if (data.Count == 0)
            {
                res.Ok = false;
                res.Error = "All boundary loops are degenerate (zero area).";
                return res;
            }

            res.LoopCount = data.Count;

            // ── Signed sum (what Revit's own orientation implies)
            double signedSum = 0.0;
            foreach (LoopData ld in data) signedSum += ld.Signed;
            res.SignedSumFeet2 = signedSum;

            // ── Nesting analysis: a loop contained in an odd number of other loops is a hole.
            double net = 0.0;
            for (int i = 0; i < data.Count; i++)
            {
                int depth = 0;
                for (int j = 0; j < data.Count; j++)
                {
                    if (i == j) continue;
                    if (PointInPolygon(data[i].Probe, data[j].Poly)) depth++;
                }

                bool isHole = (depth % 2) == 1;
                if (isHole) { res.HoleLoops++; net -= Math.Abs(data[i].Signed); }
                else { res.OuterLoops++; net += Math.Abs(data[i].Signed); }
            }

            res.AreaFeet2 = net;

            if (net <= 0.0)
            {
                // Nesting produced a non-positive area (very unusual). Fall back to |signed sum|.
                res.AreaFeet2 = Math.Abs(signedSum);
                res.OrientationAmbiguous = true;
                res.Warning = AppendWarning(res.Warning, "Nesting gave a non-positive area; |signed sum| used");
            }
            else if (data.Count > 1)
            {
                double diff = Math.Abs(Math.Abs(signedSum) - net);
                double tol = Math.Max(AREA_EPS, net * REL_EPS);
                if (diff > tol)
                {
                    res.OrientationAmbiguous = true;
                    res.Warning = AppendWarning(res.Warning,
                        string.Format("Loop orientation inconsistent (signed sum {0:F4} ft2 vs nested {1:F4} ft2)",
                            Math.Abs(signedSum), net));
                }
            }

            if (res.HasApproximatedEdges)
                res.Warning = AppendWarning(res.Warning, "Spline/ellipse edge: area tessellated (approximate)");

            return res;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Internals
        // ────────────────────────────────────────────────────────────────────

        private class LoopData
        {
            public double Signed;       // signed area in the UV frame, ft2
            public List<UV> Poly;       // tessellated polygon, used only for containment tests
            public UV Probe;            // a point guaranteed on this loop's boundary curve midpoint
        }

        private static LoopData BuildLoop(CurveLoop cl, XYZ origin, XYZ xVec, XYZ yVec, RegionAreaResult res)
        {
            if (cl == null) return null;

            LoopData ld = new LoopData();
            ld.Poly = new List<UV>();

            double signed = 0.0;
            bool probeSet = false;

            foreach (Curve c in cl)
            {
                if (c == null) continue;

                XYZ e0, e1;
                try
                {
                    e0 = c.GetEndPoint(0);
                    e1 = c.GetEndPoint(1);
                }
                catch { continue; }

                UV p0 = ToUV(e0, origin, xVec, yVec);
                UV p1 = ToUV(e1, origin, xVec, yVec);

                // Chord contribution (shoelace)
                signed += 0.5 * (p0.U * p1.V - p1.U * p0.V);

                Arc arc = c as Arc;
                if (arc != null)
                {
                    res.HasCurvedEdges = true;
                    signed += ArcSegmentSignedArea(arc, p0, p1, origin, xVec, yVec);
                }
                else if (!(c is Line))
                {
                    // Spline / ellipse / hermite: approximate with the tessellated chain.
                    res.HasCurvedEdges = true;
                    res.HasApproximatedEdges = true;
                    signed += TessellatedExtraArea(c, p0, p1, origin, xVec, yVec);
                }

                // Polygon for containment tests (tessellated so arcs are respected)
                try
                {
                    IList<XYZ> pts = c.Tessellate();
                    if (pts != null)
                    {
                        for (int k = 0; k < pts.Count - 1; k++)   // last point = next curve's first
                            ld.Poly.Add(ToUV(pts[k], origin, xVec, yVec));
                    }
                    else ld.Poly.Add(p0);
                }
                catch { ld.Poly.Add(p0); }

                if (!probeSet)
                {
                    try
                    {
                        XYZ mid = c.Evaluate(0.5, true);
                        ld.Probe = ToUV(mid, origin, xVec, yVec);
                        probeSet = true;
                    }
                    catch { }
                }
            }

            if (!probeSet)
            {
                if (ld.Poly.Count == 0) return null;
                ld.Probe = ld.Poly[0];
            }

            ld.Signed = signed;
            return ld;
        }

        /// <summary>
        /// Exact signed area between an arc and its chord (circular segment), with the sign that
        /// makes the shoelace accumulation correct regardless of loop orientation.
        ///
        /// The side is decided with the arc MIDPOINT, not the centre: a semicircle bulging inward
        /// and one bulging outward share the same endpoints and the same centre, so the centre
        /// cannot tell them apart, while the midpoint always can. If the midpoint lies to the
        /// right of the chord direction the segment is added, otherwise it is subtracted.
        /// </summary>
        private static double ArcSegmentSignedArea(Arc arc, UV p0, UV p1, XYZ origin, XYZ xVec, XYZ yVec)
        {
            try
            {
                double r = arc.Radius;
                if (r <= 0) return 0.0;

                double sweep = Math.Abs(arc.GetEndParameter(1) - arc.GetEndParameter(0));
                if (sweep <= 0) return 0.0;
                if (sweep > 2.0 * Math.PI) sweep = 2.0 * Math.PI;

                double segment = 0.5 * r * r * (sweep - Math.Sin(sweep));   // >= 0

                double dx = p1.U - p0.U, dy = p1.V - p0.V;

                UV probe;
                try { probe = ToUV(arc.Evaluate(0.5, true), origin, xVec, yVec); }
                catch { probe = null; }

                if (probe != null)
                {
                    double mx = probe.U - p0.U, my = probe.V - p0.V;
                    double cross = dx * my - dy * mx;
                    if (Math.Abs(cross) < 1e-12) return segment;      // degenerate chord (full circle)
                    return cross < 0 ? segment : -segment;
                }

                // Fallback when the midpoint cannot be evaluated: use the centre (ambiguous only
                // for an exact semicircle, where the magnitude is still correct).
                UV c = ToUV(arc.Center, origin, xVec, yVec);
                double ccx = c.U - p0.U, ccy = c.V - p0.V;
                double crossC = dx * ccy - dy * ccx;
                if (Math.Abs(crossC) < 1e-12) return segment;
                return crossC > 0 ? segment : -segment;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>Fallback for splines: shoelace of the tessellated chain minus its chord.</summary>
        private static double TessellatedExtraArea(Curve c, UV p0, UV p1, XYZ origin, XYZ xVec, XYZ yVec)
        {
            try
            {
                IList<XYZ> pts = c.Tessellate();
                if (pts == null || pts.Count < 3) return 0.0;

                double a = 0.0;
                for (int k = 0; k < pts.Count - 1; k++)
                {
                    UV a0 = ToUV(pts[k], origin, xVec, yVec);
                    UV a1 = ToUV(pts[k + 1], origin, xVec, yVec);
                    a += 0.5 * (a0.U * a1.V - a1.U * a0.V);
                }
                // remove the chord already accounted for by the caller
                a -= 0.5 * (p0.U * p1.V - p1.U * p0.V);
                return a;
            }
            catch { return 0.0; }
        }

        private static UV ToUV(XYZ p, XYZ origin, XYZ xVec, XYZ yVec)
        {
            XYZ d = p - origin;
            return new UV(d.DotProduct(xVec), d.DotProduct(yVec));
        }

        private static bool PointInPolygon(UV pt, List<UV> poly)
        {
            if (poly == null || poly.Count < 3) return false;

            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].U, yi = poly[i].V;
                double xj = poly[j].U, yj = poly[j].V;

                bool straddles = (yi > pt.V) != (yj > pt.V);
                if (!straddles) continue;

                double t = (pt.V - yi) / (yj - yi);
                double x = xi + t * (xj - xi);
                if (pt.U < x) inside = !inside;
            }
            return inside;
        }

        private static string AppendWarning(string current, string add)
        {
            if (string.IsNullOrEmpty(current)) return add;
            return current + " · " + add;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Cross-check helper: the read-only Area shown by Revit, when it exists.
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the read-only Area parameter of the element when the Revit version exposes one.
        /// Returns NaN when unavailable. Used only to show a delta column in the preview so the
        /// user can confirm the computed value matches what Revit displays.
        /// </summary>
        public static double TryReadRevitAreaParameter(Element e)
        {
            if (e == null) return double.NaN;
            try
            {
                Parameter p = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (p == null) p = e.LookupParameter("Area");
                if (p == null) return double.NaN;
                if (p.StorageType != StorageType.Double) return double.NaN;
                double v = p.AsDouble();
                return v > 0 ? v : double.NaN;
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
