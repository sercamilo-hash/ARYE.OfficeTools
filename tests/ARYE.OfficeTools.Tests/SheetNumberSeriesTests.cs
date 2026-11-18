using System;
using System.Collections.Generic;
using ARYE.OfficeTools.Core.Sheets;
using Xunit;

namespace ARYE.OfficeTools.Tests
{
    public class SheetNumberSeriesTests
    {
        private static ISet<string> Set(params string[] values) =>
            new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void FreshSeries_AllocatesFromStart()
        {
            SheetNumberAllocation allocation =
                SheetNumberSeries.Allocate("Z-", 100, 1, 3, Set(), Set());

            Assert.Equal(new[] { "Z-100", "Z-101", "Z-102" }, allocation.Numbers);
            Assert.Empty(allocation.SkippedNumbers);
        }

        [Fact]
        public void OwnExistingSheets_ConsumeSeriesPositions_Silently()
        {
            // The package already owns Z-100 and Z-101 — the next new sheet is Z-102.
            SheetNumberAllocation allocation = SheetNumberSeries.Allocate(
                "Z-", 100, 1, 2, Set("Z-100", "Z-101"), Set());

            Assert.Equal(new[] { "Z-102", "Z-103" }, allocation.Numbers);
            Assert.Empty(allocation.SkippedNumbers);
        }

        [Fact]
        public void ForeignCollision_SkipsToNextFree_AndReports()
        {
            // Z-101 belongs to somebody else: never overwritten, never blocking.
            SheetNumberAllocation allocation = SheetNumberSeries.Allocate(
                "Z-", 100, 1, 3, Set(), Set("Z-101"));

            Assert.Equal(new[] { "Z-100", "Z-102", "Z-103" }, allocation.Numbers);
            Assert.Equal(new[] { "Z-101" }, allocation.SkippedNumbers);
        }

        [Fact]
        public void Increment_StepsTheSeries()
        {
            SheetNumberAllocation allocation =
                SheetNumberSeries.Allocate("A-", 100, 10, 3, Set(), Set());
            Assert.Equal(new[] { "A-100", "A-110", "A-120" }, allocation.Numbers);
        }

        [Fact]
        public void NonPositiveIncrement_FallsBackToOne()
        {
            SheetNumberAllocation allocation =
                SheetNumberSeries.Allocate("A-", 1, 0, 2, Set(), Set());
            Assert.Equal(new[] { "A-1", "A-2" }, allocation.Numbers);
        }

        [Fact]
        public void ZeroCount_AllocatesNothing()
        {
            SheetNumberAllocation allocation =
                SheetNumberSeries.Allocate("Z-", 100, 1, 0, Set(), Set());
            Assert.Empty(allocation.Numbers);
            Assert.Empty(allocation.SkippedNumbers);
        }

        [Fact]
        public void MixedOwnAndForeign_ResolvesInOrder()
        {
            SheetNumberAllocation allocation = SheetNumberSeries.Allocate(
                "Z-", 100, 1, 2,
                Set("Z-100"),          // own sheet — silent series position
                Set("Z-101", "Z-103")); // foreign — reported skips

            Assert.Equal(new[] { "Z-102", "Z-104" }, allocation.Numbers);
            Assert.Equal(new[] { "Z-101", "Z-103" }, allocation.SkippedNumbers);
        }
    }
}
