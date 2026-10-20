namespace ARYE.OfficeTools.Core.Results
{
    /// <summary>Something intentionally not done, and why. Skips are shown to the user — never silent.</summary>
    public sealed class OperationSkip
    {
        public OperationSkip(string subject, string reason)
        {
            Subject = subject;
            Reason = reason;
        }

        /// <summary>What was skipped, e.g. "Area 'OFFICE 3' on L07".</summary>
        public string Subject { get; }

        public string Reason { get; }

        public override string ToString() => Subject + " — " + Reason;
    }
}
