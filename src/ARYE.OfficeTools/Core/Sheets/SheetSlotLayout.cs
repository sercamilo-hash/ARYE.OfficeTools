using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>
    /// Splits a sheet's usable area into a Columns × Rows matrix of equal view
    /// slots (each side 1–4), in reading order: top-left → right → next row
    /// down. The legacy slot-count API (1, 2, 3, 4, 6, 8) maps onto the same
    /// grids it always produced.
    /// </summary>
    public static class SheetSlotLayout
    {
        public const double GutterIn = 0.5;

        public const int MinColumns = 1;
        public const int MaxColumns = 4;
        public const int MinRows = 1;
        public const int MaxRows = 4;

        private static readonly Dictionary<int, (int Columns, int Rows)> Grids =
            new Dictionary<int, (int, int)>
            {
                { 1, (1, 1) },
                { 2, (2, 1) },
                { 3, (3, 1) },
                { 4, (2, 2) },
                { 6, (3, 2) },
                { 8, (4, 2) }
            };

        public static IReadOnlyList<int> SupportedSlotCounts => new[] { 1, 2, 3, 4, 6, 8 };

        /// <summary>Legacy entry point: a slot count that maps to a fixed landscape grid.</summary>
        public static IReadOnlyList<SlotRect> Compute(double usableWidthIn, double usableHeightIn, int slotCount)
        {
            if (!Grids.TryGetValue(slotCount, out (int Columns, int Rows) grid))
            {
                throw new ArgumentException(
                    $"Unsupported slot count {slotCount}. Supported: 1, 2, 3, 4, 6, 8.", nameof(slotCount));
            }
            return ComputeGrid(usableWidthIn, usableHeightIn, grid.Columns, grid.Rows);
        }

        public static IReadOnlyList<SlotRect> ComputeGrid(
            double usableWidthIn, double usableHeightIn, int columns, int rows)
            => ComputeGrid(usableWidthIn, usableHeightIn, columns, rows, null, null);

        /// <summary>
        /// Same matrix with the cell sizes the user dragged. Each list holds the
        /// CUMULATIVE share (0–1, strictly increasing, one entry per separator)
        /// of the net space left after the gutters — columns measured from the
        /// left, rows from the top. A null/empty/invalid list means equal cells,
        /// which reproduces the original equal grid exactly.
        /// </summary>
        public static IReadOnlyList<SlotRect> ComputeGrid(
            double usableWidthIn, double usableHeightIn, int columns, int rows,
            IReadOnlyList<double> columnCumulative, IReadOnlyList<double> rowCumulative)
        {
            if (columns < MinColumns || columns > MaxColumns)
            {
                throw new ArgumentException(
                    $"Columns must be {MinColumns}–{MaxColumns}, got {columns}.", nameof(columns));
            }
            if (rows < MinRows || rows > MaxRows)
            {
                throw new ArgumentException(
                    $"Rows must be {MinRows}–{MaxRows}, got {rows}.", nameof(rows));
            }
            if (usableWidthIn <= 0 || usableHeightIn <= 0)
            {
                throw new ArgumentException("Usable area must be positive.");
            }

            double netWidth = usableWidthIn - GutterIn * (columns - 1);
            double netHeight = usableHeightIn - GutterIn * (rows - 1);
            if (netWidth <= 0 || netHeight <= 0)
            {
                throw new ArgumentException(
                    $"Usable area {usableWidthIn}x{usableHeightIn} in is too small for a {columns}x{rows} grid.");
            }

            IReadOnlyList<double> cx = NormalizeCumulative(columnCumulative, columns);
            IReadOnlyList<double> cy = NormalizeCumulative(rowCumulative, rows);

            var slots = new List<SlotRect>();
            for (int row = 0; row < rows; row++)
            {
                double top = Share(cy, row) * netHeight + row * GutterIn;
                double slotHeight = (Share(cy, row + 1) - Share(cy, row)) * netHeight;
                // Reading order: top row first; Y is measured up from the area's lower-left.
                double centerY = usableHeightIn - (top + slotHeight / 2);
                for (int column = 0; column < columns; column++)
                {
                    double left = Share(cx, column) * netWidth + column * GutterIn;
                    double slotWidth = (Share(cx, column + 1) - Share(cx, column)) * netWidth;
                    slots.Add(new SlotRect
                    {
                        CenterXIn = left + slotWidth / 2,
                        CenterYIn = centerY,
                        WidthIn = slotWidth,
                        HeightIn = slotHeight
                    });
                }
            }
            return slots;
        }

        /// <summary>The cumulative shares of equal cells: 1/n, 2/n … (n-1)/n.</summary>
        public static IReadOnlyList<double> EqualCumulative(int count)
        {
            var list = new List<double>();
            for (int i = 1; i < count; i++)
            {
                list.Add((double)i / count);
            }
            return list;
        }

        /// <summary>The stored shares when they are usable for this many cells, otherwise equal cells.</summary>
        public static IReadOnlyList<double> NormalizeCumulative(IReadOnlyList<double> stored, int count)
        {
            if (stored == null || stored.Count != count - 1 || count < 2)
            {
                return EqualCumulative(count);
            }
            double previous = 0;
            foreach (double value in stored)
            {
                if (double.IsNaN(value) || value <= previous || value >= 1)
                {
                    return EqualCumulative(count);
                }
                previous = value;
            }
            return stored;
        }

        /// <summary>True when the shares describe equal cells (or are empty).</summary>
        public static bool IsEqualCumulative(IReadOnlyList<double> cumulative, int count)
        {
            if (cumulative == null || cumulative.Count == 0)
            {
                return true;
            }
            IReadOnlyList<double> equal = EqualCumulative(count);
            if (cumulative.Count != equal.Count)
            {
                return false;
            }
            for (int i = 0; i < equal.Count; i++)
            {
                if (Math.Abs(cumulative[i] - equal[i]) > 0.0005)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Where each separator sits (the middle of the gutter), in inches from
        /// the start of an axis of the given length. One entry per separator.
        /// </summary>
        public static IReadOnlyList<double> SeparatorPositionsIn(double lengthIn, int count, IReadOnlyList<double> cumulative)
        {
            IReadOnlyList<double> shares = NormalizeCumulative(cumulative, count);
            double net = lengthIn - GutterIn * (count - 1);
            var positions = new List<double>();
            for (int k = 1; k < count; k++)
            {
                positions.Add(shares[k - 1] * net + (k - 1) * GutterIn + GutterIn / 2);
            }
            return positions;
        }

        /// <summary>Inverse of <see cref="SeparatorPositionsIn"/> for separator k (1-based): the cumulative share for a dragged position.</summary>
        public static double CumulativeFromPositionIn(double lengthIn, int count, int separatorNumber, double positionIn)
        {
            double net = lengthIn - GutterIn * (count - 1);
            return (positionIn - (separatorNumber - 1) * GutterIn - GutterIn / 2) / net;
        }

        private static double Share(IReadOnlyList<double> cumulative, int boundary) =>
            boundary <= 0 ? 0 : boundary > cumulative.Count ? 1 : cumulative[boundary - 1];
    }
}
