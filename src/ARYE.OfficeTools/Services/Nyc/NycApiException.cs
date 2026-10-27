using System;

namespace ARYE.OfficeTools.Services.Nyc
{
    /// <summary>
    /// A NYC public API call failed. Carries a message the UI can show as-is —
    /// this project never swallows an HTTP failure into "lot not found".
    /// </summary>
    public sealed class NycApiException : Exception
    {
        public NycApiException(string message) : base(message) { }

        public NycApiException(string message, Exception inner) : base(message, inner) { }
    }
}
