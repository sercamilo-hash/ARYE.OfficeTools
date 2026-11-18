using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class SheetDiffPlannerTests
    {
        [Fact]
        public void Plan_SplitsDesiredAndExisting_IntoCreateUpdateOrphan()
        {
            SheetDiffPlan plan = SheetDiffPlanner.Plan(
                new[] { "L01", "L02", "L03", "L04" },
                new[] { "L02", "L04", "L99" });

            Assert.Equal(new[] { "L01", "L03" }, plan.CreateKeys);
            Assert.Equal(new[] { "L02", "L04" }, plan.UpdateKeys);
            Assert.Equal(new[] { "L99" }, plan.OrphanKeys);
        }

        [Fact]
        public void Plan_EmptyExisting_EverythingIsCreate()
        {
            SheetDiffPlan plan = SheetDiffPlanner.Plan(new[] { "A", "B" }, Array.Empty<string>());
            Assert.Equal(new[] { "A", "B" }, plan.CreateKeys);
            Assert.Empty(plan.UpdateKeys);
            Assert.Empty(plan.OrphanKeys);
        }

        [Fact]
        public void Plan_EmptyDesired_EverythingIsOrphan_NothingDeleted()
        {
            SheetDiffPlan plan = SheetDiffPlanner.Plan(Array.Empty<string>(), new[] { "A", "B" });
            Assert.Empty(plan.CreateKeys);
            Assert.Empty(plan.UpdateKeys);
            Assert.Equal(new[] { "A", "B" }, plan.OrphanKeys);
        }

        [Fact]
        public void AssignSlots_FillsExistingLastSheetFirst_ThenNewSheets()
        {
            // 6-slot sheets, last existing sheet has 2 free slots, 5 new views.
            IReadOnlyList<SlotAssignment> assignments = SheetDiffPlanner.AssignSlots(5, 6, 2);

            Assert.Equal(5, assignments.Count);
            // First two land on the existing sheet, in its tail slots 4 and 5.
            Assert.True(assignments[0].OnExistingLastSheet);
            Assert.Equal(4, assignments[0].SlotIndex);
            Assert.True(assignments[1].OnExistingLastSheet);
            Assert.Equal(5, assignments[1].SlotIndex);
            // Remaining three start a new sheet from slot 0.
            Assert.False(assignments[2].OnExistingLastSheet);
            Assert.Equal(0, assignments[2].NewSheetOrdinal);
            Assert.Equal(0, assignments[2].SlotIndex);
            Assert.Equal(2, assignments[4].SlotIndex);
            Assert.Equal(1, SheetDiffPlanner.CountNewSheets(assignments));
        }

        [Fact]
        public void AssignSlots_Overflow_ContinuesOntoFurtherNewSheets()
        {
            // 4-slot sheets, no existing sheet capacity, 9 new views → 3 new sheets.
            IReadOnlyList<SlotAssignment> assignments = SheetDiffPlanner.AssignSlots(9, 4, 0);

            Assert.All(assignments, a => Assert.False(a.OnExistingLastSheet));
            Assert.Equal(0, assignments[3].NewSheetOrdinal);
            Assert.Equal(3, assignments[3].SlotIndex);
            Assert.Equal(1, assignments[4].NewSheetOrdinal);
            Assert.Equal(0, assignments[4].SlotIndex);
            Assert.Equal(2, assignments[8].NewSheetOrdinal);
            Assert.Equal(0, assignments[8].SlotIndex);
            Assert.Equal(3, SheetDiffPlanner.CountNewSheets(assignments));
        }

        [Fact]
        public void AssignSlots_NoNewViews_NoNewSheets()
        {
            IReadOnlyList<SlotAssignment> assignments = SheetDiffPlanner.AssignSlots(0, 6, 3);
            Assert.Empty(assignments);
            Assert.Equal(0, SheetDiffPlanner.CountNewSheets(assignments));
        }

        [Fact]
        public void AssignSlots_NegativeFreeSlots_ClampedToZero()
        {
            IReadOnlyList<SlotAssignment> assignments = SheetDiffPlanner.AssignSlots(2, 4, -3);
            Assert.All(assignments, a => Assert.False(a.OnExistingLastSheet));
            Assert.Equal(0, assignments[0].SlotIndex);
        }

        [Fact]
        public void AssignSlots_AllFitOnExistingSheet()
        {
            IReadOnlyList<SlotAssignment> assignments = SheetDiffPlanner.AssignSlots(2, 6, 6);
            Assert.All(assignments, a => Assert.True(a.OnExistingLastSheet));
            Assert.Equal(new[] { 0, 1 }, assignments.Select(a => a.SlotIndex).ToArray());
            Assert.Equal(0, SheetDiffPlanner.CountNewSheets(assignments));
        }
    }
}
