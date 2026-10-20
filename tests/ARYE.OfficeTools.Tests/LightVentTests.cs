using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.LightVent;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class LightVentTests
    {
        private static readonly HashSet<long> Scope = new HashSet<long> { 301, 302 };

        // ── Window → room ────────────────────────────────────────────────

        [Fact]
        public void Resolve_OneSideRoom_CountsForThatRoom()
        {
            WindowResolution r = WindowRoomResolver.Resolve(new WindowSides { ToRoom = 301 }, Scope);

            Assert.Equal(WindowOutcome.Counted, r.Outcome);
            Assert.Equal(301, r.RoomId);
            Assert.Equal(RoomFoundBy.ToRoom, r.FoundBy);
        }

        [Fact]
        public void Resolve_FromRoomSide_ReportsFromRoom()
        {
            WindowResolution r = WindowRoomResolver.Resolve(new WindowSides { FromRoom = 302 }, Scope);

            Assert.Equal(WindowOutcome.Counted, r.Outcome);
            Assert.Equal(RoomFoundBy.FromRoom, r.FoundBy);
        }

        [Fact]
        public void Resolve_RoomOnBothSides_IsInterior() =>
            Assert.Equal(WindowOutcome.Interior,
                WindowRoomResolver.Resolve(new WindowSides { FromRoom = 301, ToRoom = 302 }, Scope).Outcome);

        [Fact]
        public void Resolve_SameRoomOnBothSides_IsInterior() =>
            Assert.Equal(WindowOutcome.Interior,
                WindowRoomResolver.Resolve(new WindowSides { FromRoom = 301, ToRoom = 301 }, Scope).Outcome);

        [Fact]
        public void Resolve_NoRoom_IsReported() =>
            Assert.Equal(WindowOutcome.NoRoom, WindowRoomResolver.Resolve(new WindowSides(), Scope).Outcome);

        [Fact]
        public void Resolve_Skylight_IsExcludedEvenWithARoom() =>
            Assert.Equal(WindowOutcome.Skylight,
                WindowRoomResolver.Resolve(new WindowSides { IsSkylight = true, ToRoom = 301 }, Scope).Outcome);

        [Fact]
        public void Resolve_RoomNotInRun_IsOutsideScope()
        {
            WindowResolution r = WindowRoomResolver.Resolve(new WindowSides { ToRoom = 999 }, Scope);

            Assert.Equal(WindowOutcome.OutsideScope, r.Outcome);
            Assert.Equal(999, r.RoomId);
        }

        [Fact]
        public void Resolve_CurtainPanelWithoutFromTo_UsesProbe()
        {
            var w = new WindowSides { IsCurtainPanel = true, ProbeFront = null, ProbeBack = 302 };

            WindowResolution r = WindowRoomResolver.Resolve(w, Scope);

            Assert.Equal(WindowOutcome.Counted, r.Outcome);
            Assert.Equal(302, r.RoomId);
            Assert.Equal(RoomFoundBy.Probe, r.FoundBy);
        }

        [Fact]
        public void Resolve_CurtainPanelWithToRoom_IgnoresProbe()
        {
            // The probe is a fallback: when Revit already knows the room, its answer wins.
            var w = new WindowSides { IsCurtainPanel = true, ToRoom = 301, ProbeFront = 301, ProbeBack = 302 };

            WindowResolution r = WindowRoomResolver.Resolve(w, Scope);

            Assert.Equal(WindowOutcome.Counted, r.Outcome);
            Assert.Equal(RoomFoundBy.ToRoom, r.FoundBy);
        }

        [Fact]
        public void Resolve_LinkedWindow_UsesOnlyTheProbe()
        {
            var w = new WindowSides { IsLinked = true, ToRoom = 302, ProbeFront = 301, ProbeBack = null };

            WindowResolution r = WindowRoomResolver.Resolve(w, Scope);

            Assert.Equal(301, r.RoomId);
            Assert.Equal(RoomFoundBy.Probe, r.FoundBy);
        }

        // ── Totals ───────────────────────────────────────────────────────

        private static RoomTotals One(RoomCurrent room, params WindowContribution[] windows) =>
            LightVentAggregator.Aggregate(new[] { room }, windows).Single();

        [Fact]
        public void Aggregate_SumsBothValues()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301 },
                new WindowContribution { RoomId = 301, Light = 9.8, Vent = 4.6 },
                new WindowContribution { RoomId = 301, Light = 8.2, Vent = 8.2 });

            Assert.Equal(18.0, t.NewLight.Value, 6);
            Assert.Equal(12.8, t.NewVent.Value, 6);
            Assert.Equal(RoomLightVentStatus.WillChange, t.Status);
            Assert.Equal(2, t.CountedWindows);
        }

        [Fact]
        public void Aggregate_EmptyValueIsNotAdded_OtherValueStillCounts()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301 },
                new WindowContribution { RoomId = 301, Light = 9.8, Vent = 4.6 },
                new WindowContribution { RoomId = 301, Light = 5.0, Vent = null });

            Assert.Equal(14.8, t.NewLight.Value, 6);
            Assert.Equal(4.6, t.NewVent.Value, 6);
            Assert.Equal(1, t.WindowsWithEmptyValues);
        }

        [Fact]
        public void Aggregate_NoWindowHasVent_VentIsNotWritten()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301, CurrentVent = 7.0 },
                new WindowContribution { RoomId = 301, Light = 9.8, Vent = null });

            Assert.Null(t.NewVent);
            Assert.False(t.VentChanges);
            Assert.True(t.LightChanges);
        }

        [Fact]
        public void Aggregate_AllValuesEmpty_LeavesRoomUnchanged()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301 },
                new WindowContribution { RoomId = 301 });

            Assert.Equal(RoomLightVentStatus.NoValues, t.Status);
            Assert.False(t.LightChanges || t.VentChanges);
        }

        [Fact]
        public void Aggregate_RoomWithoutWindows_IsLeftUnchanged()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 302, CurrentLight = 2.1, CurrentVent = 1.05 });

            Assert.Equal(RoomLightVentStatus.NoExteriorWindows, t.Status);
            Assert.Null(t.NewLight);
            Assert.False(t.LightChanges || t.VentChanges);
        }

        [Fact]
        public void Aggregate_MatchingValues_AreUpToDate()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301, CurrentLight = 7.9, CurrentVent = 3.7 },
                new WindowContribution { RoomId = 301, Light = 7.9, Vent = 3.7 });

            Assert.Equal(RoomLightVentStatus.UpToDate, t.Status);
        }

        [Fact]
        public void Aggregate_EmptyCurrentValue_IsAChangeEvenWhenNewIsZero()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301, CurrentLight = 9.8, CurrentVent = null },
                new WindowContribution { RoomId = 301, Light = 9.8, Vent = 0 });

            Assert.True(t.VentChanges);
            Assert.Equal(RoomLightVentStatus.WillChange, t.Status);
        }

        [Fact]
        public void Aggregate_ReadOnlyTarget_IsReportedNotWritten()
        {
            RoomTotals t = One(new RoomCurrent { RoomId = 301, CanWriteVent = false },
                new WindowContribution { RoomId = 301, Light = 9.8, Vent = 4.6 });

            Assert.Equal(RoomLightVentStatus.CannotWrite, t.Status);
            Assert.Contains("vent", t.Note);
        }

        [Fact]
        public void Aggregate_KeepsEveryRoom_InInputOrder()
        {
            List<RoomTotals> all = LightVentAggregator.Aggregate(
                new[] { new RoomCurrent { RoomId = 302 }, new RoomCurrent { RoomId = 301 } },
                new[] { new WindowContribution { RoomId = 301, Light = 1, Vent = 1 } });

            Assert.Equal(new long[] { 302, 301 }, all.Select(t => t.RoomId));
        }
    }
}
