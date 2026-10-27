using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>Which geometry a block lot (not the subject) is drawn from. Never defaulted by the resolver.</summary>
    public enum LotSourceChoice
    {
        /// <summary>Not chosen yet — the lot is not drawn.</summary>
        None = 0,

        Dtm = 1,

        Legal = 2
    }

    /// <summary>
    /// Everything the viewer needs to offer one block lot the same DTM/Legal
    /// choice the subject gets: both rings, why Legal is or isn't available,
    /// how far its Legal rebuild sits from the subject where they meet, and a
    /// suggestion the user can accept or override.
    /// </summary>
    public sealed class BlockLotSourceResolution
    {
        public string Bbl { get; set; } = string.Empty;

        public bool IsAdjacent { get; set; }

        /// <summary>DTM polygon ring, empty when MapPLUTO has none.</summary>
        public List<LotPoint> DtmRing { get; set; } = new List<LotPoint>();

        /// <summary>Legal rebuild, empty when unavailable.</summary>
        public List<LotPoint> LegalRing { get; set; } = new List<LotPoint>();

        /// <summary>Full detail behind the Legal ring (closure, warnings). Null when reconstruction was never attempted.</summary>
        public LotReconstructionResult Reconstruction { get; set; }

        /// <summary>Ordered faces as chained from DOF, before reconstruction. Empty when chaining failed.</summary>
        public List<NycLotFace> ChainedFaces { get; set; } = new List<NycLotFace>();

        /// <summary>Of the chained faces, how many carry no LOT_FACE_LENGTH.</summary>
        public int MissingLegalFaceCount { get; set; }

        public bool LegalAvailable => LegalRing.Count >= 3;

        /// <summary>Why Legal is unavailable. Empty when it IS available.</summary>
        public string LegalUnavailableReason { get; set; } = string.Empty;

        /// <summary>
        /// Farthest the Legal ring drifts from the subject's own Legal ring where
        /// the two meet. Only set for adjacent lots with an available Legal ring
        /// and a subject reference ring to compare against.
        /// </summary>
        public double? SeamToSubjectFt { get; set; }

        /// <summary>Area difference between DTM and Legal, as a fraction of the DTM area. 0 when either ring is unavailable.</summary>
        public double DifferencePct { get; set; }

        /// <summary>What BauTools would pick if asked. Never applied on its own — see <see cref="BlockLotSourceResolver.SuggestAll"/>.</summary>
        public LotSourceChoice Suggested { get; set; } = LotSourceChoice.None;

        /// <summary>What the user picked. None = not drawn.</summary>
        public LotSourceChoice Chosen { get; set; } = LotSourceChoice.None;

        public List<LotPoint> RingFor(LotSourceChoice choice) => choice switch
        {
            LotSourceChoice.Legal => LegalRing,
            LotSourceChoice.Dtm => DtmRing,
            _ => new List<LotPoint>()
        };
    }

    /// <summary>
    /// Gives every lot on the block the same DTM-vs-Legal choice the subject
    /// gets, one lot at a time. Each lot is rebuilt independently from its own
    /// tax-map faces — Legal neighbors are NOT joined into one network
    /// adjustment, so two Legal neighbors can gap or overlap by inches where
    /// they meet. That gap is measured (<see cref="LotSeamMeasurement"/>), not
    /// hidden.
    /// </summary>
    public static class BlockLotSourceResolver
    {
        /// <summary>
        /// A lot with this fraction (or more) of its chained faces missing a
        /// legal length is not offered Legal — too much of it would be a
        /// digitized guess wearing a legal number's certainty.
        /// </summary>
        public const double MaxMissingLegalFaceRatio = 0.5;

        /// <summary>
        /// Resolves one block lot. <paramref name="rawFaces"/> is that lot's own
        /// TAX_LOT_FACE rows (unordered, as DOF serves them — may be empty).
        /// <paramref name="subjectLegalReference"/> is the subject's own Legal
        /// ring, used only to measure the seam; pass null/empty to skip that.
        /// </summary>
        public static BlockLotSourceResolution Resolve(
            NycLotInfo lot,
            IReadOnlyList<NycLotFace> rawFaces,
            IReadOnlyList<LotPoint> subjectLegalReference)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            var res = new BlockLotSourceResolution
            {
                Bbl = lot.Bbl,
                IsAdjacent = lot.IsAdjacent,
                DtmRing = lot.PolygonRings.Count > 0 ? lot.PolygonRings[0].ToList() : new List<LotPoint>()
            };

            if (rawFaces == null || rawFaces.Count == 0)
            {
                res.LegalUnavailableReason = "no tax-map faces published for this lot";
            }
            else
            {
                LotFaceChainResult chain = LotFaceChain.Order(rawFaces);
                if (!chain.Succeeded)
                {
                    res.LegalUnavailableReason = chain.FailureReason;
                }
                else
                {
                    res.ChainedFaces = chain.Faces;
                    res.MissingLegalFaceCount = chain.Faces.Count(f => f.LegalLengthFt <= 0);
                    double missingRatio = (double)res.MissingLegalFaceCount / chain.Faces.Count;
                    if (missingRatio >= MaxMissingLegalFaceRatio)
                    {
                        res.LegalUnavailableReason =
                            $"legal length missing on {res.MissingLegalFaceCount} of {chain.Faces.Count} sides";
                    }
                    else
                    {
                        LotReconstructionResult recon = LotReconstruction.FromLegalFaces(chain.Faces);
                        res.Reconstruction = recon;
                        if (!recon.Succeeded)
                        {
                            res.LegalUnavailableReason = recon.FailureReason.Length > 0
                                ? recon.FailureReason
                                : "legal faces did not reconstruct into a closed lot";
                        }
                        else
                        {
                            res.LegalRing = recon.Ring;
                            if (subjectLegalReference != null && subjectLegalReference.Count >= 3)
                            {
                                res.SeamToSubjectFt = LotSeamMeasurement.MaxGapToReference(recon.Ring, subjectLegalReference);
                            }
                        }
                    }
                }
            }

            if (res.LegalAvailable && res.DtmRing.Count >= 3)
            {
                double dtmArea = Math.Abs(LotGeometry.SignedArea(res.DtmRing));
                double legalArea = Math.Abs(LotGeometry.SignedArea(res.LegalRing));
                res.DifferencePct = dtmArea > 0 ? Math.Abs(legalArea - dtmArea) / dtmArea : 0;
            }

            res.Suggested = res.LegalAvailable
                ? LotSourceChoice.Legal
                : (res.DtmRing.Count >= 3 ? LotSourceChoice.Dtm : LotSourceChoice.None);

            return res;
        }

        /// <summary>Sets <see cref="BlockLotSourceResolution.Chosen"/> to the suggestion for every lot that has none chosen yet. Never overrides a user pick.</summary>
        public static void SuggestAll(IEnumerable<BlockLotSourceResolution> resolutions)
        {
            foreach (BlockLotSourceResolution r in resolutions)
            {
                if (r.Chosen == LotSourceChoice.None)
                {
                    r.Chosen = r.Suggested;
                }
            }
        }
    }
}
