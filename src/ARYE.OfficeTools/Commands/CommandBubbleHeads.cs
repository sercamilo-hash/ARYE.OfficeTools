using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Views;

namespace ARYE.OfficeTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class CommandBubbleHeads : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                View activeView = doc.ActiveView;

                if (activeView == null)
                {
                    TaskDialog.Show("BauTools — Bubble Heads", "No active view is currently open.");
                    return Result.Cancelled;
                }

                int gridCount = new FilteredElementCollector(doc, activeView.Id).OfClass(typeof(Grid)).GetElementCount();
                int levelCount = new FilteredElementCollector(doc, activeView.Id).OfClass(typeof(Level)).GetElementCount();
                if (gridCount == 0 && levelCount == 0)
                {
                    TaskDialog.Show("BauTools — Bubble Heads",
                        $"No visible Grids or Levels found in active view '{activeView.Name}'.\n\n" +
                        "Open a Floor Plan, Elevation, or Section view that contains datum elements and try again.");
                    return Result.Cancelled;
                }

                var window = new BubbleHeadsWindow(doc, activeView);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("CommandBubbleHeads failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
