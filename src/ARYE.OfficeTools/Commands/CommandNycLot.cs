using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ARYE.OfficeTools.Views;

namespace ARYE.OfficeTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class CommandNycLot : IExternalCommand
    {
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
                    TaskDialog.Show("BauTools", "This tool runs in a project document, not in the Family Editor.");
                    return Result.Cancelled;
                }

                var window = new NycLotBoundaryWindow(uidoc.Document);
                // The window hides itself (ShowDialog returns) when the user asks to
                // pick property lines: picking needs Revit's own UI, which a modal
                // dialog blocks. Pick, hand the elements back, re-show the same window.
                while (true)
                {
                    window.ShowDialog();
                    if (!window.SurveyPickRequested)
                    {
                        break;
                    }
                    window.SurveyPickRequested = false;
                    List<CurveElement> picked = PickPropertyLines(uidoc);
                    window.ApplySurveyPick(picked);
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("CommandNycLot failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static List<CurveElement> PickPropertyLines(UIDocument uidoc)
        {
            try
            {
                IList<Reference> references = uidoc.Selection.PickObjects(ObjectType.Element, new CurveElementFilter(),
                    "Pick the surveyed property lines (model or detail lines), then Finish");
                return references.Select(r => uidoc.Document.GetElement(r)).OfType<CurveElement>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null; // Esc — nothing changes
            }
        }

        private sealed class CurveElementFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is CurveElement;

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
