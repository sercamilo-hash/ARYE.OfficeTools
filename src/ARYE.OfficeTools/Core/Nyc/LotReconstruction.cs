using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>A lot ring rebuilt from legal face lengths, with the honesty numbers attached.</summary>
    public sealed class LotReconstructionResult
    {
        /// <summary>Vertices counter-clockwise, face i runs from Ring[i] to Ring[(i+1)%n]. Empty when it failed.</summary>
        public List<LotPoint> Ring { get; } = new List<LotPoint>();

        /// <summary>Length actually drawn for each face: the legal length exactly, or the adjusted digitized length for faces without one.</summary>
        public List<double> FaceLengthsFt { get; } = new List<double>();

        /// <summary>Bearing change applied to each face, degrees (signed).</summary>
        public List<double> BearingChangesDeg { get; } = new List<double>();

        /// <summary>Raw traverse misclosure before adjustment, in feet.</summary>
        public double ClosureErrorFt { get; set; }

        /// <summary>Misclosure relative to the perimeter (e.g. 0.001 = 1:1000).</summary>
        public double ClosureErrorRatio { get; set; }

        public double MaxBearingChangeDeg => BearingChangesDeg.Count > 0 ? BearingChangesDeg.Max(Math.Abs) : 0;

        public List<string> Warnings { get; } = new List<string>();

        public string FailureReason { get; set; } = string.Empty;

        public bool Succeeded => Ring.Count >= 3 && FailureReason.Length == 0;

        public double AreaSqFt => Ring.Count >= 3 ? Math.Abs(LotGeometry.SignedArea(Ring)) : 0;
    }

    /// <summary>
    /// Rebuilds a lot from the dimensions written on the official tax map.
    /// The tax map gives LENGTHS and no bearings; bearings come from the
    /// digitized DTM, which is the weaker datum. So the legal lengths are
    /// kept EXACT and the traverse misclosure is absorbed by the bearings
    /// (smallest possible rotations, weighted least squares), plus by the
    /// length of any face that has no legal dimension. Nothing is scaled.
    /// </summary>
    public static class LotReconstruction
    {
        /// <summary>Above this the legal lengths and DTM bearings disagree too much to trust the rebuild.</summary>
        public const double MaxAcceptableClosureRatio = 0.02;

        /// <summary>How much a DTM bearing is expected to be off (1σ), radians. ~0.5°.</summary>
        private const double BearingSigmaRad = 0.5 * Math.PI / 180.0;

        /// <summary>How much a face WITHOUT a legal dimension may change length (1σ), feet.</summary>
        private const double FreeLengthSigmaFt = 5.0;

        public static LotReconstructionResult FromLegalFaces(IReadOnlyList<NycLotFace> orderedFaces)
        {
            var result = new LotReconstructionResult();
            if (orderedFaces == null || orderedFaces.Count < 3)
            {
                result.FailureReason = "need at least 3 ordered faces";
                return result;
            }

            int n = orderedFaces.Count;
            var theta = new double[n];   // direction angle (math convention, radians)
            var length = new double[n];
            var isFree = new bool[n];
            for (int i = 0; i < n; i++)
            {
                NycLotFace face = orderedFaces[i];
                LotPoint dir = face.Direction;
                if (Math.Abs(dir.X) < 1e-12 && Math.Abs(dir.Y) < 1e-12)
                {
                    result.FailureReason = $"face {i} has no direction (zero-length digitized path)";
                    return result;
                }
                theta[i] = Math.Atan2(dir.Y, dir.X);
                if (face.LegalLengthFt > 0)
                {
                    length[i] = face.LegalLengthFt;
                }
                else
                {
                    length[i] = face.MeasuredLengthFt;
                    isFree[i] = true;
                    result.Warnings.Add($"face {i}: no legal length on the tax map — its digitized {length[i]:0.##} ft may stretch to close the figure");
                }
            }

            double perimeter = length.Sum();
            (double ex, double ey) = Misclosure(theta, length);
            result.ClosureErrorFt = Math.Sqrt(ex * ex + ey * ey);
            result.ClosureErrorRatio = perimeter > 0 ? result.ClosureErrorFt / perimeter : 0;
            if (result.ClosureErrorRatio > MaxAcceptableClosureRatio)
            {
                result.FailureReason =
                    $"traverse misclosure {result.ClosureErrorFt:0.##} ft (1:{(result.ClosureErrorRatio > 0 ? 1 / result.ClosureErrorRatio : 0):0}) " +
                    $"exceeds 1:{1 / MaxAcceptableClosureRatio:0} — legal lengths and tax-map bearings do not describe the same figure";
                return result;
            }

            // Weighted minimum-norm least squares, iterated (the problem is mildly non-linear):
            // unknowns are δθ for every face and δL for free faces; constraints are the
            // two closure equations. Solution: δ = W Jᵀ (J W Jᵀ)⁻¹ (−e).
            var deltaTheta = new double[n];
            bool adjusted = false;
            for (int iter = 0; iter < 8 && Math.Sqrt(ex * ex + ey * ey) > 1e-7; iter++)
            {
                double a11 = 0, a12 = 0, a22 = 0;
                for (int i = 0; i < n; i++)
                {
                    // ∂closure/∂θ_i = L_i·(−sinθ, cosθ)
                    double jx = -length[i] * Math.Sin(theta[i]), jy = length[i] * Math.Cos(theta[i]);
                    double w = BearingSigmaRad * BearingSigmaRad;
                    a11 += w * jx * jx; a12 += w * jx * jy; a22 += w * jy * jy;
                    if (isFree[i])
                    {
                        // ∂closure/∂L_i = (cosθ, sinθ)
                        double ux = Math.Cos(theta[i]), uy = Math.Sin(theta[i]);
                        double wl = FreeLengthSigmaFt * FreeLengthSigmaFt;
                        a11 += wl * ux * ux; a12 += wl * ux * uy; a22 += wl * uy * uy;
                    }
                }
                double det = a11 * a22 - a12 * a12;
                if (Math.Abs(det) < 1e-12)
                {
                    result.FailureReason = "faces are collinear — cannot close the figure by adjusting bearings";
                    return result;
                }
                // λ = (J W Jᵀ)⁻¹ (−e)
                double lx = (a22 * -ex - a12 * -ey) / det;
                double ly = (-a12 * -ex + a11 * -ey) / det;
                for (int i = 0; i < n; i++)
                {
                    double jx = -length[i] * Math.Sin(theta[i]), jy = length[i] * Math.Cos(theta[i]);
                    double dTheta = BearingSigmaRad * BearingSigmaRad * (jx * lx + jy * ly);
                    theta[i] += dTheta;
                    deltaTheta[i] += dTheta;
                    if (isFree[i])
                    {
                        double ux = Math.Cos(theta[i] - dTheta), uy = Math.Sin(theta[i] - dTheta);
                        length[i] += FreeLengthSigmaFt * FreeLengthSigmaFt * (ux * lx + uy * ly);
                        if (length[i] <= 0)
                        {
                            result.FailureReason = $"face {i} (no legal dimension) collapsed while closing the figure";
                            return result;
                        }
                    }
                }
                adjusted = true;
                (ex, ey) = Misclosure(theta, length);
            }

            // Walk the adjusted traverse from face 0's digitized start; snap the last
            // residual (numerically negligible by now) onto the closing vertex.
            LotPoint current = orderedFaces[0].Start;
            result.Ring.Add(current);
            for (int i = 0; i < n - 1; i++)
            {
                current = new LotPoint(current.X + length[i] * Math.Cos(theta[i]), current.Y + length[i] * Math.Sin(theta[i]));
                result.Ring.Add(current);
            }
            for (int i = 0; i < n; i++)
            {
                result.FaceLengthsFt.Add(length[i]);
                result.BearingChangesDeg.Add(deltaTheta[i] * 180.0 / Math.PI);
            }

            if (adjusted && result.ClosureErrorFt > 0.05)
            {
                result.Warnings.Add(
                    $"misclosure {result.ClosureErrorFt:0.##} ft closed by rotating faces up to {result.MaxBearingChangeDeg:0.00}° " +
                    $"(legal lengths kept exact{(isFree.Any(f => f) ? "; faces without a legal dimension took length" : "")})");
            }
            return result;
        }

        private static (double, double) Misclosure(double[] theta, double[] length)
        {
            double ex = 0, ey = 0;
            for (int i = 0; i < theta.Length; i++)
            {
                ex += length[i] * Math.Cos(theta[i]);
                ey += length[i] * Math.Sin(theta[i]);
            }
            return (ex, ey);
        }
    }
}
