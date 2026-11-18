using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    /// <summary>The arbitrary Columns × Rows API behind the sheet package matrix.</summary>
    public class SheetSlotLayoutGridTests
    {
        // Usable area of a 36x24 D-size sheet after titleblock strip and margins.
        private const double UsableWidth = 32.0;
        private const double UsableHeight = 22.0;

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        public void Grid_ProducesExactCount_InsideUsableArea_NoOverlaps(int columns, int rows)
        {
            IReadOnlyList<SlotRect> slots =
                SheetSlotLayout.ComputeGrid(UsableWidth, UsableHeight, columns, rows);

            Assert.Equal(columns * rows, slots.Count);

            foreach (SlotRect slot in slots)
            {
                Assert.True(slot.LeftIn >= -1e-9, "slot escapes left edge");
                Assert.True(slot.RightIn <= UsableWidth + 1e-9, "slot escapes right edge");
                Assert.True(slot.BottomIn >= -1e-9, "slot escapes bottom edge");
                Assert.True(slot.TopIn <= UsableHeight + 1e-9, "slot escapes top edge");
                Assert.True(slot.WidthIn > 0 && slot.HeightIn > 0);
            }

            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    bool overlaps =
                        slots[i].LeftIn < slots[j].RightIn - 1e-9 &&
                        slots[j].LeftIn < slots[i].RightIn - 1e-9 &&
                        slots[i].BottomIn < slots[j].TopIn - 1e-9 &&
                        slots[j].BottomIn < slots[i].TopIn - 1e-9;
                    Assert.False(overlaps, $"slots {i} and {j} overlap");
                }
            }
        }

        [Theory]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(3, 3)]
        public void Grid_ComesInReadingOrder_TopLeftFirst(int columns, int rows)
        {
            IReadOnlyList<SlotRect> slots =
                SheetSlotLayout.ComputeGrid(UsableWidth, UsableHeight, columns, rows);

            for (int index = 1; index < slots.Count; index++)
            {
                int row = index / columns;
                int previousRow = (index - 1) / columns;
                if (row == previousRow)
                {
                    Assert.True(slots[index].CenterXIn > slots[index - 1].CenterXIn,
                        $"slot {index} does not advance rightward");
                    Assert.Equal(slots[index].CenterYIn, slots[index - 1].CenterYIn, 9);
                }
                else
                {
                    Assert.True(slots[index].CenterYIn < slots[index - 1].CenterYIn,
                        $"slot {index} does not drop to the next row");
                }
            }
        }

        [Theory]
        [InlineData(0, 2)]
        [InlineData(5, 2)]
        [InlineData(2, 0)]
        [InlineData(2, 5)]
        public void Grid_OutOfRangeSide_Throws(int columns, int rows)
        {
            Assert.Throws<ArgumentException>(
                () => SheetSlotLayout.ComputeGrid(UsableWidth, UsableHeight, columns, rows));
        }

        [Fact]
        public void Grid_AllSlotsEqualSize()
        {
            IReadOnlyList<SlotRect> slots =
                SheetSlotLayout.ComputeGrid(UsableWidth, UsableHeight, 3, 3);
            Assert.True(slots.All(s => Math.Abs(s.WidthIn - slots[0].WidthIn) < 1e-9));
            Assert.True(slots.All(s => Math.Abs(s.HeightIn - slots[0].HeightIn) < 1e-9));
        }

        [Fact]
        public void LegacySlotCounts_StillMapToTheSameGrids()
        {
            // 6 slots was 3×2 — the legacy API must keep producing it.
            IReadOnlyList<SlotRect> legacy = SheetSlotLayout.Compute(UsableWidth, UsableHeight, 6);
            IReadOnlyList<SlotRect> grid = SheetSlotLayout.ComputeGrid(UsableWidth, UsableHeight, 3, 2);
            Assert.Equal(grid.Count, legacy.Count);
            for (int i = 0; i < grid.Count; i++)
            {
                Assert.Equal(grid[i].CenterXIn, legacy[i].CenterXIn, 9);
                Assert.Equal(grid[i].CenterYIn, legacy[i].CenterYIn, 9);
            }
        }
    }
}
