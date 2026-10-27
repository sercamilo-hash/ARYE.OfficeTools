using System;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.ViewModels;
using ARYE.OfficeTools.Views;

namespace ARYE.OfficeTools.Commands
{
    /// <summary>Standalone tool: creates a run of evenly-spaced levels (and,
    /// optionally, a Floor Plan view per level) from a base elevation and a
    /// typical floor-to-floor height. Independent of the ZFA workflow — useful
    /// before a building's level structure exists at all.</summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class GenerateLevelsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Boundary catch (rule 5.2b): anything that escapes here becomes Revit's
            // generic "Command Failure" dialog with no diagnostics. Report and log
            // the real exception instead.
            try
            {
                Document doc = commandData.Application.ActiveUIDocument.Document;

                var existingLevels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Select(l => new LevelRef { UniqueId = l.UniqueId, Name = l.Name, ElevationFt = l.Elevation })
                    .ToList();

                Infrastructure.WpfHost.EnsureApplication();
                var viewModel = new GenerateLevelsViewModel(existingLevels);
                var window = new GenerateLevelsWindow(viewModel);
                if (commandData.Application.MainWindowHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
                }

                bool? accepted = window.ShowDialog();
                LevelPlanResult plan = viewModel.PlanResult;
                if (accepted != true || plan == null || plan.ToCreate.Count == 0)
                {
                    return Result.Succeeded;
                }

                ViewFamilyType floorPlanType = null;
                if (viewModel.CreateViews)
                {
                    floorPlanType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
                }

                using (var tx = new Transaction(doc, "BauTools: Generate levels"))
                {
                    tx.Start();
                    foreach (PlannedLevel planned in plan.ToCreate)
                    {
                        Level level = Level.Create(doc, planned.ElevationFt);
                        level.Name = planned.Name;

                        if (floorPlanType != null)
                        {
                            ViewPlan.Create(doc, floorPlanType.Id, level.Id);
                        }
                    }
                    tx.Commit();
                }

                int skipped = plan.Skipped.Count;
                string summary = plan.ToCreate.Count + " level(s) created" +
                    (skipped > 0 ? ", " + skipped + " skipped (already existed)." : ".");
                App.Log?.Info("GenerateLevelsCommand: " + summary);
                TaskDialog.Show("BauTools", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                App.Log?.Error("GenerateLevelsCommand failed", ex);
                var dialog = new TaskDialog("BauTools")
                {
                    MainInstruction = "The level generator could not finish.",
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
