using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.RegionArea.Views;

namespace ARYE.RegionArea.Commands
{
    /// <summary>
    /// BauTools · Region Area → Parameter.
    /// Migrates the computed area of one or many Filled Regions into a writable parameter.
    /// TransactionMode.Manual: every write happens inside the service's own transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class CommandRegionAreaToParameter : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                if (uidoc.Document.IsFamilyDocument)
                {
                    TaskDialog.Show("BauTools", "This tool runs in a project document, not in the Family Editor.");
                    return Result.Cancelled;
                }

                RegionAreaMigratorWindow window = new RegionAreaMigratorWindow(uidoc);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("CommandRegionAreaToParameter failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
