using System;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace ARYE.OfficeTools.Infrastructure
{
    /// <summary>
    /// The only door ViewModels have into Revit. Must be constructed in a valid
    /// Revit API context (the command's Execute) because ExternalEvent.Create
    /// requires one.
    /// </summary>
    internal sealed class RevitDispatcher
    {
        private readonly RevitRequestHandler _handler;
        private readonly ExternalEvent _event;

        public RevitDispatcher(Dispatcher uiDispatcher)
        {
            _handler = new RevitRequestHandler(uiDispatcher);
            _event = ExternalEvent.Create(_handler);
        }

        /// <summary>
        /// Queues work for the Revit API thread. onCompleted/onFailed run on the
        /// WPF dispatcher. The work delegate opens its own Transaction if it writes.
        /// </summary>
        public void Enqueue(string operationName, Func<UIApplication, object> work,
            Action<object> onCompleted, Action<Exception> onFailed)
        {
            _handler.Enqueue(new RevitRequest(operationName, work, onCompleted, onFailed));
            _event.Raise();
        }
    }
}
