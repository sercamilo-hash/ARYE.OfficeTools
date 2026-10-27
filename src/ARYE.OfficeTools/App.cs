using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace ARYE.OfficeTools
{
    /// <summary>
    /// ARYE Office Tools: two ribbon buttons on ARYE › Zoning — Region Area → Param and Light &amp; Vent.
    /// Keep this thin — a throw here means Revit shows a startup error and disables the add-in.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        private const string TabName = "ARYE";
        private const string PanelName = "Zoning";

        // Created on first use, not in OnStartup: when the buttons come from the pyRevit extension,
        // Revit never runs this IExternalApplication, but the commands still log their errors.
        public static Log Log { get; } = new Log();

        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                Log.Info($"ARYE Office Tools {Version} starting under Revit {application.ControlledApplication.VersionNumber}");

                try { application.CreateRibbonTab(TabName); }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Another ARYE add-in already created the tab: share it.
                }

                RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);

                var regionAreaButton = new PushButtonData(
                    "ARYE_RegionAreaToParameter",
                    "Region Area\n→ Param",
                    assemblyPath,
                    typeof(Commands.CommandRegionAreaToParameter).FullName)
                {
                    ToolTip = "Writes the computed area of one or many Filled Regions into a writable parameter " +
                              "(for example 'Region Area' on the energy elevations), with a before/after preview " +
                              "that must be confirmed before anything is written.",
                    LongDescription =
                        "Scope: current selection, active view or the whole model, filtered by region type and owner " +
                        "view. The area is computed from the region boundaries (exact for lines and arcs) and checked " +
                        "against Revit's own Area. The target selector only lists parameters that exist on those regions " +
                        "and are writable. Building and Side can be filled in the same run. Version " + Version + "."
                };
                ApplyIcon(regionAreaButton, assemblyDir, "icon_region_area");
                panel.AddItem(regionAreaButton);

                var lightVentButton = new PushButtonData(
                    "ARYE_RoomLightVent",
                    "Light &\nVent",
                    assemblyPath,
                    typeof(Commands.CommandRoomLightVent).FullName)
                {
                    ToolTip = "Adds up the light area and the vent area of each room's exterior windows and " +
                              "writes the two totals to the room, after a plan preview and a before/after table.",
                    LongDescription =
                        "Uses the rooms visible in the active plan view, or only the rooms you selected first. " +
                        "A window counts for a room when exactly one of its sides (FromRoom/ToRoom, in the view's " +
                        "phase) has a room; window panels in curtain walls count too, skylights do not. Window " +
                        "and room parameters are picked from lists of every parameter in the model. Empty values " +
                        "are not added, rooms without exterior windows are left unchanged, and every skipped " +
                        "window is listed with its reason. Version " + Version + "."
                };
                ApplyIcon(lightVentButton, assemblyDir, "icon_light_vent");
                panel.AddItem(lightVentButton);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log?.Error("Startup failed", ex);
                TaskDialog.Show("ARYE Office Tools", "Failed to start:\n" + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static void ApplyIcon(PushButtonData button, string assemblyDir, string baseName)
        {
            if (assemblyDir == null) return;
            string large = Path.Combine(assemblyDir, "Resources", baseName + "_32.png");
            string small = Path.Combine(assemblyDir, "Resources", baseName + "_16.png");
            if (File.Exists(large)) button.LargeImage = new BitmapImage(new Uri(large));
            if (File.Exists(small)) button.Image = new BitmapImage(new Uri(small));
        }
    }

    /// <summary>
    /// Minimal file log in %LOCALAPPDATA%\ARYE\OfficeTools\logs — one file per day. A failure to
    /// write the log is reported to the debugger and never stops the tool.
    /// </summary>
    public sealed class Log
    {
        private readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARYE", "OfficeTools", "logs");

        public string Folder => _dir;

        public void Info(string message) => Write("INFO ", message);

        public void Error(string message, Exception exception = null) =>
            Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);

        private void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(Path.Combine(_dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                    $"{DateTime.Now:HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine("ARYE Office Tools log failed: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine("ARYE Office Tools log failed: " + ex.Message);
            }
        }
    }
}
