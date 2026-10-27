using System.Windows.Media;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// One row of the color-coded level range preview: which range (or
    /// nothing) a level belongs to, with the swatch color that matches its
    /// range's card, plus the SRC / pair chips. Rows are rebuilt wholesale on
    /// every change, so plain properties suffice.
    /// </summary>
    internal sealed class LevelRangeRowViewModel
    {
        public string LevelName { get; set; }

        public string ElevationLabel { get; set; }

        /// <summary>Range name, "<name> · unpaired", or "— unassigned".</summary>
        public string Label { get; set; }

        public Brush ColorBrush { get; set; }

        /// <summary>Tinted version of <see cref="ColorBrush"/> used as the row's
        /// full-width background, so the diagram reads as stacked bands of
        /// color instead of a flat list with a small swatch.</summary>
        public Brush RowBackground { get; set; }

        /// <summary>"SRC" chip: typical source level, or lower level of the duplex source pair.</summary>
        public bool ShowSourceChip { get; set; }

        /// <summary>"P1", "P2", … on duplex pair members.</summary>
        public string PairChipText { get; set; }

        public bool ShowPairChip { get; set; }

        /// <summary>Level not covered by any range — rendered with a gray dashed border.</summary>
        public bool IsUnassigned { get; set; }

        /// <summary>The odd leftover level of a duplex span — warning-styled.</summary>
        public bool IsWarning { get; set; }
    }
}
