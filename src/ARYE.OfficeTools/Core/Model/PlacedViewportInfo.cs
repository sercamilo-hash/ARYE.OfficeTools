namespace ARYE.OfficeTools.Core.Model
{
    /// <summary>One viewport actually placed by the sheet diagrammer.</summary>
    public sealed class PlacedViewportInfo
    {
        public string ViewName { get; set; }

        public string SheetNumber { get; set; }

        public int ScaleDenominator { get; set; }

        public override string ToString() =>
            ViewName + " → sheet " + SheetNumber + " @ 1/" + ScaleDenominator;
    }
}
