using System.Collections.Generic;
using System.Text;

namespace ARYE.OfficeTools.Core.Results
{
    /// <summary>
    /// The truthful outcome of a batch operation. Counts are derived from the
    /// lists, so they are structurally unable to lie — the direct fix for
    /// audit finding A4 ("counts that lie to the user").
    /// </summary>
    public sealed class OperationResult<T>
    {
        public List<T> Succeeded { get; } = new List<T>();

        public List<OperationSkip> Skipped { get; } = new List<OperationSkip>();

        public List<OperationFailure> Failed { get; } = new List<OperationFailure>();

        public bool HasProblems => Skipped.Count > 0 || Failed.Count > 0;

        public string BuildSummary()
        {
            var sb = new StringBuilder();
            sb.Append(Succeeded.Count).Append(" succeeded, ")
              .Append(Skipped.Count).Append(" skipped, ")
              .Append(Failed.Count).Append(" failed.");

            if (Skipped.Count > 0)
            {
                sb.AppendLine().AppendLine().Append("Skipped:");
                foreach (OperationSkip skip in Skipped)
                {
                    sb.AppendLine().Append("  • ").Append(skip);
                }
            }

            if (Failed.Count > 0)
            {
                sb.AppendLine().AppendLine().Append("Failed:");
                foreach (OperationFailure failure in Failed)
                {
                    sb.AppendLine().Append("  • ").Append(failure);
                }
            }

            return sb.ToString();
        }
    }
}
