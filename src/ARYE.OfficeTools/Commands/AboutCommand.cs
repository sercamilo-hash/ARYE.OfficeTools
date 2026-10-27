using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ARYE.OfficeTools.Commands
{
    /// <summary>
    /// Version, the BauTools commit the buttons were copied from, and where the log is — the
    /// first questions on any bug report. Own file of this repo: never overwritten by a promotion.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string location = Assembly.GetExecutingAssembly().Location;
            string buildDate = System.IO.File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm");
            string commit = PromotedFrom.Commit.Length >= 7 ? PromotedFrom.Commit.Substring(0, 7) : PromotedFrom.Commit;

            var dialog = new TaskDialog("About ARYE Office Tools")
            {
                MainInstruction = $"ARYE Office Tools {App.Version}",
                MainContent =
                    "NYC Lot Boundary · Generate Levels · Bubble Heads · Zoning Floor Area · " +
                    "Region Area → Param · Light & Vent\n\n" +
                    $"Copied from BauTools {commit} on {PromotedFrom.Date}\n" +
                    $"Build date: {buildDate}\n" +
                    $"Revit: {commandData.Application.Application.VersionName}\n" +
                    $"Installed at: {System.IO.Path.GetDirectoryName(location)}\n" +
                    $"Log folder: {App.Log.Folder}\n\n" +
                    "ARYE / KHL Architecture — Arch. Sergio Castro",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
