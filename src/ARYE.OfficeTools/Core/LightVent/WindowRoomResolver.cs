using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.LightVent
{
    /// <summary>What happened to one window in a Light &amp; Vent run.</summary>
    public enum WindowOutcome
    {
        /// <summary>Exterior window: exactly one side has a room, and that room is in scope.</summary>
        Counted,

        /// <summary>A room on both sides (or the same room on both sides).</summary>
        Interior,

        /// <summary>No room on either side.</summary>
        NoRoom,

        /// <summary>Hosted in a roof. Skylights are excluded by rule.</summary>
        Skylight,

        /// <summary>Exterior window whose only room is not one of the rooms of this run.</summary>
        OutsideScope
    }

    /// <summary>How the room of a counted window was found.</summary>
    public enum RoomFoundBy
    {
        None,
        FromRoom,
        ToRoom,

        /// <summary>A point tested on each side of the window against the rooms.</summary>
        Probe
    }

    /// <summary>
    /// Everything the resolver needs about one window, already read from Revit. Room ids are
    /// plain longs so this stays Revit-free.
    /// </summary>
    public sealed class WindowSides
    {
        public bool IsSkylight { get; set; }

        /// <summary>Hosted in a curtain wall: the probe is tried when FromRoom/ToRoom are both empty.</summary>
        public bool IsCurtainPanel { get; set; }

        /// <summary>From a linked model: FromRoom/ToRoom do not cross models, so only the probe is used.</summary>
        public bool IsLinked { get; set; }

        public long? FromRoom { get; set; }

        public long? ToRoom { get; set; }

        /// <summary>Room found by the point on the facing side. Only read when the probe applies.</summary>
        public long? ProbeFront { get; set; }

        /// <summary>Room found by the point on the opposite side. Only read when the probe applies.</summary>
        public long? ProbeBack { get; set; }

        /// <summary>True when the probe applies to this window (see <see cref="WindowRoomResolver"/>).</summary>
        public bool NeedsProbe => IsLinked || (IsCurtainPanel && FromRoom == null && ToRoom == null);
    }

    public sealed class WindowResolution
    {
        public WindowResolution(WindowOutcome outcome, long? roomId, RoomFoundBy foundBy, string reason)
        {
            Outcome = outcome;
            RoomId = roomId;
            FoundBy = foundBy;
            Reason = reason;
        }

        public WindowOutcome Outcome { get; }

        /// <summary>The room the window counts for (Counted / OutsideScope), otherwise null.</summary>
        public long? RoomId { get; }

        public RoomFoundBy FoundBy { get; }

        /// <summary>Plain-English reason, shown in the preview and the CSV.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Decides which room a window counts for. The rule approved for this tool: a window is
    /// exterior when exactly one of its two sides has a room. Skylights never count.
    /// </summary>
    public static class WindowRoomResolver
    {
        public static WindowResolution Resolve(WindowSides w, ISet<long> roomsInScope)
        {
            if (w.IsSkylight)
            {
                return new WindowResolution(WindowOutcome.Skylight, null, RoomFoundBy.None, "skylight (hosted in a roof)");
            }

            long? a, b;
            RoomFoundBy byA, byB;
            if (w.NeedsProbe)
            {
                a = w.ProbeFront;
                b = w.ProbeBack;
                byA = byB = RoomFoundBy.Probe;
            }
            else
            {
                a = w.FromRoom;
                b = w.ToRoom;
                byA = RoomFoundBy.FromRoom;
                byB = RoomFoundBy.ToRoom;
            }

            if (a != null && b != null)
            {
                string reason = a.Value == b.Value
                    ? "same room on both sides"
                    : "room on both sides (interior)";
                return new WindowResolution(WindowOutcome.Interior, null, RoomFoundBy.None, reason);
            }

            if (a == null && b == null)
            {
                return new WindowResolution(WindowOutcome.NoRoom, null, RoomFoundBy.None, "no room on either side");
            }

            long room = (a ?? b).Value;
            RoomFoundBy by = a != null ? byA : byB;
            if (roomsInScope == null || !roomsInScope.Contains(room))
            {
                return new WindowResolution(WindowOutcome.OutsideScope, room, by, "its room is not in this run");
            }

            return new WindowResolution(WindowOutcome.Counted, room, by, string.Empty);
        }

        public static string Describe(RoomFoundBy by)
        {
            switch (by)
            {
                case RoomFoundBy.FromRoom: return "FromRoom";
                case RoomFoundBy.ToRoom: return "ToRoom";
                case RoomFoundBy.Probe: return "geometry probe";
                default: return "—";
            }
        }
    }
}
