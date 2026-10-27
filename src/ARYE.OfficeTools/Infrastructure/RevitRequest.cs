using System;
using Autodesk.Revit.UI;

namespace ARYE.OfficeTools.Infrastructure
{
    internal sealed class RevitRequest
    {
        public RevitRequest(string operationName, Func<UIApplication, object> work,
            Action<object> onCompleted, Action<Exception> onFailed)
        {
            OperationName = operationName;
            Work = work;
            OnCompleted = onCompleted;
            OnFailed = onFailed;
        }

        public string OperationName { get; }

        /// <summary>Runs on the Revit API thread. Opens its own Transaction if it writes.</summary>
        public Func<UIApplication, object> Work { get; }

        /// <summary>Runs on the WPF dispatcher after Work returns.</summary>
        public Action<object> OnCompleted { get; }

        /// <summary>Runs on the WPF dispatcher when Work throws.</summary>
        public Action<Exception> OnFailed { get; }
    }
}
