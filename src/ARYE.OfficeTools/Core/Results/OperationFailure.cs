using System;

namespace ARYE.OfficeTools.Core.Results
{
    /// <summary>Something attempted that failed. String-based so Core stays Revit-free.</summary>
    public sealed class OperationFailure
    {
        public OperationFailure(string subject, string message)
        {
            Subject = subject;
            Message = message;
        }

        public OperationFailure(string subject, Exception exception)
            : this(subject, exception.GetType().Name + ": " + exception.Message)
        {
        }

        public string Subject { get; }

        public string Message { get; }

        public override string ToString() => Subject + " — " + Message;
    }
}
