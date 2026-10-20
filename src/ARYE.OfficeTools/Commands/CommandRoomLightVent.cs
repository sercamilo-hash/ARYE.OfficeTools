using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Views;

namespace ARYE.OfficeTools.Commands
{
    /// <summary>
    /// BauTools · Room Light &amp; Vent.
    /// Sums the light and vent area of each room's exterior windows and writes the two totals
    /// to the room. TransactionMode.Manual: the only write happens inside the service's own transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class CommandRoomLightVent : IExternalCommand
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

                // Any plan view that shows rooms works (floor, ceiling, structural plans, dependents).
                // Whether it actually has rooms is checked below, with the view named in the message.
                if (!(uidoc.ActiveView is ViewPlan plan))
                {
                    TaskDialog.Show("BauTools",
                        "The active view “" + uidoc.ActiveView?.Name + "” is a " + uidoc.ActiveView?.ViewType + ", not a plan.\n\n" +
                        "Open a plan view that shows the rooms, then click Light & Vent again. The tool uses the rooms " +
                        "visible in the active view (or the rooms you selected in it).");
                    return Result.Cancelled;
                }

                ICollection<ElementId> preselected = uidoc.Selection.GetElementIds();
                var window = new RoomLightVentWindow(uidoc, preselected);
                if (!window.HasRooms)
                {
                    string extra = window.NotEnclosedRooms > 0
                        ? $"\n\n{window.NotEnclosedRooms} room(s) were found but are not placed or not enclosed (area 0)."
                        : string.Empty;
                    string hint = plan.ViewType == ViewType.AreaPlan
                        ? "\n\nThis is an Area Plan: it shows Areas, not Rooms. Open a floor plan of the same level."
                        : string.Empty;
                    TaskDialog.Show("BauTools",
                        "No rooms to process in the view “" + plan.Name + "” (" + plan.ViewType + ")." + extra + hint);
                    return Result.Cancelled;
                }

                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("CommandRoomLightVent failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
