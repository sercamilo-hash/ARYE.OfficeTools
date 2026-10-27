using System;

namespace ARYE.OfficeTools.Core.Nyc
{
    /// <summary>Plain 2D point in feet, local project coordinates. No Revit XYZ in Core.</summary>
    public struct LotPoint
    {
        public LotPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public double DistanceTo(LotPoint other)
        {
            double dx = other.X - X;
            double dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>True when both coordinates agree within the tolerance (feet).</summary>
        public bool IsSame(LotPoint other, double toleranceFt) =>
            Math.Abs(other.X - X) <= toleranceFt && Math.Abs(other.Y - Y) <= toleranceFt;

        public override string ToString() => $"({X:0.####}, {Y:0.####})";
    }
}
