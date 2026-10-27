using System;
using Autodesk.Revit.UI;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// What a step ViewModel needs from its host window: a way to run work on
    /// the Revit API thread, the busy flag, and a place to show a result.
    /// Implemented by the ZFA shell and by the standalone Sheet Diagrammer
    /// shell, so the same step ViewModel serves both.
    /// </summary>
    internal interface IRevitShell
    {
        bool IsBusy { get; }

        void RunRevit(string operationName, Func<UIApplication, object> work,
            Action<object> onCompleted, Action<Exception> onFailed = null);

        void ShowResult(string header, string summary);
    }
}
