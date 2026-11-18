using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class SheetViewNamingTests
    {
        [Theory]
        [InlineData("01_1ST FL.", "1ST FL.")]
        [InlineData("24 24TH FL.", "24TH FL.")]
        [InlineData("00 CELLAR", "CELLAR")]
        [InlineData("1st Fl.", "1ST FL.")]
        [InlineData("ROOF", "ROOF")]
        [InlineData("  02 2ND FL.  ", "2ND FL.")]
        [InlineData("12", "12")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void StripLevelNumber_DropsOnlyTheSortPrefix(string level, string expected)
        {
            Assert.Equal(expected, SheetNaming.StripLevelNumber(level));
        }

        [Theory]
        [InlineData("03_3RD FL.", "07_7TH FL.", "3RD TO 7TH FL.")]
        [InlineData("08 8TH FL.", "15 15TH FL.", "8TH TO 15TH FL.")]
        [InlineData("02 2ND FL.", "02 2ND FL.", "2ND FL.")]
        [InlineData("00 CELLAR", "01 1ST FL.", "CELLAR TO 1ST FL.")]
        public void RangeLabel_JoinsFirstAndLast_SharingTheTrailingWord(string first, string last, string expected)
        {
            Assert.Equal(expected, SheetNaming.RangeLabel(first, last));
        }

        [Theory]
        [InlineData("1ST FL.", "", "1ST FL.")]
        [InlineData("2ND FL.", "Gross Area", "2ND FL. GROSS AREA")]
        [InlineData("3RD TO 7TH FL.", "  deduction plan ", "3RD TO 7TH FL. DEDUCTION PLAN")]
        [InlineData("1ST FL.", null, "1ST FL.")]
        public void ComposeViewName_AppendsTheSetText_OnlyWhenThereIsOne(string label, string suffix, string expected)
        {
            Assert.Equal(expected, SheetNaming.ComposeViewName(label, suffix));
        }
    }
}
