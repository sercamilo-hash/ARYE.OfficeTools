using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.Core.Levels
{
    /// <summary>
    /// Pure range math behind Step 1's Floor Ranges: duplex pair generation,
    /// contiguity checks, and the mapping between a contiguous From–To range
    /// and the persisted <see cref="FloorGroup"/> shape. No Revit, no UI —
    /// everything here is testable against plain <see cref="LevelRef"/> lists.
    /// </summary>
    public static class FloorRangeMath
    {
        /// <summary>
        /// Pairs a bottom-up ordered range two-by-two: (0,1), (2,3), …
        /// An odd count generates every full pair and reports the topmost
        /// level as unpaired — a warning for the caller, never an error.
        /// </summary>
        public static DuplexPairing GeneratePairs(IReadOnlyList<LevelRef> rangeLevels)
        {
            if (rangeLevels == null)
            {
                throw new ArgumentNullException(nameof(rangeLevels));
            }

            var result = new DuplexPairing();
            int fullPairCount = rangeLevels.Count / 2;
            for (int i = 0; i < fullPairCount; i++)
            {
                result.Pairs.Add(new DuplexLevelPair
                {
                    Lower = rangeLevels[i * 2],
                    Upper = rangeLevels[i * 2 + 1]
                });
            }
            if (rangeLevels.Count % 2 == 1)
            {
                result.UnpairedLevel = rangeLevels[rangeLevels.Count - 1];
            }
            return result;
        }

        /// <summary>
        /// True when the member set occupies consecutive positions in the
        /// full ordered level list. An empty member set counts as contiguous.
        /// Members are matched by UniqueId.
        /// </summary>
        public static bool IsContiguous(
            IReadOnlyList<LevelRef> orderedLevels, ICollection<string> memberUniqueIds)
        {
            if (orderedLevels == null)
            {
                throw new ArgumentNullException(nameof(orderedLevels));
            }
            if (memberUniqueIds == null || memberUniqueIds.Count == 0)
            {
                return true;
            }

            int first = -1;
            int last = -1;
            int found = 0;
            for (int i = 0; i < orderedLevels.Count; i++)
            {
                if (memberUniqueIds.Contains(orderedLevels[i].UniqueId))
                {
                    if (first < 0)
                    {
                        first = i;
                    }
                    last = i;
                    found++;
                }
            }
            // Members missing from the ordered list can never be contiguous in it.
            return found == memberUniqueIds.Count && (last - first + 1) == found;
        }

        /// <summary>
        /// Typical range → persisted group: the source stays SourceLevel, every
        /// other range level becomes a target (bottom-up, source excluded).
        /// </summary>
        public static FloorGroup BuildStandardGroup(
            Guid id, string name, IReadOnlyList<LevelRef> rangeLevels, LevelRef sourceLevel)
        {
            if (rangeLevels == null)
            {
                throw new ArgumentNullException(nameof(rangeLevels));
            }
            if (sourceLevel == null)
            {
                throw new ArgumentNullException(nameof(sourceLevel));
            }

            return new FloorGroup
            {
                Id = id,
                Name = name,
                Kind = FloorGroupKind.Standard,
                SourceLevel = sourceLevel,
                TargetLevels = rangeLevels
                    .Where(l => l.UniqueId != sourceLevel.UniqueId)
                    .ToList(),
                TargetPairs = new List<DuplexLevelPair>()
            };
        }

        /// <summary>
        /// Duplex range → persisted group: pairs are regenerated from the range,
        /// the chosen pair becomes SourceLevel/SourceUpperLevel and the rest
        /// become TargetPairs. The unpaired odd level (if any) is not persisted.
        /// </summary>
        public static FloorGroup BuildDuplexGroup(
            Guid id, string name, IReadOnlyList<LevelRef> rangeLevels, int sourcePairIndex)
        {
            DuplexPairing pairing = GeneratePairs(rangeLevels);
            if (pairing.Pairs.Count == 0)
            {
                // A one-level duplex range persists as an empty duplex group —
                // the UI already shows the unpaired warning.
                return new FloorGroup
                {
                    Id = id,
                    Name = name,
                    Kind = FloorGroupKind.Duplex,
                    TargetPairs = new List<DuplexLevelPair>()
                };
            }

            if (sourcePairIndex < 0 || sourcePairIndex >= pairing.Pairs.Count)
            {
                sourcePairIndex = 0;
            }
            DuplexLevelPair source = pairing.Pairs[sourcePairIndex];
            return new FloorGroup
            {
                Id = id,
                Name = name,
                Kind = FloorGroupKind.Duplex,
                SourceLevel = source.Lower,
                SourceUpperLevel = source.Upper,
                TargetLevels = new List<LevelRef>(),
                TargetPairs = pairing.Pairs.Where((p, i) => i != sourcePairIndex).ToList()
            };
        }

        /// <summary>
        /// All levels a Standard group covers (source + targets), ordered
        /// bottom-up by elevation. From = first, To = last.
        /// </summary>
        public static List<LevelRef> CollectStandardMembers(FloorGroup group)
        {
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var members = new List<LevelRef>();
            if (group.SourceLevel != null)
            {
                members.Add(group.SourceLevel);
            }
            if (group.TargetLevels != null)
            {
                members.AddRange(group.TargetLevels.Where(l => l != null));
            }
            return Dedupe(members);
        }

        /// <summary>
        /// All levels a Duplex group covers (source pair + target pairs),
        /// ordered bottom-up by elevation. From = first, To = last.
        /// </summary>
        public static List<LevelRef> CollectDuplexMembers(FloorGroup group)
        {
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var members = new List<LevelRef>();
            if (group.SourceLevel != null)
            {
                members.Add(group.SourceLevel);
            }
            if (group.SourceUpperLevel != null)
            {
                members.Add(group.SourceUpperLevel);
            }
            if (group.TargetPairs != null)
            {
                foreach (DuplexLevelPair pair in group.TargetPairs)
                {
                    if (pair?.Lower != null)
                    {
                        members.Add(pair.Lower);
                    }
                    if (pair?.Upper != null)
                    {
                        members.Add(pair.Upper);
                    }
                }
            }
            return Dedupe(members);
        }

        /// <summary>
        /// Only the level(s) a group's own template comes from — SourceLevel for
        /// every kind, plus SourceUpperLevel for Duplex (its second, physically
        /// different plan). Never a propagation target: every target level of a
        /// Standard or Duplex group looks identical to its source, so diagramming
        /// "the range" only needs these, not one view per target.
        /// </summary>
        public static List<LevelRef> CollectSourceMembers(FloorGroup group)
        {
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var members = new List<LevelRef>();
            if (group.SourceLevel != null)
            {
                members.Add(group.SourceLevel);
            }
            if (group.SourceUpperLevel != null)
            {
                members.Add(group.SourceUpperLevel);
            }
            return Dedupe(members);
        }

        private static List<LevelRef> Dedupe(List<LevelRef> members)
        {
            var seen = new HashSet<string>();
            return members
                .Where(l => seen.Add(l.UniqueId))
                .OrderBy(l => l.ElevationFt)
                .ToList();
        }
    }

    /// <summary>Result of pairing a duplex range bottom-up two-by-two.</summary>
    public sealed class DuplexPairing
    {
        public List<DuplexLevelPair> Pairs { get; } = new List<DuplexLevelPair>();

        /// <summary>The topmost level when the range has an odd count; null otherwise.</summary>
        public LevelRef UnpairedLevel { get; set; }
    }
}
