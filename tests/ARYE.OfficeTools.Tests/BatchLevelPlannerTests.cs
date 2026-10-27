using System;
using System.Collections.Generic;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class BatchLevelPlannerTests
    {
        private static LevelGenerationRequest TypicalRequest() => new LevelGenerationRequest
        {
            BaseElevationFt = 0,
            Count = 5,
            TypicalHeightFt = 10,
            NamePrefix = "L",
            StartNumber = 1,
            NamePadding = 2
        };

        [Fact]
        public void Plan_GeneratesEvenlySpacedLevels_NamedByPattern()
        {
            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), new List<LevelRef>());

            Assert.Equal(5, result.ToCreate.Count);
            Assert.Empty(result.Skipped);
            Assert.Equal(new[] { "L01", "L02", "L03", "L04", "L05" },
                result.ToCreate.ConvertAll(l => l.Name));
            Assert.Equal(new[] { 0.0, 10.0, 20.0, 30.0, 40.0 },
                result.ToCreate.ConvertAll(l => l.ElevationFt));
        }

        [Fact]
        public void Plan_StartNumberAndPrefix_ProduceExpectedNames()
        {
            LevelGenerationRequest request = TypicalRequest();
            request.NamePrefix = "Roof ";
            request.StartNumber = 3;
            request.NamePadding = 1;
            request.Count = 2;

            LevelPlanResult result = BatchLevelPlanner.Plan(request, new List<LevelRef>());

            Assert.Equal(new[] { "Roof 3", "Roof 4" }, result.ToCreate.ConvertAll(l => l.Name));
        }

        [Fact]
        public void Plan_NameCollision_SkipsThatLevelAndReportsWhy()
        {
            var existing = new List<LevelRef> { new LevelRef { Name = "L03", ElevationFt = 999 } };

            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), existing);

            Assert.Equal(4, result.ToCreate.Count);
            Assert.DoesNotContain(result.ToCreate, l => l.Name == "L03");
            LevelPlanIssue issue = Assert.Single(result.Skipped);
            Assert.Equal("L03", issue.Level.Name);
            Assert.Equal(LevelPlanIssueKind.NameCollision, issue.Kind);
            Assert.Equal("L03", issue.ExistingLevelName);
        }

        [Fact]
        public void Plan_NameCollision_IsCaseInsensitive()
        {
            var existing = new List<LevelRef> { new LevelRef { Name = "l01", ElevationFt = 999 } };

            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), existing);

            Assert.DoesNotContain(result.ToCreate, l => l.Name == "L01");
            Assert.Single(result.Skipped, i => i.Kind == LevelPlanIssueKind.NameCollision);
        }

        [Fact]
        public void Plan_ElevationCollision_SkipsEvenWithADifferentName()
        {
            // Ground level from another workflow sits at 20ft — exactly where L03 would land.
            var existing = new List<LevelRef> { new LevelRef { Name = "Existing Ground", ElevationFt = 20.0 } };

            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), existing);

            Assert.Equal(4, result.ToCreate.Count);
            LevelPlanIssue issue = Assert.Single(result.Skipped);
            Assert.Equal("L03", issue.Level.Name);
            Assert.Equal(LevelPlanIssueKind.ElevationCollision, issue.Kind);
            Assert.Equal("Existing Ground", issue.ExistingLevelName);
        }

        [Fact]
        public void Plan_ElevationWithinTolerance_StillCollides()
        {
            var existing = new List<LevelRef>
            {
                new LevelRef { Name = "Existing", ElevationFt = 20.0 + BatchLevelPlanner.ElevationToleranceFt / 2 }
            };

            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), existing);

            Assert.Single(result.Skipped, i => i.Kind == LevelPlanIssueKind.ElevationCollision);
        }

        [Fact]
        public void Plan_ElevationJustOutsideTolerance_DoesNotCollide()
        {
            var existing = new List<LevelRef>
            {
                new LevelRef { Name = "Existing", ElevationFt = 20.0 + BatchLevelPlanner.ElevationToleranceFt * 2 }
            };

            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), existing);

            Assert.Equal(5, result.ToCreate.Count);
            Assert.Empty(result.Skipped);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Plan_NonPositiveCount_Throws(int count)
        {
            LevelGenerationRequest request = TypicalRequest();
            request.Count = count;

            Assert.Throws<ArgumentException>(() => BatchLevelPlanner.Plan(request, new List<LevelRef>()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Plan_NonPositiveHeight_Throws(double height)
        {
            LevelGenerationRequest request = TypicalRequest();
            request.TypicalHeightFt = height;

            Assert.Throws<ArgumentException>(() => BatchLevelPlanner.Plan(request, new List<LevelRef>()));
        }

        [Fact]
        public void Plan_NullRequest_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BatchLevelPlanner.Plan(null, new List<LevelRef>()));
        }

        [Fact]
        public void Plan_NullExistingLevels_TreatedAsEmpty()
        {
            LevelPlanResult result = BatchLevelPlanner.Plan(TypicalRequest(), null);

            Assert.Equal(5, result.ToCreate.Count);
            Assert.Empty(result.Skipped);
        }
    }
}
