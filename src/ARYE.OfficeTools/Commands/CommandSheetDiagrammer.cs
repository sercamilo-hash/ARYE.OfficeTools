using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Infrastructure;
using ARYE.OfficeTools.ViewModels;
using ARYE.OfficeTools.Views;

namespace ARYE.OfficeTools.Commands
{
    /// <summary>
    /// Step 4 · Sheets — the standalone Sheet Diagrammer. Modeless like the
    /// ZFA window (live preview needs the dispatcher); one instance at a time.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class CommandSheetDiagrammer : IExternalCommand
    {
        private static SheetDiagrammerWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc?.Document == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }
                if (uidoc.Document.IsFamilyDocument)
                {
                    TaskDialog.Show("BauTools", "Sheet Diagrammer runs in a project document, not in the Family Editor.");
                    return Result.Cancelled;
                }
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                WpfHost.EnsureApplication();

                // ExternalEvent.Create requires a valid API context — here, not in the window.
                var dispatcher = new RevitDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var shell = new SheetDiagrammerShellViewModel(dispatcher);
                _window = new SheetDiagrammerWindow(shell, commandData.Application.MainWindowHandle);
                _window.Closed += (_, __) => _window = null;
                _window.Show();
                App.Log?.Info("Sheet Diagrammer window opened.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("CommandSheetDiagrammer failed to open the window", ex);
                var dialog = new TaskDialog("BauTools")
                {
                    MainInstruction = "The Sheet Diagrammer window could not open.",
                    MainContent = ex.Message,
                    ExpandedContent = ex.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                dialog.Show();
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
