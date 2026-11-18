using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class SheetSlotLayoutTests
    {
        // Usable area of a 36x24 D-size sheet after titleblock strip and margins.
        private const double UsableWidth = 32.0;
        private const double UsableHeight = 22.0;

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        public void EverySlotCount_ProducesExactCount_InsideUsableArea_NoOverlaps(int slotCount)
        {
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.Compute(UsableWidth, UsableHeight, slotCount);

            Assert.Equal(slotCount, slots.Count);

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

        [Fact]
        public void SlotsComeInReadingOrder_TopRowFirst()
        {
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.Compute(UsableWidth, UsableHeight, 6);
            // 3x2: first three share the top row, next three the bottom row.
            Assert.True(slots[0].CenterYIn > slots[3].CenterYIn);
            Assert.True(slots[0].CenterXIn < slots[1].CenterXIn);
            Assert.Equal(slots[0].CenterYIn, slots[2].CenterYIn, 9);
        }

        [Fact]
        public void UnsupportedCount_Throws()
        {
            Assert.Throws<ArgumentException>(() => SheetSlotLayout.Compute(UsableWidth, UsableHeight, 5));
        }

        [Fact]
        public void AllSlotsEqualSize()
        {
            foreach (int count in SheetSlotLayout.SupportedSlotCounts)
            {
                IReadOnlyList<SlotRect> slots = SheetSlotLayout.Compute(UsableWidth, UsableHeight, count);
                Assert.True(slots.All(s => Math.Abs(s.WidthIn - slots[0].WidthIn) < 1e-9));
                Assert.True(slots.All(s => Math.Abs(s.HeightIn - slots[0].HeightIn) < 1e-9));
            }
        }
    }
}
