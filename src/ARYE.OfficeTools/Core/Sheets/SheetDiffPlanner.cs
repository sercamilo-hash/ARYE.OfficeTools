using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>
    /// The three buckets of a diff-based sheet run. Keys are opaque level
    /// identity strings (level UniqueIds in practice).
    /// </summary>
    public sealed class SheetDiffPlan
    {
        /// <summary>Desired but not existing — views to create, in desired order.</summary>
        public List<string> CreateKeys { get; } = new List<string>();

        /// <summary>Desired and existing — views updated in place, in desired order.</summary>
        public List<string> UpdateKeys { get; } = new List<string>();

        /// <summary>Existing but no longer desired — kept exactly as they are, reported.</summary>
        public List<string> OrphanKeys { get; } = new List<string>();
    }

    /// <summary>Where one newly created view lands.</summary>
    public sealed class SlotAssignment
    {
        /// <summary>True: the view fills an empty slot on the package's existing last sheet.</summary>
        public bool OnExistingLastSheet { get; set; }

        /// <summary>0-based index among the run's NEW sheets; -1 when on the existing last sheet.</summary>
        public int NewSheetOrdinal { get; set; } = -1;

        /// <summary>Slot index in reading order on the target sheet.</summary>
        public int SlotIndex { get; set; }
    }

    /// <summary>
    /// Pure diff planning for sheet package runs: which views to create,
    /// update or keep, and which slot each new view fills. Existing views and
    /// viewports are never moved or deleted — new views only fill empty slots
    /// on the existing last sheet, then continue onto new sheets.
    /// </summary>
    public static class SheetDiffPlanner
    {
        public static SheetDiffPlan Plan(
            IReadOnlyList<string> desiredKeys, IEnumerable<string> existingKeys)
        {
            if (desiredKeys == null) throw new ArgumentNullException(nameof(desiredKeys));
            if (existingKeys == null) throw new ArgumentNullException(nameof(existingKeys));

            var existing = new List<string>(existingKeys);
            var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);
            var desiredSet = new HashSet<string>(desiredKeys, StringComparer.Ordinal);

            var plan = new SheetDiffPlan();
            foreach (string key in desiredKeys)
            {
                if (existingSet.Contains(key))
                {
                    plan.UpdateKeys.Add(key);
                }
                else
                {
                    plan.CreateKeys.Add(key);
                }
            }
            foreach (string key in existing)
            {
                if (!desiredSet.Contains(key))
                {
                    plan.OrphanKeys.Add(key);
                }
            }
            return plan;
        }

        /// <summary>
        /// Assigns each of <paramref name="newViewCount"/> views a slot: the
        /// free slots at the tail of the existing last sheet first (occupied
        /// slots are counted from the front in reading order), then new sheets.
        /// </summary>
        public static IReadOnlyList<SlotAssignment> AssignSlots(
            int newViewCount, int slotsPerSheet, int freeSlotsOnLastExistingSheet)
        {
            if (newViewCount < 0) throw new ArgumentException("View count cannot be negative.", nameof(newViewCount));
            if (slotsPerSheet <= 0) throw new ArgumentException("Slots per sheet must be positive.", nameof(slotsPerSheet));

            int free = Math.Max(0, Math.Min(freeSlotsOnLastExistingSheet, slotsPerSheet));
            int occupied = slotsPerSheet - free;

            var assignments = new List<SlotAssignment>(newViewCount);
            int placedOnExisting = Math.Min(free, newViewCount);
            for (int i = 0; i < placedOnExisting; i++)
            {
                assignments.Add(new SlotAssignment
                {
                    OnExistingLastSheet = true,
                    SlotIndex = occupied + i
                });
            }
            for (int i = placedOnExisting; i < newViewCount; i++)
            {
                int overflowIndex = i - placedOnExisting;
                assignments.Add(new SlotAssignment
                {
                    OnExistingLastSheet = false,
                    NewSheetOrdinal = overflowIndex / slotsPerSheet,
                    SlotIndex = overflowIndex % slotsPerSheet
                });
            }
            return assignments;
        }

        /// <summary>How many new sheets a set of assignments needs.</summary>
        public static int CountNewSheets(IReadOnlyList<SlotAssignment> assignments)
        {
            int max = -1;
            foreach (SlotAssignment assignment in assignments)
            {
                if (!assignment.OnExistingLastSheet && assignment.NewSheetOrdinal > max)
                {
                    max = assignment.NewSheetOrdinal;
                }
            }
            return max + 1;
        }
    }
}
