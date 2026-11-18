using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    /// <summary>Dragged (unequal) matrix cells — equal stays the exact original grid.</summary>
    public class SheetSlotLayoutCustomCellsTests
    {
        private const double W = 32.0;
        private const double H = 22.0;

        [Theory]
        [InlineData(3, 2)]
        [InlineData(4, 3)]
        [InlineData(1, 1)]
        public void EmptyShares_ReproduceTheOriginalEqualGrid(int columns, int rows)
        {
            IReadOnlyList<SlotRect> original = SheetSlotLayout.ComputeGrid(W, H, columns, rows);
            IReadOnlyList<SlotRect> custom = SheetSlotLayout.ComputeGrid(W, H, columns, rows, new List<double>(), new List<double>());

            Assert.Equal(original.Count, custom.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].CenterXIn, custom[i].CenterXIn, 9);
                Assert.Equal(original[i].CenterYIn, custom[i].CenterYIn, 9);
                Assert.Equal(original[i].WidthIn, custom[i].WidthIn, 9);
                Assert.Equal(original[i].HeightIn, custom[i].HeightIn, 9);
            }
        }

        [Fact]
        public void WiderFirstColumn_GivesItMoreOfTheNetWidth_AndSlotsStillFillTheArea()
        {
            // 2 columns, first one takes 70% of the width left after the gutter.
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.ComputeGrid(W, H, 2, 1, new[] { 0.7 }, null);

            double net = W - SheetSlotLayout.GutterIn;
            Assert.Equal(net * 0.7, slots[0].WidthIn, 9);
            Assert.Equal(net * 0.3, slots[1].WidthIn, 9);
            Assert.Equal(0, slots[0].LeftIn, 9);
            Assert.Equal(W, slots[1].RightIn, 9);
            Assert.Equal(SheetSlotLayout.GutterIn, slots[1].LeftIn - slots[0].RightIn, 9);
        }

        [Fact]
        public void RowShares_AreMeasuredFromTheTop()
        {
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.ComputeGrid(W, H, 1, 2, null, new[] { 0.25 });

            double net = H - SheetSlotLayout.GutterIn;
            Assert.Equal(net * 0.25, slots[0].HeightIn, 9);   // first slot = top row = the short one
            Assert.Equal(net * 0.75, slots[1].HeightIn, 9);
            Assert.Equal(H, slots[0].TopIn, 9);
            Assert.Equal(0, slots[1].BottomIn, 9);
        }

        [Theory]
        [InlineData(new double[] { 0.8, 0.3 })]    // not increasing
        [InlineData(new double[] { 0.5 })]         // wrong count for 3 cells
        [InlineData(new double[] { 0.0, 0.5 })]    // zero-width first cell
        [InlineData(new double[] { 0.4, 1.0 })]    // zero-width last cell
        public void InvalidShares_FallBackToEqualCells(double[] shares)
        {
            IReadOnlyList<double> normalized = SheetSlotLayout.NormalizeCumulative(shares, 3);

            Assert.Equal(SheetSlotLayout.EqualCumulative(3), normalized);
        }

        [Fact]
        public void IsEqualCumulative_TreatsEmptyAndNearlyEqualAsEqual()
        {
            Assert.True(SheetSlotLayout.IsEqualCumulative(null, 3));
            Assert.True(SheetSlotLayout.IsEqualCumulative(new List<double>(), 3));
            Assert.True(SheetSlotLayout.IsEqualCumulative(new[] { 1.0 / 3 + 0.0001, 2.0 / 3 }, 3));
            Assert.False(SheetSlotLayout.IsEqualCumulative(new[] { 0.4, 0.7 }, 3));
        }

        [Fact]
        public void SeparatorPositions_OfEqualCells_SitAtTheMiddleOfEachGutter()
        {
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.ComputeGrid(W, H, 3, 1);
            IReadOnlyList<double> positions = SheetSlotLayout.SeparatorPositionsIn(W, 3, null);

            Assert.Equal(2, positions.Count);
            Assert.Equal((slots[0].RightIn + slots[1].LeftIn) / 2, positions[0], 9);
            Assert.Equal((slots[1].RightIn + slots[2].LeftIn) / 2, positions[1], 9);
        }

        [Fact]
        public void DraggedPosition_RoundTripsThroughCumulativeShares()
        {
            var shares = new[] { 0.3, 0.55 };
            IReadOnlyList<double> positions = SheetSlotLayout.SeparatorPositionsIn(W, 3, shares);

            Assert.Equal(0.3, SheetSlotLayout.CumulativeFromPositionIn(W, 3, 1, positions[0]), 9);
            Assert.Equal(0.55, SheetSlotLayout.CumulativeFromPositionIn(W, 3, 2, positions[1]), 9);
        }

        [Fact]
        public void SeparatorsBetweenUnequalCells_AlwaysLieInTheGutterBetweenTheirSlots()
        {
            var shares = new[] { 0.2, 0.9 };
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.ComputeGrid(W, H, 3, 1, shares, null);
            IReadOnlyList<double> positions = SheetSlotLayout.SeparatorPositionsIn(W, 3, shares);

            Assert.True(positions[0] > slots[0].RightIn && positions[0] < slots[1].LeftIn);
            Assert.True(positions[1] > slots[1].RightIn && positions[1] < slots[2].LeftIn);
        }

        [Fact]
        public void NewSheetPackage_DefaultsToEqualCellsAndNoSeparatorLines_AndCloneKeepsTheEdits()
        {
            var package = new SheetPackage();
            Assert.Empty(package.ColumnCumulative);
            Assert.Empty(package.RowCumulative);
            Assert.False(package.DrawSeparatorLines);

            package.ColumnCumulative = new List<double> { 0.4, 0.7 };
            package.RowCumulative = new List<double> { 0.6 };
            package.DrawSeparatorLines = true;
            package.SeparatorLineStyle = "Medium Lines";
            SheetPackage copy = package.CloneAsNew("copy");

            Assert.Equal(new[] { 0.4, 0.7 }, copy.ColumnCumulative);
            Assert.Equal(new[] { 0.6 }, copy.RowCumulative);
            Assert.True(copy.DrawSeparatorLines);
            Assert.Equal("Medium Lines", copy.SeparatorLineStyle);
            copy.ColumnCumulative.Add(0.9);
            Assert.Equal(2, package.ColumnCumulative.Count);
        }

        [Fact]
        public void ZeroWidthResult_IsStillRejectedAsTooSmall()
        {
            Assert.Throws<ArgumentException>(() => SheetSlotLayout.ComputeGrid(0.4, H, 2, 1, null, null));
        }
    }
}
