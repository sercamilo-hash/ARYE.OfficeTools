using ARYE.OfficeTools.Core.Results;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class OperationResultTests
    {
        [Fact]
        public void CleanResult_HasNoProblems()
        {
            var result = new OperationResult<string>();
            result.Succeeded.Add("A");
            result.Succeeded.Add("B");

            Assert.False(result.HasProblems);
            Assert.Equal("2 succeeded, 0 skipped, 0 failed.", result.BuildSummary());
        }

        [Fact]
        public void SkipsAndFailures_AppearInSummaryWithReasons()
        {
            var result = new OperationResult<string>();
            result.Succeeded.Add("L03");
            result.Skipped.Add(new OperationSkip("L07", "atypical floor — excluded from propagation"));
            result.Failed.Add(new OperationFailure("Area 'OFFICE 3'", "not enclosed after regeneration"));

            Assert.True(result.HasProblems);
            string summary = result.BuildSummary();
            Assert.StartsWith("1 succeeded, 1 skipped, 1 failed.", summary);
            Assert.Contains("L07 — atypical floor", summary);
            Assert.Contains("Area 'OFFICE 3' — not enclosed", summary);
        }

        [Fact]
        public void Counts_DeriveFromLists_NeverStoredSeparately()
        {
            // The A4 regression guard: adding to a list is the only way to change a count.
            var result = new OperationResult<int>();
            for (int i = 0; i < 5; i++) result.Succeeded.Add(i);
            result.Succeeded.RemoveAt(0);

            Assert.StartsWith("4 succeeded", result.BuildSummary());
        }
    }
}
