using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class AreaTableNamingTests
    {
        [Theory]
        [InlineData("02 2ND FL.", "2ND FLOOR")]
        [InlineData("01_1ST FL.", "1ST FLOOR")]
        [InlineData("00 CELLAR", "CELLAR")]
        [InlineData("24 ROOF", "ROOF")]
        [InlineData("2ND FL", "2ND FLOOR")]
        [InlineData("1st fl.", "1ST FLOOR")]
        public void FloorLabel_StripsTheSortPrefix_AndSpellsOutFL(string level, string expected)
        {
            Assert.Equal(expected, SheetNaming.FloorLabel(level));
        }

        [Fact]
        public void FloorWord_SpellsOutFL_EvenBeforeADuplexSuffix()
        {
            Assert.Equal("8TH TO 9TH FLOOR LOWER", SheetNaming.FloorWord("8TH TO 9TH FL. LOWER"));
        }

        [Fact]
        public void FloorRangeLabel_JoinsTheEnds_AndSpellsOutFL()
        {
            Assert.Equal("3RD TO 7TH FLOOR", SheetNaming.FloorRangeLabel("03_3RD FL.", "07_7TH FL."));
        }

        [Fact]
        public void ComposeTableName_ReplacesEveryToken_AndUppercases()
        {
            string name = SheetNaming.ComposeTableName("AREA - {building value} - {level}", "Gross", "BLDG A", "BUILDING A", "2ND FLOOR");
            Assert.Equal("AREA - BUILDING A - 2ND FLOOR", name);
        }

        [Fact]
        public void ComposeTableName_BuildingToken_UsesTheZfaName()
        {
            string name = SheetNaming.ComposeTableName("DEDUCTIONS - {building} - {level}", "Deductions", "BLDG A", "BUILDING A", "1ST FLOOR");
            Assert.Equal("DEDUCTIONS - BLDG A - 1ST FLOOR", name);
        }

        [Fact]
        public void ComposeTableName_BlankPattern_FallsBackToTheDefault()
        {
            Assert.Equal("GROSS - BLDG A - CELLAR", SheetNaming.ComposeTableName("  ", "Gross", "BLDG A", "", "CELLAR"));
        }

        [Fact]
        public void ComposeTableName_BlankBuildingValue_UsesTheBuildingName()
        {
            Assert.Equal("AREA - BLDG B - ROOF", SheetNaming.ComposeTableName("AREA - {building value} - {level}", "Gross", "BLDG B", null, "ROOF"));
        }

        [Fact]
        public void ComposeTableName_RemovesCharactersRevitRefuses_AndCollapsesSpaces()
        {
            string name = SheetNaming.ComposeTableName("{set} [draft] | {level}", "Gross", "BLDG A", "", "2ND FLOOR");
            Assert.Equal("GROSS DRAFT 2ND FLOOR", name);
        }

        [Fact]
        public void ComposeTableName_ValueWithDollarSign_IsKeptLiteral()
        {
            Assert.Equal("A$1 - X", SheetNaming.ComposeTableName("{set} - {level}", "A$1", "B", "", "X"));
        }

        [Fact]
        public void NewPackage_HasNoAreaTable_AndCloneKeepsTheOptions()
        {
            var package = new SheetPackage();
            Assert.False(package.AddAreaTable);
            Assert.Equal(AreaTablePlacement.BesideViewLeft, package.AreaTablePlacement);
            Assert.Equal(0.25, package.AreaTableGapIn);

            package.AddAreaTable = true;
            package.AreaTableTemplateUniqueId = "tpl";
            package.AreaTableViewTemplateUniqueId = "vt";
            package.AreaTableNamePattern = "AREA - {level}";
            package.AreaTableBuildingValue = "BUILDING A";
            package.AreaTablePlacement = AreaTablePlacement.FixedEdge;
            package.AreaTableEdge = AreaTableEdge.Bottom;
            package.AreaTableGapIn = 0.5;
            SheetPackage copy = package.CloneAsNew("copy");

            Assert.True(copy.AddAreaTable);
            Assert.Equal("tpl", copy.AreaTableTemplateUniqueId);
            Assert.Equal("vt", copy.AreaTableViewTemplateUniqueId);
            Assert.Equal("AREA - {level}", copy.AreaTableNamePattern);
            Assert.Equal("BUILDING A", copy.AreaTableBuildingValue);
            Assert.Equal(AreaTablePlacement.FixedEdge, copy.AreaTablePlacement);
            Assert.Equal(AreaTableEdge.Bottom, copy.AreaTableEdge);
            Assert.Equal(0.5, copy.AreaTableGapIn);
        }
    }

    public class AreaTableLayoutTests
    {
        private static SlotRect Cell() => new SlotRect { CenterXIn = 5, CenterYIn = 4, WidthIn = 10, HeightIn = 8 };

        [Fact]
        public void SplitCell_Under_ViewKeepsTheTop_TableSitsAtTheBottom()
        {
            (SlotRect view, TableRect table) = AreaTableLayout.SplitCell(Cell(), AreaTablePlacement.UnderView, 6, 2, 0.5);

            Assert.Equal(5.5, view.HeightIn, 9);
            Assert.Equal(8, view.TopIn, 9);               // still touches the top of the cell
            Assert.Equal(10, view.WidthIn, 9);
            Assert.Equal(0, table.LeftIn, 9);
            Assert.Equal(2, table.TopIn, 9);              // table top = cell bottom + its height
            Assert.Equal(0, table.BottomIn, 9);
            Assert.True(view.BottomIn >= table.TopIn);    // the two never overlap
        }

        [Fact]
        public void SplitCell_Beside_ViewKeepsTheLeft_TableSitsAtTheRight()
        {
            (SlotRect view, TableRect table) = AreaTableLayout.SplitCell(Cell(), AreaTablePlacement.BesideView, 3, 5, 0.5);

            Assert.Equal(6.5, view.WidthIn, 9);
            Assert.Equal(0, view.LeftIn, 9);
            Assert.Equal(8, view.HeightIn, 9);
            Assert.Equal(7, table.LeftIn, 9);             // right edge of the cell minus the table width
            Assert.Equal(8, table.TopIn, 9);
            Assert.True(view.RightIn <= table.LeftIn);
        }

        [Fact]
        public void SplitCell_BesideLeft_TableHugsTheTopLeftCorner_ViewFillsTheRest()
        {
            (SlotRect view, TableRect table) = AreaTableLayout.SplitCell(Cell(), AreaTablePlacement.BesideViewLeft, 3, 5, 0.5);

            Assert.Equal(0, table.LeftIn, 9);              // against the left edge of the cell
            Assert.Equal(8, table.TopIn, 9);               // from the top corner down
            Assert.Equal(6.5, view.WidthIn, 9);            // 10 - 3 - 0.5
            Assert.Equal(10, view.RightIn, 9);             // the view runs to the right edge
            Assert.Equal(3.5, view.LeftIn, 9);             // and starts after the strip and the gap
            Assert.Equal(8, view.HeightIn, 9);             // full cell height, centred as before
            Assert.True(table.RightIn <= view.LeftIn);
        }

        [Fact]
        public void NewPackage_DefaultsToTheSmallLeftStrip()
        {
            Assert.Equal(AreaTablePlacement.BesideViewLeft, new SheetPackage().AreaTablePlacement);
        }

        [Fact]
        public void SplitCell_TableLargerThanTheCell_LeavesNoViewRoom_NotANegativeOne()
        {
            (SlotRect view, _) = AreaTableLayout.SplitCell(Cell(), AreaTablePlacement.UnderView, 4, 20, 0.25);
            Assert.Equal(0, view.HeightIn);
        }

        [Fact]
        public void SplitCell_OtherPlacements_Throw()
        {
            Assert.Throws<System.ArgumentException>(() => AreaTableLayout.SplitCell(Cell(), AreaTablePlacement.OwnCell, 1, 1, 0));
        }

        [Theory]
        [InlineData(AreaTableEdge.Right, 28, 22, 0, 0)]
        [InlineData(AreaTableEdge.Left, 28, 22, 4, 0)]
        [InlineData(AreaTableEdge.Bottom, 32, 17, 0, 5)]
        [InlineData(AreaTableEdge.Top, 32, 17, 0, 0)]
        public void ReduceArea_TakesTheStripFromTheRightSide(AreaTableEdge edge, double viewW, double viewH, double offX, double offY)
        {
            AreaReduction r = AreaTableLayout.ReduceArea(32, 22, edge, 3.5, 4.5, 0.5);

            Assert.Equal(viewW, r.ViewWidthIn, 9);
            Assert.Equal(viewH, r.ViewHeightIn, 9);
            Assert.Equal(offX, r.OffsetXIn, 9);
            Assert.Equal(offY, r.OffsetYIn, 9);
        }

        [Fact]
        public void Stack_Right_RightAlignsAndStacksDownward_WithTheGap()
        {
            IReadOnlyList<TableRect> rects = AreaTableLayout.Stack(AreaTableEdge.Right, 32, 22,
                new[] { (3.0, 6.0), (4.0, 5.0) }, 0.5, out bool overflows);

            Assert.False(overflows);
            Assert.Equal(29, rects[0].LeftIn, 9);
            Assert.Equal(22, rects[0].TopIn, 9);
            Assert.Equal(28, rects[1].LeftIn, 9);
            Assert.Equal(22 - 6 - 0.5, rects[1].TopIn, 9);
        }

        [Fact]
        public void Stack_Right_ReportsOverflow_WhenTheLastTableLeavesTheArea()
        {
            AreaTableLayout.Stack(AreaTableEdge.Right, 32, 10, new[] { (3.0, 6.0), (3.0, 6.0) }, 0.5, out bool overflows);
            Assert.True(overflows);
        }

        [Fact]
        public void Stack_Bottom_LinesUpFromTheLeft_TopsAlignedToTheTallest()
        {
            IReadOnlyList<TableRect> rects = AreaTableLayout.Stack(AreaTableEdge.Bottom, 32, 22,
                new[] { (5.0, 3.0), (6.0, 4.0) }, 0.5, out bool overflows);

            Assert.False(overflows);
            Assert.Equal(0, rects[0].LeftIn, 9);
            Assert.Equal(5.5, rects[1].LeftIn, 9);
            Assert.Equal(4, rects[0].TopIn, 9);
            Assert.Equal(4, rects[1].TopIn, 9);
        }

        [Fact]
        public void Stack_Top_StartsAtTheTopEdge()
        {
            IReadOnlyList<TableRect> rects = AreaTableLayout.Stack(AreaTableEdge.Top, 32, 22,
                new[] { (5.0, 3.0) }, 0.5, out _);
            Assert.Equal(22, rects[0].TopIn, 9);
        }

        [Fact]
        public void Stack_Bottom_ReportsOverflow_WhenTablesRunPastTheRightEdge()
        {
            AreaTableLayout.Stack(AreaTableEdge.Bottom, 10, 22, new[] { (6.0, 3.0), (6.0, 3.0) }, 0.5, out bool overflows);
            Assert.True(overflows);
        }

        [Fact]
        public void InCell_AnchorsTheTableAtTheCellsTopLeft()
        {
            TableRect t = AreaTableLayout.InCell(Cell(), 4, 3);
            Assert.Equal(0, t.LeftIn, 9);
            Assert.Equal(8, t.TopIn, 9);
        }

        [Fact]
        public void ExceedsRoom_FlagsATableBiggerThanItsCell()
        {
            Assert.True(AreaTableLayout.ExceedsRoom(new TableRect { WidthIn = 11, HeightIn = 2 }, 10, 8));
            Assert.False(AreaTableLayout.ExceedsRoom(new TableRect { WidthIn = 9, HeightIn = 2 }, 10, 8));
        }
    }
}
