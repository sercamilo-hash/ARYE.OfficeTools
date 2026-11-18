using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>Where a set's area table goes relative to the view it belongs to.</summary>
    public enum AreaTablePlacement
    {
        /// <summary>The cell is split: the view keeps the top, its table sits at the bottom of the same cell.</summary>
        UnderView = 0,

        /// <summary>The cell is split: the view keeps the left, its table sits at the right of the same cell.</summary>
        BesideView = 1,

        /// <summary>Each table takes a matrix cell of its own, after the views.</summary>
        OwnCell = 2,

        /// <summary>All of a sheet's tables stack along one edge of the usable area.</summary>
        FixedEdge = 3,

        /// <summary>The cell is split: a narrow strip at the left holds the table from the top corner down, the view fills the rest.</summary>
        BesideViewLeft = 4
    }

    /// <summary>The edge of the usable area a fixed-place table stack sits on.</summary>
    public enum AreaTableEdge
    {
        Right = 0,
        Left = 1,
        Bottom = 2,
        Top = 3
    }

    /// <summary>
    /// A table's footprint on a sheet. The anchor is the top-left corner, because that is the point
    /// a schedule is placed by. Inches from the usable area's lower-left corner, Y up.
    /// </summary>
    public sealed class TableRect
    {
        public double LeftIn { get; set; }

        public double TopIn { get; set; }

        public double WidthIn { get; set; }

        public double HeightIn { get; set; }

        public double RightIn => LeftIn + WidthIn;

        public double BottomIn => TopIn - HeightIn;
    }

    /// <summary>The view area that is left once a fixed table edge has taken its share.</summary>
    public sealed class AreaReduction
    {
        public double ViewWidthIn { get; set; }

        public double ViewHeightIn { get; set; }

        /// <summary>How far the view area's lower-left corner moves right of the usable area's.</summary>
        public double OffsetXIn { get; set; }

        /// <summary>How far the view area's lower-left corner moves up from the usable area's.</summary>
        public double OffsetYIn { get; set; }
    }

    /// <summary>
    /// The geometry of the four table positions, as pure functions: how much of a cell or of the usable
    /// area a table takes from its view, and where the table's top-left corner lands. Nothing here reads Revit.
    /// </summary>
    public static class AreaTableLayout
    {
        /// <summary>
        /// Splits one cell between its view and its table for <see cref="AreaTablePlacement.UnderView"/>,
        /// <see cref="AreaTablePlacement.BesideView"/> or <see cref="AreaTablePlacement.BesideViewLeft"/>. The view area never goes below zero; a table larger than
        /// the cell simply leaves the view no room, which the caller reports.
        /// </summary>
        public static (SlotRect View, TableRect Table) SplitCell(
            SlotRect cell, AreaTablePlacement placement, double tableWidthIn, double tableHeightIn, double gapIn)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            double gap = Math.Max(0, gapIn);
            switch (placement)
            {
                case AreaTablePlacement.UnderView:
                {
                    double viewHeight = Math.Max(0, cell.HeightIn - tableHeightIn - gap);
                    var view = new SlotRect
                    {
                        CenterXIn = cell.CenterXIn, WidthIn = cell.WidthIn,
                        HeightIn = viewHeight, CenterYIn = cell.TopIn - viewHeight / 2
                    };
                    var table = new TableRect
                    {
                        LeftIn = cell.LeftIn, TopIn = cell.BottomIn + tableHeightIn,
                        WidthIn = tableWidthIn, HeightIn = tableHeightIn
                    };
                    return (view, table);
                }
                case AreaTablePlacement.BesideViewLeft:
                {
                    double viewWidth = Math.Max(0, cell.WidthIn - tableWidthIn - gap);
                    var view = new SlotRect
                    {
                        CenterXIn = cell.RightIn - viewWidth / 2, WidthIn = viewWidth,
                        HeightIn = cell.HeightIn, CenterYIn = cell.CenterYIn
                    };
                    var table = new TableRect
                    {
                        LeftIn = cell.LeftIn, TopIn = cell.TopIn,
                        WidthIn = tableWidthIn, HeightIn = tableHeightIn
                    };
                    return (view, table);
                }
                case AreaTablePlacement.BesideView:
                {
                    double viewWidth = Math.Max(0, cell.WidthIn - tableWidthIn - gap);
                    var view = new SlotRect
                    {
                        CenterXIn = cell.LeftIn + viewWidth / 2, WidthIn = viewWidth,
                        HeightIn = cell.HeightIn, CenterYIn = cell.CenterYIn
                    };
                    var table = new TableRect
                    {
                        LeftIn = cell.RightIn - tableWidthIn, TopIn = cell.TopIn,
                        WidthIn = tableWidthIn, HeightIn = tableHeightIn
                    };
                    return (view, table);
                }
                default:
                    throw new ArgumentException("Only UnderView, BesideView and BesideViewLeft split a cell.", nameof(placement));
            }
        }

        /// <summary>The view area left for the views when the tables stack along <paramref name="edge"/>.</summary>
        public static AreaReduction ReduceArea(
            double usableWidthIn, double usableHeightIn, AreaTableEdge edge, double stripWidthIn, double stripHeightIn, double gapIn)
        {
            double gap = Math.Max(0, gapIn);
            switch (edge)
            {
                case AreaTableEdge.Right:
                    return new AreaReduction { ViewWidthIn = usableWidthIn - stripWidthIn - gap, ViewHeightIn = usableHeightIn };
                case AreaTableEdge.Left:
                    return new AreaReduction
                    {
                        ViewWidthIn = usableWidthIn - stripWidthIn - gap, ViewHeightIn = usableHeightIn, OffsetXIn = stripWidthIn + gap
                    };
                case AreaTableEdge.Bottom:
                    return new AreaReduction
                    {
                        ViewWidthIn = usableWidthIn, ViewHeightIn = usableHeightIn - stripHeightIn - gap, OffsetYIn = stripHeightIn + gap
                    };
                default:
                    return new AreaReduction { ViewWidthIn = usableWidthIn, ViewHeightIn = usableHeightIn - stripHeightIn - gap };
            }
        }

        /// <summary>
        /// Stacks a sheet's tables along an edge, in the order given: Right and Left stack downward from the top,
        /// Bottom and Top line up from the left. <paramref name="overflows"/> is true when the last table does not
        /// fit inside the usable area, so the caller can say so instead of placing it silently off the sheet.
        /// </summary>
        public static IReadOnlyList<TableRect> Stack(
            AreaTableEdge edge, double usableWidthIn, double usableHeightIn,
            IReadOnlyList<(double WidthIn, double HeightIn)> sizes, double gapIn, out bool overflows)
        {
            double gap = Math.Max(0, gapIn);
            var rects = new List<TableRect>();
            overflows = false;
            if (edge == AreaTableEdge.Right || edge == AreaTableEdge.Left)
            {
                double top = usableHeightIn;
                foreach ((double width, double height) in sizes)
                {
                    rects.Add(new TableRect
                    {
                        LeftIn = edge == AreaTableEdge.Right ? usableWidthIn - width : 0,
                        TopIn = top, WidthIn = width, HeightIn = height
                    });
                    top -= height + gap;
                    if (rects.Last().BottomIn < -1e-9) overflows = true;
                }
            }
            else
            {
                double stripHeight = sizes.Count == 0 ? 0 : sizes.Max(s => s.HeightIn);
                double top = edge == AreaTableEdge.Bottom ? stripHeight : usableHeightIn;
                double left = 0;
                foreach ((double width, double height) in sizes)
                {
                    rects.Add(new TableRect { LeftIn = left, TopIn = top, WidthIn = width, HeightIn = height });
                    left += width + gap;
                    if (rects.Last().RightIn > usableWidthIn + 1e-9) overflows = true;
                }
            }
            return rects;
        }

        /// <summary>The rectangle of cell <paramref name="slot"/> as a table anchored at its top-left corner (OwnCell).</summary>
        public static TableRect InCell(SlotRect slot, double tableWidthIn, double tableHeightIn) =>
            new TableRect { LeftIn = slot.LeftIn, TopIn = slot.TopIn, WidthIn = tableWidthIn, HeightIn = tableHeightIn };

        /// <summary>True when the table is wider or taller than the room it was given.</summary>
        public static bool ExceedsRoom(TableRect table, double roomWidthIn, double roomHeightIn) =>
            table.WidthIn > roomWidthIn + 1e-6 || table.HeightIn > roomHeightIn + 1e-6;
    }
}
