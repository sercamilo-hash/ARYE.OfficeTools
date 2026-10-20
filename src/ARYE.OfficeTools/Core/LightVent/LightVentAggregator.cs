using System;
using System.Collections.Generic;
using System.Linq;

namespace ARYE.OfficeTools.Core.LightVent
{
    /// <summary>How a counted window contributed, given which of its two values were empty.</summary>
    public enum WindowValueState
    {
        /// <summary>Both values present: both are added.</summary>
        Both,

        /// <summary>Only the light value is present: the vent value is skipped.</summary>
        LightOnly,

        /// <summary>Only the vent value is present: the light value is skipped.</summary>
        VentOnly,

        /// <summary>Both empty: the window adds nothing and is listed as skipped.</summary>
        Neither
    }

    public enum RoomLightVentStatus
    {
        WillChange,
        UpToDate,

        /// <summary>No counted window: the room is left unchanged, by rule.</summary>
        NoExteriorWindows,

        /// <summary>Counted windows exist, but every value was empty: nothing to write.</summary>
        NoValues,

        /// <summary>A target parameter is missing or read-only on this room.</summary>
        CannotWrite
    }

    /// <summary>One counted window's contribution. Areas in square feet (Revit internal units).</summary>
    public sealed class WindowContribution
    {
        public long RoomId { get; set; }

        public double? Light { get; set; }

        public double? Vent { get; set; }

        public WindowValueState State =>
            Light != null && Vent != null ? WindowValueState.Both
            : Light != null ? WindowValueState.LightOnly
            : Vent != null ? WindowValueState.VentOnly
            : WindowValueState.Neither;
    }

    /// <summary>A room's current state, read from Revit. Areas in square feet.</summary>
    public sealed class RoomCurrent
    {
        public long RoomId { get; set; }

        /// <summary>Null when the parameter is empty.</summary>
        public double? CurrentLight { get; set; }

        public double? CurrentVent { get; set; }

        public bool CanWriteLight { get; set; } = true;

        public bool CanWriteVent { get; set; } = true;
    }

    public sealed class RoomTotals
    {
        public long RoomId { get; set; }

        public int CountedWindows { get; set; }

        /// <summary>Counted windows with at least one empty value.</summary>
        public int WindowsWithEmptyValues { get; set; }

        /// <summary>Sum of the light values present. Null when no window had one: then light is not written.</summary>
        public double? NewLight { get; set; }

        public double? NewVent { get; set; }

        public bool LightChanges { get; set; }

        public bool VentChanges { get; set; }

        public RoomLightVentStatus Status { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Adds up the counted windows of each room and decides what would be written.
    /// Rules approved for this tool:
    ///  - an empty value is not added; the other value of the same window still counts;
    ///  - a room with no counted window is left unchanged;
    ///  - a total is only written when at least one window supplied that value.
    /// </summary>
    public static class LightVentAggregator
    {
        /// <summary>Two areas closer than this (ft²) are treated as equal.</summary>
        public const double Tolerance = 1e-4;

        public static List<RoomTotals> Aggregate(
            IEnumerable<RoomCurrent> rooms,
            IEnumerable<WindowContribution> counted)
        {
            Dictionary<long, List<WindowContribution>> byRoom = counted
                .GroupBy(c => c.RoomId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<RoomTotals>();
            foreach (RoomCurrent room in rooms)
            {
                var totals = new RoomTotals { RoomId = room.RoomId };
                result.Add(totals);

                if (!byRoom.TryGetValue(room.RoomId, out List<WindowContribution> windows) || windows.Count == 0)
                {
                    totals.Status = RoomLightVentStatus.NoExteriorWindows;
                    totals.Note = "no exterior windows · left unchanged";
                    continue;
                }

                totals.CountedWindows = windows.Count;
                totals.WindowsWithEmptyValues = windows.Count(w => w.State != WindowValueState.Both);
                totals.NewLight = SumPresent(windows.Select(w => w.Light));
                totals.NewVent = SumPresent(windows.Select(w => w.Vent));

                if (totals.NewLight == null && totals.NewVent == null)
                {
                    totals.Status = RoomLightVentStatus.NoValues;
                    totals.Note = "every window value is empty · left unchanged";
                    continue;
                }

                var missing = new List<string>();
                if (totals.NewLight != null && !room.CanWriteLight) missing.Add("light");
                if (totals.NewVent != null && !room.CanWriteVent) missing.Add("vent");
                if (missing.Count > 0)
                {
                    totals.Status = RoomLightVentStatus.CannotWrite;
                    totals.Note = "target parameter for " + string.Join(" and ", missing) + " is missing or read-only";
                    continue;
                }

                totals.LightChanges = totals.NewLight != null && !SameArea(room.CurrentLight, totals.NewLight.Value);
                totals.VentChanges = totals.NewVent != null && !SameArea(room.CurrentVent, totals.NewVent.Value);
                totals.Status = totals.LightChanges || totals.VentChanges
                    ? RoomLightVentStatus.WillChange
                    : RoomLightVentStatus.UpToDate;

                if (totals.WindowsWithEmptyValues > 0)
                {
                    totals.Note = totals.WindowsWithEmptyValues == 1
                        ? "1 window with an empty value"
                        : totals.WindowsWithEmptyValues + " windows with an empty value";
                }
            }
            return result;
        }

        public static bool SameArea(double? current, double proposed) =>
            current != null && Math.Abs(current.Value - proposed) < Tolerance;

        private static double? SumPresent(IEnumerable<double?> values)
        {
            double sum = 0;
            bool any = false;
            foreach (double? v in values)
            {
                if (v == null) continue;
                sum += v.Value;
                any = true;
            }
            return any ? sum : (double?)null;
        }
    }
}
