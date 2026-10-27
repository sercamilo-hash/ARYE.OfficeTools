using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>One lot side, every length anyone claims for it.</summary>
    public sealed class FaceComparison
    {
        public int FaceIndex { get; set; }

        public bool IsStreetFace { get; set; }

        public string StreetName { get; set; } = string.Empty;

        /// <summary>Dimension written on the official tax map. 0 = none.</summary>
        public double LegalFt { get; set; }

        /// <summary>Length of the digitized DTM line.</summary>
        public double DigitizedFt { get; set; }

        public double DifferenceFt => LegalFt > 0 ? DigitizedFt - LegalFt : 0;

        public double DifferencePct => LegalFt > 0 ? Math.Abs(DifferenceFt) / LegalFt : 0;

        public bool IsDiscrepant(double tolerancePct) => LegalFt > 0 && DigitizedFt > 0 && DifferencePct > tolerancePct;

        public string RoleLabel => IsStreetFace
            ? (string.IsNullOrWhiteSpace(StreetName) ? "street" : StreetName)
            : "interior";
    }

    /// <summary>
    /// Side-by-side table of what the tax map says each side measures versus
    /// what its digitized line spans. This is the per-face version of
    /// <see cref="LotDimensionCheck"/>: the record has two numbers, the map has
    /// one per side, and the user deserves both.
    /// </summary>
    public static class LotFaceComparison
    {
        public const double DefaultTolerancePct = 0.01;

        public static List<FaceComparison> Compare(IReadOnlyList<NycLotFace> orderedFaces, IReadOnlyList<string> streetNames = null)
        {
            var rows = new List<FaceComparison>();
            if (orderedFaces == null)
            {
                return rows;
            }
            for (int i = 0; i < orderedFaces.Count; i++)
            {
                NycLotFace f = orderedFaces[i];
                rows.Add(new FaceComparison
                {
                    FaceIndex = i,
                    IsStreetFace = f.IsStreetFace,
                    StreetName = streetNames != null && i < streetNames.Count ? streetNames[i] ?? string.Empty : string.Empty,
                    LegalFt = f.LegalLengthFt,
                    DigitizedFt = f.MeasuredLengthFt
                });
            }
            return rows;
        }

        public static IEnumerable<string> Describe(IEnumerable<FaceComparison> rows, double tolerancePct = DefaultTolerancePct)
        {
            foreach (FaceComparison r in rows)
            {
                string legal = r.LegalFt > 0 ? $"{r.LegalFt:0.##}' legal" : "no legal dimension";
                string flag = r.IsDiscrepant(tolerancePct)
                    ? $"  ⚠ {(r.DifferenceFt >= 0 ? "+" : "")}{r.DifferenceFt:0.#}' ({r.DifferencePct:P1})"
                    : string.Empty;
                yield return $"Face {r.FaceIndex + 1} ({r.RoleLabel}): {legal} · {r.DigitizedFt:0.##}' digitized{flag}";
            }
        }

        public static bool AnyDiscrepant(IEnumerable<FaceComparison> rows, double tolerancePct = DefaultTolerancePct)
        {
            foreach (FaceComparison r in rows)
            {
                if (r.IsDiscrepant(tolerancePct))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
