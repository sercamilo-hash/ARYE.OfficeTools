using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Infrastructure;
using ARYE.OfficeTools.ViewModels;
using ARYE.OfficeTools.Views;

namespace ARYE.OfficeTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ZfaCommand : IExternalCommand
    {
        private static MainWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Boundary catch (rule 5.2b): anything that escapes here becomes Revit's
            // generic "Command Failure" dialog with no diagnostics. Report and log
            // the real exception instead.
            try
            {
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                WpfHost.EnsureApplication();

                // ExternalEvent.Create requires a valid API context — here, not in the window.
                var dispatcher = new RevitDispatcher(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var shell = new ShellViewModel(dispatcher);

                _window = new MainWindow(shell, commandData.Application.MainWindowHandle);
                _window.Closed += (_, __) => _window = null;
                _window.Show();

                App.Log?.Info("ZFA window opened.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("ZfaCommand failed to open the window", ex);
                var dialog = new TaskDialog("BauTools")
                {
                    MainInstruction = "The Zoning Floor Area window could not open.",
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
