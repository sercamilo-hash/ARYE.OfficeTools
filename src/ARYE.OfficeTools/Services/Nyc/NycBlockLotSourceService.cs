using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARYE.OfficeTools.Core.Nyc;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>
    /// Gives every other lot on the block the same DTM/Legal choice the
    /// subject gets: fetches every block lot's own tax-map faces in one
    /// request, then resolves each lot independently with
    /// <see cref="BlockLotSourceResolver"/>. A lot whose faces cannot be
    /// fetched at all still resolves — it falls back to DTM with a reason,
    /// never drops out of the list.
    /// </summary>
    public sealed class NycBlockLotSourceService
    {
        private readonly NycTaxLotFaceService _faceService;

        public NycBlockLotSourceService(NycTaxLotFaceService faceService = null)
        {
            _faceService = faceService ?? new NycTaxLotFaceService();
        }

        /// <summary>
        /// Resolves every lot in <see cref="NycBlockContext.OtherLots"/>.
        /// <paramref name="subjectLegalRing"/> is the subject's own Legal
        /// rebuild (when available) — used only to measure the seam for
        /// adjacent lots; pass null when the subject has no Legal ring yet.
        /// </summary>
        public async Task<List<BlockLotSourceResolution>> ResolveAsync(NycBlockContext context, List<LotPoint> subjectLegalRing)
        {
            var results = new List<BlockLotSourceResolution>();
            if (context == null || context.OtherLots.Count == 0)
            {
                return results;
            }

            Dictionary<string, List<NycLotFace>> byBbl;
            try
            {
                byBbl = await _faceService.GetFacesForBblsAsync(context.OtherLots.Select(l => l.Bbl));
            }
            catch (NycApiException)
            {
                byBbl = new Dictionary<string, List<NycLotFace>>(); // every lot falls back to DTM below, with its own reason
            }

            foreach (NycLotInfo lot in context.OtherLots)
            {
                List<NycLotFace> raw = byBbl.TryGetValue(lot.Bbl, out List<NycLotFace> faces) ? faces : new List<NycLotFace>();
                results.Add(BlockLotSourceResolver.Resolve(lot, raw, subjectLegalRing));
            }
            return results;
        }
    }
}
