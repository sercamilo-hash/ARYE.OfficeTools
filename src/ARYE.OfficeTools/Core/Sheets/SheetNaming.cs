using System.Globalization;
using System.Text.RegularExpressions;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>
    /// Naming conventions of the sheet package generator. Sheet names are set
    /// on creation only — a user's later edits are never rewritten.
    /// </summary>
    public static class SheetNaming
    {
        /// <summary>
        /// "PACKAGE NAME — FIRSTLEVEL TO LASTLEVEL" uppercase, e.g.
        /// "GROSS — CELLAR TO L05"; a single level drops the range.
        /// </summary>
        public static string ComposeSheetName(string packageName, string firstLevelName, string lastLevelName)
        {
            string package = Upper(packageName);
            string first = Upper(firstLevelName);
            string last = Upper(lastLevelName);
            if (first.Length == 0)
            {
                return package;
            }
            return first == last || last.Length == 0
                ? $"{package} — {first}"
                : $"{package} — {first} TO {last}";
        }

        /// <summary>
        /// A level name without its sort prefix: "01_1ST FL." becomes "1ST FL.", "24 24TH FL." becomes
        /// "24TH FL.", "00 CELLAR" becomes "CELLAR". A name that does not start with digits plus a
        /// separator is kept as is. Always uppercase.
        /// </summary>
        public static string StripLevelNumber(string levelName)
        {
            string name = (levelName ?? string.Empty).Trim();
            Match prefix = Regex.Match(name, @"^\d+[\s_\-]+(?=\S)");
            return (prefix.Success ? name.Substring(prefix.Length) : name).ToUpper(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The label of a view that stands for a run of levels: "3RD TO 7TH FL." from "03_3RD FL." and
        /// "07_7TH FL."; a single level, or two names that do not share a trailing word, keep their full labels.
        /// </summary>
        public static string RangeLabel(string firstLevelName, string lastLevelName)
        {
            string first = StripLevelNumber(firstLevelName);
            string last = StripLevelNumber(lastLevelName);
            if (first.Length == 0)
            {
                return last;
            }
            if (last.Length == 0 || first == last)
            {
                return first;
            }
            int firstSplit = first.IndexOf(' ');
            int lastSplit = last.IndexOf(' ');
            if (firstSplit > 0 && lastSplit > 0 && first.Substring(firstSplit) == last.Substring(lastSplit))
            {
                return $"{first.Substring(0, firstSplit)} TO {last}";
            }
            return $"{first} TO {last}";
        }

        /// <summary>
        /// Name of a generated view: the level (or range) label, then the set's own text when it has one:
        /// "1ST FL.", "2ND FL. GROSS AREA", "3RD TO 7TH FL. DEDUCTION PLAN".
        /// </summary>
        public static string ComposeViewName(string viewLabel, string suffix)
        {
            string label = Upper(viewLabel);
            string tail = Upper(suffix);
            return tail.Length == 0 ? label : label + " " + tail;
        }

        /// <summary>"FL." spelled out: "2ND FL." becomes "2ND FLOOR", "8TH TO 9TH FL. LOWER" keeps its LOWER. Anything else is left alone.</summary>
        public static string FloorWord(string label)
        {
            string text = (label ?? string.Empty).Trim();
            return Regex.Replace(text, @"\bFL\.?(?=\s|$)", "FLOOR", RegexOptions.IgnoreCase).ToUpper(CultureInfo.InvariantCulture);
        }

        /// <summary>The level part of a table name: "02 2ND FL." becomes "2ND FLOOR", "00 CELLAR" stays "CELLAR".</summary>
        public static string FloorLabel(string levelName) => FloorWord(StripLevelNumber(levelName));

        /// <summary>The same for a run of levels: "03_3RD FL." to "07_7TH FL." becomes "3RD TO 7TH FLOOR".</summary>
        public static string FloorRangeLabel(string firstLevelName, string lastLevelName) =>
            FloorWord(RangeLabel(firstLevelName, lastLevelName));

        public const string DefaultTableNamePattern = "{set} - {building} - {level}";

        /// <summary>
        /// Name of a generated area table. Tokens: {set} the set's name, {building} the building's name in
        /// Zoning Floor Area, {building value} the text the areas carry in their Building parameter, {level}
        /// the level or range label. A blank pattern uses <see cref="DefaultTableNamePattern"/>. Always uppercase,
        /// and without the characters Revit refuses in a view name.
        /// </summary>
        public static string ComposeTableName(string pattern, string setName, string buildingName, string buildingValue, string levelLabel)
        {
            string text = string.IsNullOrWhiteSpace(pattern) ? DefaultTableNamePattern : pattern;
            text = ReplaceToken(text, "{building value}", string.IsNullOrWhiteSpace(buildingValue) ? buildingName : buildingValue);
            text = ReplaceToken(text, "{building}", buildingName);
            text = ReplaceToken(text, "{set}", setName);
            text = ReplaceToken(text, "{level}", levelLabel);
            foreach (char bad in new[] { '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' })
            {
                text = text.Replace(bad, ' ');
            }
            return Regex.Replace(text, @"\s+", " ").Trim().ToUpper(CultureInfo.InvariantCulture);
        }

        private static string ReplaceToken(string text, string token, string value) =>
            Regex.Replace(text, Regex.Escape(token), (value ?? string.Empty).Replace("$", "$$"), RegexOptions.IgnoreCase);

        private static string Upper(string value) =>
            (value ?? string.Empty).Trim().ToUpper(CultureInfo.InvariantCulture);
    }
}
