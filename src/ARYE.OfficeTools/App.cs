using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using ARYE.OfficeTools.Core.Logging;

namespace ARYE.OfficeTools
{
    /// <summary>
    /// ARYE Office Tools: the BauTools buttons approved for the office, on the ARYE tab.
    /// Own file of this repo (never overwritten by build/promote.py): when a button is promoted
    /// or retired, update the ribbon here and in ARYE.tab/ (the pyRevit extension).
    /// Keep this thin — a throw here means Revit shows a startup error and disables the add-in.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        private const string TabName = "ARYE";

        // Created on first use, not in OnStartup: when the buttons come from the pyRevit extension,
        // Revit never runs this IExternalApplication, but the commands still log their errors.
        public static Log Log { get; } = new Log();

        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private string _assemblyPath;
        private string _assemblyDir;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                Log.Info($"ARYE Office Tools {Version} starting under Revit {application.ControlledApplication.VersionNumber}");
                Infrastructure.WpfHost.EnsureApplication();

                try { application.CreateRibbonTab(TabName); }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Another ARYE add-in already created the tab: share it.
                }

                _assemblyPath = Assembly.GetExecutingAssembly().Location;
                _assemblyDir = Path.GetDirectoryName(_assemblyPath);

                // Panels render left to right in creation order, in the order a project is approached.
                RibbonPanel site = application.CreateRibbonPanel(TabName, "Site");
                Add(site, "ARYE_NycLot", "NYC Lot\nBoundary", typeof(Commands.CommandNycLot), "icon_nyc_lot",
                    "Searches a NYC tax lot by address or BBL, shows every dimension source side by side " +
                    "(tax-map polygon, legal tax-map dimensions, assessment record) and draws the lot you " +
                    "choose in context: adjacent lots, block, sidewalk, 3D masses and street names.",
                    "The subject lot is drawn as Model Lines with the 'BauTools - Property Line' style and carries " +
                    "an interop record, so TopoGenerator's 'Align to Lot' can find it. Building heights come from " +
                    "NYC Building Footprints when a footprint matches the lot, otherwise an estimate labeled as such. " +
                    "Street names are proposed and must be confirmed before anything is drawn. Needs an internet connection.");

                RibbonPanel levels = application.CreateRibbonPanel(TabName, "Levels");
                Add(levels, "ARYE_GenerateLevels", "Generate\nLevels", typeof(Commands.GenerateLevelsCommand), "icon_levels",
                    "Batch-creates a run of evenly-spaced levels from a base elevation and a typical floor-to-floor height.",
                    "Set a count, a typical floor-to-floor height and a naming pattern. Levels that would collide with " +
                    "one already in the model, by name or by elevation, are skipped, never overwritten.");
                Add(levels, "ARYE_BubbleHeads", "Bubble\nHeads", typeof(Commands.CommandBubbleHeads), "icon_bubble",
                    "Shows or hides the End0/End1 bubble on every Grid and Level in the active view.",
                    "Quick presets (only left, only right, both, none) or per-end manual control. Applies only to datums " +
                    "visible in the active view; a datum that cannot take the change in this view is skipped and named " +
                    "in the result. Levels are only visible in elevations and sections.");

                RibbonPanel zoning = application.CreateRibbonPanel(TabName, "Zoning");
                Add(zoning, "ARYE_Zfa", "Zoning\nFloor Area", typeof(Commands.ZfaCommand), "icon_zfa",
                    "Multi-building ZFA workflow: buildings and typical floor ranges, Sync Ranges, and the NYC " +
                    "deductions matrix. The ZR calculation is not yet validated against an approved filing: check " +
                    "every number before using it in a filing.",
                    "Step 1 defines buildings, area schemes and floor ranges. Step 2 (Sync Ranges) keeps each typical " +
                    "range a mirror of its source level. Step 3 computes Zoning Floor Area with the office's deduction " +
                    "categories and exports the matrix. The setup is saved in the model.");
                Add(zoning, "ARYE_RegionAreaToParameter", "Region Area\n→ Param", typeof(Commands.CommandRegionAreaToParameter), "icon_region_area",
                    "Writes the computed area of one or many Filled Regions into a writable parameter (for example " +
                    "'Region Area' on the energy elevations), with a before/after preview that must be confirmed.",
                    "Scope: current selection, active view or the whole model, filtered by region type and owner view. " +
                    "The area is computed from the region boundaries and checked against Revit's own Area. Building and " +
                    "Side can be filled in the same run.");
                Add(zoning, "ARYE_RoomLightVent", "Light &\nVent", typeof(Commands.CommandRoomLightVent), "icon_light_vent",
                    "Adds up the light area and the vent area of each room's exterior windows and writes the two " +
                    "totals to the room, after a plan preview and a before/after table.",
                    "Uses the rooms visible in the active plan view, or only the rooms you selected first. A window " +
                    "counts for a room when exactly one of its sides has a room; window panels in curtain walls count " +
                    "too, skylights do not. Empty values are not added and every skipped window is listed with its reason.");

                RibbonPanel help = application.CreateRibbonPanel(TabName, "Help");
                Add(help, "ARYE_About", "About", typeof(Commands.AboutCommand), "icon_about",
                    $"ARYE Office Tools {Version}: version, the BauTools commit these buttons come from, and the log folder.",
                    null);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Startup failed", ex);
                TaskDialog.Show("ARYE Office Tools", "Failed to start:\n" + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private void Add(RibbonPanel panel, string id, string text, Type command, string icon, string tooltip, string longDescription)
        {
            var button = new PushButtonData(id, text, _assemblyPath, command.FullName) { ToolTip = tooltip };
            if (longDescription != null) button.LongDescription = longDescription + " Version " + Version + ".";
            string large = Path.Combine(_assemblyDir, "Resources", icon + "_32.png");
            string small = Path.Combine(_assemblyDir, "Resources", icon + "_16.png");
            if (File.Exists(large)) button.LargeImage = new BitmapImage(new Uri(large));
            if (File.Exists(small)) button.Image = new BitmapImage(new Uri(small));
            panel.AddItem(button);
        }
    }

    /// <summary>
    /// Minimal file log in %LOCALAPPDATA%\ARYE\OfficeTools\logs — one file per day. A failure to
    /// write the log is reported to the debugger and never stops the tool.
    /// </summary>
    public sealed class Log : ILog
    {
        private readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARYE", "OfficeTools", "logs");

        public string Folder => _dir;

        public void Info(string message) => Write("INFO ", message);

        public void Warn(string message) => Write("WARN ", message);

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
