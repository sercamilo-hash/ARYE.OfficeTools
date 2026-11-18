using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>
    /// Recommends the largest standard imperial architectural scale at which a
    /// footprint fits a slot with room for annotation. Returns the scale
    /// denominator (e.g. 96 for 1/8" = 1'-0"), or 0 when nothing fits — the
    /// caller records a Failure, never a silent clip.
    /// </summary>
    public static class SmartScaleAdvisor
    {
        /// <summary>Standard scales, largest drawing first: 1"=1' … 1/32"=1'.</summary>
        public static readonly IReadOnlyList<int> StandardScaleDenominators =
            new[] { 12, 16, 24, 32, 48, 64, 96, 128, 192, 384 };

        /// <summary>
        /// Candidates for tools that also draw at 3/64" = 1'-0" (1:256), the usual scale of energy-code plans.
        /// The default list does not include it, so the scale of sets already generated never changes.
        /// </summary>
        public static readonly IReadOnlyList<int> EnergyScaleDenominators =
            new[] { 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384 };

        public const double DefaultAnnotationMarginIn = 0.75;

        public static int RecommendScale(
            double footprintWidthFt, double footprintHeightFt,
            double slotWidthIn, double slotHeightIn,
            double annotationMarginIn = DefaultAnnotationMarginIn)
            => RecommendScale(footprintWidthFt, footprintHeightFt, slotWidthIn, slotHeightIn,
                annotationMarginIn, StandardScaleDenominators);

        /// <summary>Same recommendation over a caller-chosen list of scale denominators, largest drawing first.</summary>
        public static int RecommendScale(
            double footprintWidthFt, double footprintHeightFt,
            double slotWidthIn, double slotHeightIn,
            double annotationMarginIn, IReadOnlyList<int> candidates)
        {
            if (footprintWidthFt <= 0 || footprintHeightFt <= 0 || candidates == null)
            {
                return 0;
            }

            foreach (int denominator in candidates)
            {
                if (Fits(footprintWidthFt, footprintHeightFt, slotWidthIn, slotHeightIn,
                        annotationMarginIn, denominator))
                {
                    return denominator;
                }
            }
            return 0;
        }

        public static bool Fits(
            double footprintWidthFt, double footprintHeightFt,
            double slotWidthIn, double slotHeightIn,
            double annotationMarginIn, int scaleDenominator)
        {
            double drawingWidthIn = footprintWidthFt * 12.0 / scaleDenominator;
            double drawingHeightIn = footprintHeightFt * 12.0 / scaleDenominator;
            return drawingWidthIn + 2 * annotationMarginIn <= slotWidthIn
                && drawingHeightIn + 2 * annotationMarginIn <= slotHeightIn;
        }
    }
}
