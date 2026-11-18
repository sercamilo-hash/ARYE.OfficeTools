namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>One viewport slot on a sheet, in inches from the usable area's lower-left corner.</summary>
    public sealed class SlotRect
    {
        public double CenterXIn { get; set; }

        public double CenterYIn { get; set; }

        public double WidthIn { get; set; }

        public double HeightIn { get; set; }

        public double LeftIn => CenterXIn - WidthIn / 2;

        public double RightIn => CenterXIn + WidthIn / 2;

        public double BottomIn => CenterYIn - HeightIn / 2;

        public double TopIn => CenterYIn + HeightIn / 2;
    }
}
