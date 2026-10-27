using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// One recorded dimension (DOF assessment record) against the tax-map
    /// polygon edge that best matches it.
    /// </summary>
    public sealed class DimensionComparison
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>The legal figure MapPLUTO carries as an attribute (LotFront / LotDepth).</summary>
        public double RecordedFt { get; set; }

        /// <summary>The polygon edge closest in length to the recorded figure. 0 if no geometry.</summary>
        public double MeasuredFt { get; set; }

        public double DifferenceFt => MeasuredFt - RecordedFt;

        public double DifferencePct => RecordedFt > 0 ? Math.Abs(DifferenceFt) / RecordedFt : 0;

        public bool HasRecorded => RecordedFt > 0;

        public bool HasMeasured => MeasuredFt > 0;

        /// <summary>True when the tax map disagrees with the record by more than the tolerance.</summary>
        public bool IsDiscrepant(double tolerancePct) => HasRecorded && HasMeasured && DifferencePct > tolerancePct;
    }

    /// <summary>
    /// MapPLUTO carries two different truths for a lot's size: the recorded
    /// legal dimensions (LotFront/LotDepth, from DOF assessment records) and
    /// the Digital Tax Map polygon — a GIS product DOF itself says is not a
    /// substitute for a survey. They routinely disagree by feet. This puts
    /// them side by side so the user sees the gap instead of trusting a line.
    /// </summary>
    public static class LotDimensionCheck
    {
        /// <summary>1% — a tax-map edge more than this off the record gets a warning.</summary>
        public const double DefaultTolerancePct = 0.01;

        public static List<double> EdgeLengths(IReadOnlyList<LotPoint> ring)
        {
            var lengths = new List<double>();
            if (ring == null || ring.Count < 2)
            {
                return lengths;
            }
            for (int i = 0; i < ring.Count; i++)
            {
                LotPoint a = ring[i];
                LotPoint b = ring[(i + 1) % ring.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.01) // skip the repeated closing vertex GeoJSON rings carry
                {
                    lengths.Add(len);
                }
            }
            return lengths;
        }

        /// <summary>
        /// Matches each recorded dimension to the polygon edge nearest it in
        /// length — rotation-independent, unlike a bounding box, so a lot
        /// skewed 15° off the grid still compares the right edges.
        /// </summary>
        public static List<DimensionComparison> Compare(NycLotInfo lot)
        {
            List<double> edges = lot.PolygonRings.Count > 0
                ? EdgeLengths(lot.PolygonRings[0])
                : new List<double>();

            return new List<DimensionComparison>
            {
                Build("Frontage", lot.LotFrontRecordedFt, edges),
                Build("Depth", lot.LotDepthRecordedFt, edges)
            };
        }

        private static DimensionComparison Build(string label, double recordedFt, List<double> edges)
        {
            double measured = 0;
            if (recordedFt > 0 && edges.Count > 0)
            {
                measured = edges.OrderBy(e => Math.Abs(e - recordedFt)).First();
            }
            return new DimensionComparison { Label = label, RecordedFt = recordedFt, MeasuredFt = measured };
        }

        /// <summary>Human-readable lines for the UI and the drawing's zoning note.</summary>
        public static List<string> Describe(IReadOnlyList<DimensionComparison> comparisons, double tolerancePct = DefaultTolerancePct)
        {
            var lines = new List<string>();
            foreach (DimensionComparison c in comparisons)
            {
                if (!c.HasRecorded)
                {
                    lines.Add($"{c.Label}: no recorded dimension in MapPLUTO");
                    continue;
                }
                if (!c.HasMeasured)
                {
                    lines.Add($"{c.Label}: {c.RecordedFt:0.##}' recorded · no tax-map geometry to measure");
                    continue;
                }
                string flag = c.IsDiscrepant(tolerancePct)
                    ? $"  ⚠ {(c.DifferenceFt >= 0 ? "+" : "")}{c.DifferenceFt:0.#}' ({c.DifferencePct:P1})"
                    : "  ✓";
                lines.Add($"{c.Label}: {c.RecordedFt:0.##}' recorded · {c.MeasuredFt:0.##}' measured (tax map){flag}");
            }
            return lines;
        }

        public static bool AnyDiscrepant(IReadOnlyList<DimensionComparison> comparisons, double tolerancePct = DefaultTolerancePct) =>
            comparisons.Any(c => c.IsDiscrepant(tolerancePct));
    }
}
