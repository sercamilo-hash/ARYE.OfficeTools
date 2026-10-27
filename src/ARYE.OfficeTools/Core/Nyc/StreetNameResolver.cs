using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>
    /// MapPLUTO has no "street per side of block" field — only a house address
    /// per lot. This turns that into a proposal per cardinal side: parse the
    /// street name out of each lot's address, classify the lot against the
    /// block's own bounding box, and take the majority name per side. Always
    /// returns all four sides, even when a side has no data — the caller (the
    /// UI) is what turns "proposal" into "confirmed", never this resolver.
    /// </summary>
    public static class StreetNameResolver
    {
        private static readonly Regex LeadingHouseNumber =
            new Regex(@"^\s*\d+[A-Za-z]?(-\d+[A-Za-z]?)?\s+", RegexOptions.Compiled);

        /// <summary>"123-45 QUEENS BLVD" -> "QUEENS BLVD". Returns the input trimmed if no house number is found.</summary>
        public static string ParseStreetName(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }
            string trimmed = address.Trim();
            Match match = LeadingHouseNumber.Match(trimmed);
            string street = match.Success ? trimmed.Substring(match.Length) : trimmed;
            return street.Trim();
        }

        public static List<StreetFrontage> ResolveFrontages(NycBlockContext context)
        {
            var bySide = new Dictionary<CompassSide, List<string>>
            {
                [CompassSide.North] = new List<string>(),
                [CompassSide.South] = new List<string>(),
                [CompassSide.East] = new List<string>(),
                [CompassSide.West] = new List<string>()
            };

            double minX = context.MinX, minY = context.MinY, maxX = context.MaxX, maxY = context.MaxY;

            foreach (NycLotInfo lot in context.AllLots)
            {
                string street = ParseStreetName(lot.Address);
                if (string.IsNullOrEmpty(street) || lot.PolygonRings.Count == 0)
                {
                    continue;
                }
                CompassSide side = LotGeometry.ClassifySide(lot.Centroid, minX, minY, maxX, maxY);
                bySide[side].Add(street.ToUpperInvariant());
            }

            var result = new List<StreetFrontage>();
            foreach (CompassSide side in new[] { CompassSide.North, CompassSide.South, CompassSide.East, CompassSide.West })
            {
                var groups = bySide[side]
                    .GroupBy(s => s)
                    .OrderByDescending(g => g.Count())
                    .ToList();
                IGrouping<string, string> top = groups.FirstOrDefault();
                result.Add(new StreetFrontage
                {
                    Side = side,
                    StreetName = top?.Key ?? string.Empty,
                    SourceLotCount = top?.Count() ?? 0
                });
            }
            return result;
        }
    }
}
