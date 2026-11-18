using System.Collections.Generic;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class SmartScaleAdvisorTests
    {
        public static IEnumerable<object[]> FootprintsBySlotMode()
        {
            var footprints = new[]
            {
                (Width: 600.0, Height: 40.0),   // very elongated
                (Width: 100.0, Height: 100.0),  // square
                (Width: 15.0, Height: 10.0),    // minuscule
                (Width: 250.0, Height: 180.0)   // large city block
            };
            foreach (int slotCount in SheetSlotLayout.SupportedSlotCounts)
            {
                foreach ((double width, double height) in footprints)
                {
                    yield return new object[] { slotCount, width, height };
                }
            }
        }

        [Theory]
        [MemberData(nameof(FootprintsBySlotMode))]
        public void RecommendedScale_AlwaysActuallyFits(int slotCount, double footprintW, double footprintH)
        {
            // Usable area of a 36x24 sheet.
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.Compute(32, 22, slotCount);
            SlotRect slot = slots[0];

            int scale = SmartScaleAdvisor.RecommendScale(footprintW, footprintH, slot.WidthIn, slot.HeightIn);

            if (scale == 0)
            {
                // Honest failure: not even 1/32" fits.
                Assert.False(SmartScaleAdvisor.Fits(footprintW, footprintH, slot.WidthIn, slot.HeightIn,
                    SmartScaleAdvisor.DefaultAnnotationMarginIn, 384));
                return;
            }

            // The fit property, asserted by recomputation:
            Assert.True(SmartScaleAdvisor.Fits(footprintW, footprintH, slot.WidthIn, slot.HeightIn,
                SmartScaleAdvisor.DefaultAnnotationMarginIn, scale));

            // And it is the LARGEST that fits: the next larger standard scale must not fit.
            int index = System.Array.IndexOf((int[])SmartScaleAdvisor.StandardScaleDenominators, scale);
            if (index > 0)
            {
                int nextLarger = SmartScaleAdvisor.StandardScaleDenominators[index - 1];
                Assert.False(SmartScaleAdvisor.Fits(footprintW, footprintH, slot.WidthIn, slot.HeightIn,
                    SmartScaleAdvisor.DefaultAnnotationMarginIn, nextLarger));
            }
        }

        [Fact]
        public void TooBigForAnything_ReturnsZero()
        {
            Assert.Equal(0, SmartScaleAdvisor.RecommendScale(5000, 4000, 10, 8));
        }

        [Fact]
        public void DegenerateFootprint_ReturnsZero()
        {
            Assert.Equal(0, SmartScaleAdvisor.RecommendScale(0, 100, 15, 10));
        }

        [Fact]
        public void TypicalNycFloorplate_On6Matrix_GetsUsableScale()
        {
            // ~200x100 ft floorplate into a 6-view matrix on a 36x24 sheet.
            IReadOnlyList<SlotRect> slots = SheetSlotLayout.Compute(32, 22, 6);
            int scale = SmartScaleAdvisor.RecommendScale(200, 100, slots[0].WidthIn, slots[0].HeightIn);
            Assert.True(scale >= 192 && scale <= 384,
                $"expected a small-but-standard scale, got 1/{scale}");
        }

        [Fact]
        public void EnergyCandidates_OfferThreeSixtyFourthsWhereTheDefaultListJumpsToThirtySeconds()
        {
            // 110 x 70 ft in an 8 x 6 in cell with the default 0.75 in margin: too big for 1/16", fits 3/64".
            int standard = SmartScaleAdvisor.RecommendScale(110, 70, 8, 6);
            int energy = SmartScaleAdvisor.RecommendScale(110, 70, 8, 6,
                SmartScaleAdvisor.DefaultAnnotationMarginIn, SmartScaleAdvisor.EnergyScaleDenominators);

            Assert.Equal(384, standard);
            Assert.Equal(256, energy);
        }

        [Fact]
        public void DefaultList_StillHasNoThreeSixtyFourths()
        {
            Assert.DoesNotContain(256, SmartScaleAdvisor.StandardScaleDenominators);
        }

        [Fact]
        public void EmptyOrNullCandidates_RecommendNothing()
        {
            Assert.Equal(0, SmartScaleAdvisor.RecommendScale(50, 50, 20, 20, 0.75, new int[0]));
            Assert.Equal(0, SmartScaleAdvisor.RecommendScale(50, 50, 20, 20, 0.75, null));
        }
    }
}
