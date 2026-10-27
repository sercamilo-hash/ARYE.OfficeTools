using System;

namespace ARYE.OfficeTools.Core.Logging
{
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception = null);
    }
}
