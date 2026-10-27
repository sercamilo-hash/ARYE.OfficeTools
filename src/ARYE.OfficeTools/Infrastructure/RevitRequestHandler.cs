using System;
using System.Collections.Concurrent;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace ARYE.OfficeTools.Infrastructure
{
    /// <summary>
    /// The bridge between the modeless WPF window and the Revit API thread.
    /// The window enqueues work and raises the ExternalEvent; Revit calls
    /// Execute on its API thread; results are marshalled back to WPF.
    /// </summary>
    internal sealed class RevitRequestHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<RevitRequest> _queue = new ConcurrentQueue<RevitRequest>();
        private readonly Dispatcher _uiDispatcher;
        private string _currentOperation = "idle";

        public RevitRequestHandler(Dispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher;
        }

        public void Enqueue(RevitRequest request) => _queue.Enqueue(request);

        public void Execute(UIApplication app)
        {
            // Drain everything queued: Raise() coalesces, so one Execute may
            // need to serve several requests.
            while (_queue.TryDequeue(out RevitRequest request))
            {
                _currentOperation = request.OperationName;
                try
                {
                    object result = request.Work(app);
                    _uiDispatcher.BeginInvoke(request.OnCompleted, result);
                }
                catch (Exception ex)
                {
                    // Boundary catch: the exception crosses threads here, so it
                    // must be captured and reported — it cannot bubble to Revit
                    // without taking the ExternalEvent down.
                    App.Log?.Error($"Operation '{request.OperationName}' failed", ex);
                    _uiDispatcher.BeginInvoke(request.OnFailed, ex);
                }
                finally
                {
                    _currentOperation = "idle";
                }
            }
        }

        public string GetName() => "BauTools: " + _currentOperation;
    }
}
