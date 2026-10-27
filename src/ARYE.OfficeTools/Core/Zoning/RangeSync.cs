using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Zoning
{
    /// <summary>Where a whole range stands against its source, as the Step 2 list shows it.</summary>
    public enum RangeSyncState
    {
        /// <summary>No sync has ever been recorded and the copies do not match the source.</summary>
        NeverSynced = 0,

        /// <summary>Every copy matches the source.</summary>
        InSync = 1,

        /// <summary>The source was edited after the last sync (its fingerprint moved).</summary>
        SourceChanged = 2,

        /// <summary>The source is unchanged since the last sync, but a copy was edited by hand.</summary>
        CopyEdited = 3,

        /// <summary>
        /// Copies differ from the source and the cause is unknown: the last
        /// run predates change tracking, so there is no fingerprint to compare.
        /// </summary>
        CopiesDiffer = 4
    }

    /// <summary>One level row of the check table.</summary>
    public enum LevelSyncState
    {
        Source = 0,
        Matches = 1,
        Differs = 2,

        /// <summary>No areas in scope on the copy (and no area plan, or an empty one).</summary>
        Empty = 3
    }

    /// <summary>One area as the comparison sees it: what it is called, what it counts as, how big.</summary>
    public sealed class AreaSignature
    {
        public string Name { get; set; }

        /// <summary>Raw "ZFA Category" text — compared trimmed and case-insensitively.</summary>
        public string Category { get; set; }

        public double SquareFeet { get; set; }
    }

    /// <summary>A boundary line reduced to three plan points (start, middle, end) — enough to tell arcs apart.</summary>
    public sealed class LineSignature
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double Xm { get; set; }
        public double Ym { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    /// <summary>What one level's area plan holds, limited to the building's scope.</summary>
    public sealed class LevelSnapshot
    {
        /// <summary>Area scheme of the plan this snapshot was read from ("Gross Building", "Deductions"…).</summary>
        public string SchemeName { get; set; }

        public string LevelUniqueId { get; set; }

        public string LevelName { get; set; }

        public bool HasAreaPlan { get; set; }

        public List<AreaSignature> Areas { get; set; } = new List<AreaSignature>();

        public List<LineSignature> Lines { get; set; } = new List<LineSignature>();

        /// <summary>Areas in scope that carry no propagation tag (drawn by hand).</summary>
        public int HandDrawnAreaCount { get; set; }
    }

    /// <summary>One source and the levels copied from it. Standard ranges have one; duplex ranges two.</summary>
    public sealed class SubRangeSnapshot
    {
        public LevelSnapshot Source { get; set; }

        public List<LevelSnapshot> Targets { get; set; } = new List<LevelSnapshot>();
    }

    public sealed class LevelSyncResult
    {
        public string SchemeName { get; set; }

        public string LevelUniqueId { get; set; }

        public string LevelName { get; set; }

        public LevelSyncState State { get; set; }

        public int AreaCount { get; set; }

        public double GrossSquareFeet { get; set; }

        public double DeductionSquareFeet { get; set; }

        public double NetSquareFeet => GrossSquareFeet - DeductionSquareFeet;

        /// <summary>Plain-language differences against the source, empty when it matches.</summary>
        public List<string> Differences { get; set; } = new List<string>();

        public string Detail => string.Join("; ", Differences);
    }

    public sealed class RangeSyncReport
    {
        public RangeSyncState State { get; set; }

        /// <summary>Fingerprint of the source(s) as they are now — stored on the next sync.</summary>
        public string CurrentFingerprint { get; set; }

        /// <summary>Sources first, then their copies, in the order the snapshot listed them.</summary>
        public List<LevelSyncResult> Levels { get; set; } = new List<LevelSyncResult>();

        /// <summary>Source areas that report 0 sf — never copied, always shown.</summary>
        public List<string> UnenclosedSourceAreas { get; set; } = new List<string>();

        public int CopyCount => Levels.Count(l => l.State != LevelSyncState.Source);

        public int MismatchCount => Levels.Count(l =>
            l.State == LevelSyncState.Differs || l.State == LevelSyncState.Empty);
    }

    /// <summary>
    /// Step 2's check: is every level of a range a faithful copy of its
    /// source? Pure — the Revit side builds the snapshots, this decides. A
    /// copy matches when it holds the same areas (name + ZFA Category, as a
    /// multiset) with the same square footage within <see cref="ToleranceSqFt"/>,
    /// and the same number of boundary lines.
    /// </summary>
    public static class RangeSyncAnalyzer
    {
        public const double ToleranceSqFt = 0.01;

        /// <summary>Decimal places kept for line coordinates in the fingerprint (0.001 ft).</summary>
        private const int CoordinateDecimals = 3;

        public static RangeSyncReport Analyze(IReadOnlyList<SubRangeSnapshot> subs,
            CategoryCatalog catalog, PropagationRunRecord lastRun)
        {
            if (subs == null) throw new ArgumentNullException(nameof(subs));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var report = new RangeSyncReport
            {
                CurrentFingerprint = Fingerprint(subs.Select(s => s.Source))
            };

            // Per scheme, in the order the schemes first appear: its source(s)
            // first, then its copies — the check table groups by scheme.
            foreach (IGrouping<string, SubRangeSnapshot> scheme in subs.GroupBy(s => s.Source.SchemeName ?? string.Empty))
            {
                foreach (SubRangeSnapshot sub in scheme)
                {
                    LevelSyncResult source = Summarize(sub.Source, catalog);
                    source.State = LevelSyncState.Source;
                    report.Levels.Add(source);

                    foreach (AreaSignature area in sub.Source.Areas.Where(a => a.SquareFeet <= 0))
                    {
                        report.UnenclosedSourceAreas.Add(string.IsNullOrEmpty(sub.Source.SchemeName)
                            ? $"{area.Name} ({sub.Source.LevelName})"
                            : $"{area.Name} ({sub.Source.LevelName}, {sub.Source.SchemeName})");
                    }
                }

                foreach (SubRangeSnapshot sub in scheme)
                {
                    foreach (LevelSnapshot target in sub.Targets)
                    {
                        LevelSyncResult copy = CompareLevel(sub.Source, target, catalog);
                        copy.SchemeName = sub.Source.SchemeName;
                        report.Levels.Add(copy);
                    }
                }
            }

            report.State = DecideState(report, lastRun);
            return report;
        }

        /// <summary>Compares one copy against its source. Unenclosed source areas are never copied, so they are not expected.</summary>
        public static LevelSyncResult CompareLevel(LevelSnapshot source, LevelSnapshot target,
            CategoryCatalog catalog)
        {
            LevelSyncResult result = Summarize(target, catalog);
            List<AreaSignature> expected = source.Areas.Where(a => a.SquareFeet > 0).ToList();

            if (target.Areas.Count == 0 && expected.Count > 0)
            {
                result.State = LevelSyncState.Empty;
                result.Differences.Add(target.HasAreaPlan ? "No areas yet" : "No area plan yet");
                return result;
            }

            var unmatched = new List<AreaSignature>(target.Areas);
            foreach (AreaSignature wanted in expected)
            {
                AreaSignature sameKey = unmatched
                    .Where(a => Key(a) == Key(wanted))
                    .OrderBy(a => Math.Abs(a.SquareFeet - wanted.SquareFeet))
                    .FirstOrDefault();
                if (sameKey == null)
                {
                    result.Differences.Add($"Missing {Label(wanted)}");
                    continue;
                }
                unmatched.Remove(sameKey);
                double delta = sameKey.SquareFeet - wanted.SquareFeet;
                if (Math.Abs(delta) > ToleranceSqFt)
                {
                    result.Differences.Add(
                        $"{wanted.Name}: {Sf(sameKey.SquareFeet)} vs {Sf(wanted.SquareFeet)} sf on source ({SignedSf(delta)})");
                }
            }
            foreach (AreaSignature extra in unmatched)
            {
                result.Differences.Add($"Not on source: {Label(extra)}, {Sf(extra.SquareFeet)} sf");
            }

            if (target.Lines.Count != source.Lines.Count)
            {
                result.Differences.Add(
                    $"{target.Lines.Count} boundary lines vs {source.Lines.Count} on source");
            }

            result.State = result.Differences.Count == 0 ? LevelSyncState.Matches : LevelSyncState.Differs;
            return result;
        }

        /// <summary>
        /// Stable hash of what the source holds: areas (name, category, sf to
        /// the hundredth) and boundary lines (direction-free, to the
        /// thousandth of a foot), both sorted, so collector order never moves it.
        /// </summary>
        public static string Fingerprint(IEnumerable<LevelSnapshot> sources)
        {
            var text = new StringBuilder();
            foreach (LevelSnapshot source in sources)
            {
                text.Append("L|").Append(source.SchemeName).Append('|').Append(source.LevelUniqueId).Append('\n');
                foreach (string area in source.Areas
                    .Select(a => Key(a) + "|" + a.SquareFeet.ToString("F2", CultureInfo.InvariantCulture))
                    .OrderBy(s => s, StringComparer.Ordinal))
                {
                    text.Append("A|").Append(area).Append('\n');
                }
                foreach (string line in source.Lines
                    .Select(LineKey)
                    .OrderBy(s => s, StringComparer.Ordinal))
                {
                    text.Append("B|").Append(line).Append('\n');
                }
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return hex.ToString();
            }
        }

        private static RangeSyncState DecideState(RangeSyncReport report, PropagationRunRecord lastRun)
        {
            bool allMatch = report.Levels.All(l =>
                l.State == LevelSyncState.Source || l.State == LevelSyncState.Matches);

            if (lastRun == null)
            {
                // Copies identical to the source count as in sync even if they
                // were never made by this tool — the numbers are what matter.
                return allMatch && report.CopyCount > 0 ? RangeSyncState.InSync : RangeSyncState.NeverSynced;
            }
            if (string.IsNullOrEmpty(lastRun.SourceFingerprint))
            {
                return allMatch ? RangeSyncState.InSync : RangeSyncState.CopiesDiffer;
            }
            if (!string.Equals(lastRun.SourceFingerprint, report.CurrentFingerprint, StringComparison.Ordinal))
            {
                return RangeSyncState.SourceChanged;
            }
            return allMatch ? RangeSyncState.InSync : RangeSyncState.CopyEdited;
        }

        private static LevelSyncResult Summarize(LevelSnapshot level, CategoryCatalog catalog)
        {
            var result = new LevelSyncResult
            {
                SchemeName = level.SchemeName,
                LevelUniqueId = level.LevelUniqueId,
                LevelName = level.LevelName,
                AreaCount = level.Areas.Count
            };
            foreach (AreaSignature area in level.Areas)
            {
                CategoryDefinition category = catalog.Match(area.Category);
                if (category == null || category.Role == CategoryRole.MainGross)
                {
                    // Unclassified counts as gross, flagged elsewhere — the matrix rule.
                    result.GrossSquareFeet += area.SquareFeet;
                }
                else if (category.Role == CategoryRole.Deduction)
                {
                    result.DeductionSquareFeet += area.SquareFeet;
                }
            }
            return result;
        }

        private static string Key(AreaSignature area) =>
            Normalize(area.Name) + "|" + Normalize(area.Category);

        private static string Normalize(string text) =>
            (text ?? string.Empty).Trim().ToUpperInvariant();

        private static string Label(AreaSignature area) =>
            string.IsNullOrWhiteSpace(area.Category)
                ? $"{area.Name} (unclassified)"
                : $"{area.Name} ({area.Category.Trim()})";

        private static string LineKey(LineSignature line)
        {
            string start = Point(line.X1, line.Y1);
            string end = Point(line.X2, line.Y2);
            // A line drawn the other way round is the same line.
            if (string.CompareOrdinal(start, end) > 0)
            {
                (start, end) = (end, start);
            }
            return start + ";" + Point(line.Xm, line.Ym) + ";" + end;
        }

        private static string Point(double x, double y) => Coordinate(x) + "," + Coordinate(y);

        // "+ 0.0" folds -0.000 into 0.000, so a coordinate that rounds to zero
        // from either side hashes the same.
        private static string Coordinate(double value) =>
            (Math.Round(value, CoordinateDecimals) + 0.0)
                .ToString("F" + CoordinateDecimals, CultureInfo.InvariantCulture);

        private static string Sf(double value) => value.ToString("N2", CultureInfo.InvariantCulture);

        private static string SignedSf(double value) =>
            (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("N2", CultureInfo.InvariantCulture);
    }
}
