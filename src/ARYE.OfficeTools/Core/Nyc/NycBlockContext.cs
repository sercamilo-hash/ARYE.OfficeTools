using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>A subject lot plus every other lot MapPLUTO reports on the same block. No Revit types.</summary>
    public sealed class NycBlockContext
    {
        public string Borough { get; set; } = string.Empty;

        public string BlockNumber { get; set; } = string.Empty;

        public NycLotInfo SubjectLot { get; set; } = new NycLotInfo();

        public List<NycLotInfo> OtherLots { get; set; } = new List<NycLotInfo>();

        public List<NycBuildingFootprint> Buildings { get; set; } = new List<NycBuildingFootprint>();

        public List<NycLotInfo> AdjacentLots => OtherLots.Where(l => l.IsAdjacent).ToList();

        public List<NycLotInfo> RemainingBlockLots =>
            OtherLots.Where(l => !l.IsAdjacent && l.Bbl != SubjectLot?.Bbl).ToList();

        public List<NycLotInfo> AllLots
        {
            get
            {
                var list = new List<NycLotInfo>();
                if (SubjectLot != null) list.Add(SubjectLot);
                list.AddRange(OtherLots.Where(l => SubjectLot == null || l.Bbl != SubjectLot.Bbl));
                return list;
            }
        }

        public double MinX => AllLots.Count > 0 ? AllLots.Min(l => l.MinX) : 0.0;

        public double MaxX => AllLots.Count > 0 ? AllLots.Max(l => l.MaxX) : 0.0;

        public double MinY => AllLots.Count > 0 ? AllLots.Min(l => l.MinY) : 0.0;

        public double MaxY => AllLots.Count > 0 ? AllLots.Max(l => l.MaxY) : 0.0;

        /// <summary>
        /// Marks each OtherLots entry adjacent when it shares a vertex (within
        /// toleranceFt) with the subject lot's boundary.
        /// </summary>
        public void CalculateAdjacency(double toleranceFt = 1.0)
        {
            if (SubjectLot == null || SubjectLot.PolygonRings.Count == 0)
            {
                return;
            }
            List<LotPoint> subjectPoints = SubjectLot.PolygonRings.SelectMany(r => r).ToList();

            foreach (NycLotInfo other in OtherLots)
            {
                if (other.Bbl == SubjectLot.Bbl)
                {
                    other.IsAdjacent = false;
                    continue;
                }
                other.IsAdjacent = other.PolygonRings
                    .SelectMany(r => r)
                    .Any(pt => subjectPoints.Any(sub =>
                        Distance(pt, sub) <= toleranceFt));
            }
        }

        private static double Distance(LotPoint a, LotPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
