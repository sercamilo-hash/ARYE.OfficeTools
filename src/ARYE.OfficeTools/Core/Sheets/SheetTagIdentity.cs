using System;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>
    /// Composes/parses the identity strings stored in the per-element tag of
    /// views and sheets created by the sheet package generator. The tag's
    /// building/group fields carry BuildingId/PackageId; this string carries
    /// the rest of the identity:
    ///   views  → { SourceKind, SchemeUniqueId (empty for FloorPlans), LevelUniqueId }
    ///   sheets → { SheetIndex } (0-based position in the package's series)
    /// </summary>
    public static class SheetTagIdentity
    {
        public const string ViewMarker = "zfa-pkg-view";
        public const string SheetMarker = "zfa-pkg-sheet";
        private const char Separator = '|';

        public static string ComposeView(SheetSourceKind kind, string schemeUniqueId, string levelUniqueId)
        {
            if (string.IsNullOrEmpty(levelUniqueId))
            {
                throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
            }
            return string.Join(Separator.ToString(),
                ViewMarker, kind.ToString(), schemeUniqueId ?? string.Empty, levelUniqueId);
        }

        public static bool TryParseView(string value,
            out SheetSourceKind kind, out string schemeUniqueId, out string levelUniqueId)
        {
            kind = SheetSourceKind.FloorPlans;
            schemeUniqueId = null;
            levelUniqueId = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string[] parts = value.Split(Separator);
            if (parts.Length != 4 || parts[0] != ViewMarker)
            {
                return false;
            }
            if (parts[1] == SheetSourceKind.FloorPlans.ToString())
            {
                kind = SheetSourceKind.FloorPlans;
            }
            else if (parts[1] == SheetSourceKind.AreaPlans.ToString())
            {
                kind = SheetSourceKind.AreaPlans;
            }
            else if (parts[1] == SheetSourceKind.CeilingPlans.ToString())
            {
                kind = SheetSourceKind.CeilingPlans;
            }
            else
            {
                return false;
            }
            if (parts[3].Length == 0)
            {
                return false;
            }
            schemeUniqueId = parts[2];
            levelUniqueId = parts[3];
            return true;
        }

        public static string ComposeSheet(int sheetIndex)
        {
            if (sheetIndex < 0)
            {
                throw new ArgumentException("Sheet index cannot be negative.", nameof(sheetIndex));
            }
            return SheetMarker + Separator + sheetIndex;
        }

        /// <summary>
        /// A sheet that carries one additional view alone (1×1). Distinct from a
        /// matrix sheet so a re-run never tries to fill its "free" slots with
        /// plan views. Shares the package's sheet index series.
        /// </summary>
        public const string OwnSheetMarker = "zfa-pkg-ownsheet";

        public static string ComposeOwnSheet(int sheetIndex, string viewUniqueId)
        {
            if (sheetIndex < 0)
            {
                throw new ArgumentException("Sheet index cannot be negative.", nameof(sheetIndex));
            }
            return OwnSheetMarker + Separator + sheetIndex + Separator + (viewUniqueId ?? string.Empty);
        }

        public static bool TryParseOwnSheet(string value, out int sheetIndex, out string viewUniqueId)
        {
            sheetIndex = -1;
            viewUniqueId = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string[] parts = value.Split(Separator);
            if (parts.Length != 3 || parts[0] != OwnSheetMarker || !int.TryParse(parts[1], out sheetIndex) || sheetIndex < 0)
            {
                return false;
            }
            viewUniqueId = parts[2];
            return true;
        }

        public static bool TryParseSheet(string value, out int sheetIndex)
        {
            sheetIndex = -1;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string[] parts = value.Split(Separator);
            return parts.Length == 2
                && parts[0] == SheetMarker
                && int.TryParse(parts[1], out sheetIndex)
                && sheetIndex >= 0;
        }
    }
}
