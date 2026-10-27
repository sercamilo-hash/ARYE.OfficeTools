using System;
using System.Collections.Generic;
using System.Linq;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class FloorRangeMathTests
    {
        /// <summary>L01..Ln bottom-up, 10 ft apart, UniqueId = name.</summary>
        private static List<LevelRef> Levels(int count, int startNumber = 1) =>
            Enumerable.Range(startNumber, count)
                .Select(n => new LevelRef
                {
                    UniqueId = "L" + n.ToString("D2"),
                    Name = "L" + n.ToString("D2"),
                    ElevationFt = n * 10.0
                })
                .ToList();

        // ── Pair generation ──────────────────────────────────────────────

        [Fact]
        public void GeneratePairs_EvenCount_PairsBottomUpTwoByTwo()
        {
            List<LevelRef> range = Levels(4, startNumber: 10); // L10..L13

            DuplexPairing pairing = FloorRangeMath.GeneratePairs(range);

            Assert.Equal(2, pairing.Pairs.Count);
            Assert.Equal("L10", pairing.Pairs[0].Lower.Name);
            Assert.Equal("L11", pairing.Pairs[0].Upper.Name);
            Assert.Equal("L12", pairing.Pairs[1].Lower.Name);
            Assert.Equal("L13", pairing.Pairs[1].Upper.Name);
            Assert.Null(pairing.UnpairedLevel);
        }

        [Fact]
        public void GeneratePairs_OddCount_LeavesTopmostUnpaired()
        {
            List<LevelRef> range = Levels(5, startNumber: 10); // L10..L14

            DuplexPairing pairing = FloorRangeMath.GeneratePairs(range);

            Assert.Equal(2, pairing.Pairs.Count);
            Assert.NotNull(pairing.UnpairedLevel);
            Assert.Equal("L14", pairing.UnpairedLevel.Name);
        }

        [Fact]
        public void GeneratePairs_SingleLevel_NoPairsAndUnpaired()
        {
            DuplexPairing pairing = FloorRangeMath.GeneratePairs(Levels(1));

            Assert.Empty(pairing.Pairs);
            Assert.Equal("L01", pairing.UnpairedLevel.Name);
        }

        // ── Contiguity ───────────────────────────────────────────────────

        [Fact]
        public void IsContiguous_ConsecutiveRun_True()
        {
            List<LevelRef> all = Levels(8);
            var members = new HashSet<string> { "L03", "L04", "L05" };

            Assert.True(FloorRangeMath.IsContiguous(all, members));
        }

        [Fact]
        public void IsContiguous_GapInMembers_False()
        {
            List<LevelRef> all = Levels(8);
            var members = new HashSet<string> { "L03", "L05" }; // L04 missing

            Assert.False(FloorRangeMath.IsContiguous(all, members));
        }

        [Fact]
        public void IsContiguous_MemberMissingFromLevelList_False()
        {
            List<LevelRef> all = Levels(4);
            var members = new HashSet<string> { "L03", "L04", "L99" };

            Assert.False(FloorRangeMath.IsContiguous(all, members));
        }

        [Fact]
        public void IsContiguous_EmptyMemberSet_True()
        {
            Assert.True(FloorRangeMath.IsContiguous(Levels(3), new HashSet<string>()));
        }

        // ── Standard round-trip: range → FloorGroup → range ──────────────

        [Fact]
        public void StandardGroup_RoundTripsThroughFloorGroupShape()
        {
            List<LevelRef> range = Levels(5, startNumber: 2); // L02..L06
            LevelRef source = range[2];                       // L04
            var id = Guid.NewGuid();

            FloorGroup group = FloorRangeMath.BuildStandardGroup(id, "Typical 1", range, source);

            Assert.Equal(id, group.Id);
            Assert.Equal(FloorGroupKind.Standard, group.Kind);
            Assert.Equal("L04", group.SourceLevel.Name);
            Assert.Equal(new[] { "L02", "L03", "L05", "L06" },
                group.TargetLevels.Select(l => l.Name).ToArray());
            Assert.Empty(group.TargetPairs);

            // Reverse: members reconstruct the exact From–To span, contiguous.
            List<LevelRef> members = FloorRangeMath.CollectStandardMembers(group);
            Assert.Equal(new[] { "L02", "L03", "L04", "L05", "L06" },
                members.Select(l => l.Name).ToArray());
            Assert.True(FloorRangeMath.IsContiguous(
                Levels(10), new HashSet<string>(members.Select(m => m.UniqueId))));
        }

        [Fact]
        public void StandardGroup_SingleLevelRange_HasNoTargets()
        {
            List<LevelRef> range = Levels(1, startNumber: 7);

            FloorGroup group = FloorRangeMath.BuildStandardGroup(
                Guid.NewGuid(), "Lone", range, range[0]);

            Assert.Equal("L07", group.SourceLevel.Name);
            Assert.Empty(group.TargetLevels);
        }

        // ── Duplex round-trip: range → FloorGroup → range ────────────────

        [Fact]
        public void DuplexGroup_RoundTripsThroughFloorGroupShape()
        {
            List<LevelRef> range = Levels(6, startNumber: 10); // L10..L15 → 3 pairs
            var id = Guid.NewGuid();

            FloorGroup group = FloorRangeMath.BuildDuplexGroup(id, "Duplex 1", range, sourcePairIndex: 1);

            Assert.Equal(FloorGroupKind.Duplex, group.Kind);
            Assert.Equal("L12", group.SourceLevel.Name);      // source pair lower
            Assert.Equal("L13", group.SourceUpperLevel.Name); // source pair upper
            Assert.Equal(2, group.TargetPairs.Count);         // all pairs except the source
            Assert.Equal("L10", group.TargetPairs[0].Lower.Name);
            Assert.Equal("L11", group.TargetPairs[0].Upper.Name);
            Assert.Equal("L14", group.TargetPairs[1].Lower.Name);
            Assert.Equal("L15", group.TargetPairs[1].Upper.Name);

            // Reverse: source pair + target pairs rebuild the full span in order.
            List<LevelRef> members = FloorRangeMath.CollectDuplexMembers(group);
            Assert.Equal(new[] { "L10", "L11", "L12", "L13", "L14", "L15" },
                members.Select(l => l.Name).ToArray());
        }

        [Fact]
        public void DuplexGroup_OddRange_UnpairedLevelIsNotPersisted()
        {
            List<LevelRef> range = Levels(5, startNumber: 10); // L10..L14, L14 unpaired

            FloorGroup group = FloorRangeMath.BuildDuplexGroup(
                Guid.NewGuid(), "Duplex", range, sourcePairIndex: 0);

            List<LevelRef> members = FloorRangeMath.CollectDuplexMembers(group);
            Assert.DoesNotContain(members, m => m.Name == "L14");
            Assert.Equal(4, members.Count);
        }

        [Fact]
        public void DuplexGroup_SourcePairIndexOutOfRange_FallsBackToFirstPair()
        {
            List<LevelRef> range = Levels(4, startNumber: 10);

            FloorGroup group = FloorRangeMath.BuildDuplexGroup(
                Guid.NewGuid(), "Duplex", range, sourcePairIndex: 9);

            Assert.Equal("L10", group.SourceLevel.Name);
            Assert.Single(group.TargetPairs);
        }

        // ── Source-only members: the range's diagrammed level(s), not every target ──

        [Fact]
        public void CollectSourceMembers_StandardGroup_ReturnsOnlyTheSourceLevel()
        {
            List<LevelRef> range = Levels(5, startNumber: 2); // L02..L06
            FloorGroup group = FloorRangeMath.BuildStandardGroup(Guid.NewGuid(), "Typical", range, range[2]); // source L04

            List<LevelRef> members = FloorRangeMath.CollectSourceMembers(group);

            Assert.Equal(new[] { "L04" }, members.Select(l => l.Name).ToArray());
        }

        [Fact]
        public void CollectSourceMembers_DuplexGroup_ReturnsBothSourceLevelsOfThePair()
        {
            List<LevelRef> range = Levels(6, startNumber: 10); // L10..L15 → 3 pairs
            FloorGroup group = FloorRangeMath.BuildDuplexGroup(Guid.NewGuid(), "Duplex", range, sourcePairIndex: 1); // source pair L12/L13

            List<LevelRef> members = FloorRangeMath.CollectSourceMembers(group);

            Assert.Equal(new[] { "L12", "L13" }, members.Select(l => l.Name).ToArray());
        }

        [Fact]
        public void CollectSourceMembers_SingleLevelStandardGroup_ReturnsThatOneLevel()
        {
            List<LevelRef> range = Levels(1, startNumber: 7);
            FloorGroup group = FloorRangeMath.BuildStandardGroup(Guid.NewGuid(), "Lone", range, range[0]);

            List<LevelRef> members = FloorRangeMath.CollectSourceMembers(group);

            Assert.Equal(new[] { "L07" }, members.Select(l => l.Name).ToArray());
        }

        [Fact]
        public void CollectSourceMembers_NullGroup_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => FloorRangeMath.CollectSourceMembers(null));
        }
    }
}
