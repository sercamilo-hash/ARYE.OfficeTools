using System;
using System.Collections.Generic;

namespace ARYE.OfficeTools.Core.Sheets
{
    /// <summary>The numbers a run will give its new sheets, plus every collision skipped.</summary>
    public sealed class SheetNumberAllocation
    {
        /// <summary>Allocated numbers, in placement order.</summary>
        public List<string> Numbers { get; } = new List<string>();

        /// <summary>
        /// Numbers of the series that were passed over because a sheet NOT
        /// owned by this package already carries them — reported, never
        /// overwritten, never blocking.
        /// </summary>
        public List<string> SkippedNumbers { get; } = new List<string>();
    }

    /// <summary>
    /// Generates the next sheet numbers of a package's prefix + start +
    /// increment series. Numbers already used by the package's own existing
    /// sheets are simply positions the series has consumed; numbers taken by
    /// foreign sheets are skipped and reported.
    /// </summary>
    public static class SheetNumberSeries
    {
        /// <summary>Safety cap: a pathological document never spins this forever.</summary>
        private const int MaxProbes = 10000;

        public static SheetNumberAllocation Allocate(
            string prefix, int startNumber, int increment, int count,
            ISet<string> ownExistingNumbers, ISet<string> foreignTakenNumbers)
        {
            if (count < 0) throw new ArgumentException("Count cannot be negative.", nameof(count));
            if (ownExistingNumbers == null) throw new ArgumentNullException(nameof(ownExistingNumbers));
            if (foreignTakenNumbers == null) throw new ArgumentNullException(nameof(foreignTakenNumbers));

            string safePrefix = prefix ?? string.Empty;
            int step = increment > 0 ? increment : 1;

            var allocation = new SheetNumberAllocation();
            long n = startNumber;
            for (int probes = 0; allocation.Numbers.Count < count && probes < MaxProbes; probes++, n += step)
            {
                string number = safePrefix + n;
                if (ownExistingNumbers.Contains(number))
                {
                    continue; // this position of the series is an existing package sheet
                }
                if (foreignTakenNumbers.Contains(number))
                {
                    allocation.SkippedNumbers.Add(number);
                    continue;
                }
                allocation.Numbers.Add(number);
            }
            if (allocation.Numbers.Count < count)
            {
                throw new InvalidOperationException(
                    $"Could not find {count} free sheet number(s) in the series " +
                    $"'{safePrefix}{startNumber}' + {step} after {MaxProbes} candidates.");
            }
            return allocation;
        }
    }
}
