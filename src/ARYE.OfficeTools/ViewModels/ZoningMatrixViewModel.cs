using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ARYE.OfficeTools.Core.Export;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Core.Zoning;
using ARYE.OfficeTools.Services;
using ARYE.OfficeTools.Storage;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// Step 3 — harvest, classify, compute the office-format ZFA matrix,
    /// export (CSV / formulated XLSX) and draw the drafting view. The matrix
    /// arithmetic lives in Core's OfficeMatrixCalculator; this class only
    /// shapes it for binding.
    /// </summary>
    internal sealed class ZoningMatrixViewModel : ViewModelBase
    {
        private static readonly Color UnderFarColor = Color.FromRgb(0x05, 0x96, 0x69);  // green
        private static readonly Color OverFarColor = Color.FromRgb(0xDC, 0x26, 0x26);   // red
        private static readonly Color BuildingBarColor = Color.FromRgb(0x25, 0x63, 0xEB); // blue
        private static readonly Color DeductionBarColor = Color.FromRgb(0xD9, 0x77, 0x06); // amber

        private readonly ShellViewModel _shell;
        private readonly AreaHarvestService _harvest = new AreaHarvestService();
        private readonly OfficeMatrixCalculator _calculator = new OfficeMatrixCalculator();
        private readonly ZfaStorageService _storage = new ZfaStorageService();

        private HarvestResult _lastHarvest;
        private Dictionary<string, MatrixSyncCell> _syncCells = new Dictionary<string, MatrixSyncCell>();
        private OfficeMatrix _matrix;
        private string _problemsText;

        public ZoningMatrixViewModel(ShellViewModel shell)
        {
            _shell = shell;
            ComputeCommand = new RelayCommand(Compute, () => !_shell.IsBusy);
            ApplyClassificationsCommand = new RelayCommand(
                ApplyClassifications,
                () => !_shell.IsBusy && Unclassified.Any(u => u.SelectedCategory != null));
            ExportXlsxCommand = new RelayCommand(
                () => Export(new XlsxMatrixExporter(), "Excel workbook (formulated)|*.xlsx"),
                () => _matrix != null && !_shell.IsBusy);
            ExportCsvCommand = new RelayCommand(
                () => Export(new CsvMatrixExporter(), "CSV (Excel-compatible)|*.csv"),
                () => _matrix != null && !_shell.IsBusy);
            DraftingViewCommand = new RelayCommand(
                UpdateDraftingView, () => _matrix != null && !_shell.IsBusy);
            EditCategoriesCommand = new RelayCommand(EditCategories, () => !_shell.IsBusy);
        }

        // ── Dialog hooks (wired by MainWindow so dialogs get a real owner) ────

        /// <summary>Shows the category editor modal; true when the user saved the list.</summary>
        public Func<CategoryEditorRequest, bool> ShowCategoryEditorHandler { get; set; }

        // ── Commands ──────────────────────────────────────────────────────────

        public RelayCommand ComputeCommand { get; }

        public RelayCommand ApplyClassificationsCommand { get; }

        public RelayCommand ExportXlsxCommand { get; }

        public RelayCommand ExportCsvCommand { get; }

        public RelayCommand DraftingViewCommand { get; }

        public RelayCommand EditCategoriesCommand { get; }

        // ── Zoning inputs (bind straight into the shared model) ───────────────

        public double LotAreaSqFt
        {
            get => _shell.Model.Zoning.LotAreaSqFt;
            set { _shell.Model.Zoning.LotAreaSqFt = value; Raise(); RecomputeLocal(); }
        }

        public double MaxFar
        {
            get => _shell.Model.Zoning.MaxFar;
            set { _shell.Model.Zoning.MaxFar = value; Raise(); RecomputeLocal(); }
        }

        public string District
        {
            get => _shell.Model.Zoning.District;
            set { _shell.Model.Zoning.District = value; Raise(); }
        }

        public bool EnergyEnabled
        {
            get => _shell.Model.Zoning.EnergyDeductionEnabled;
            set { _shell.Model.Zoning.EnergyDeductionEnabled = value; Raise(); RecomputeLocal(); }
        }

        public double EnergyPercent
        {
            get => _shell.Model.Zoning.EnergyDeductionPercent;
            set { _shell.Model.Zoning.EnergyDeductionPercent = value; Raise(); RecomputeLocal(); }
        }

        public void OnModelReloaded()
        {
            Raise(nameof(LotAreaSqFt));
            Raise(nameof(MaxFar));
            Raise(nameof(District));
            Raise(nameof(EnergyEnabled));
            Raise(nameof(EnergyPercent));
            Raise(nameof(LastComputedText));
        }

        // ── Matrix table ──────────────────────────────────────────────────────

        /// <summary>Raised after the column set changed — the view rebuilds the DataGrid columns.</summary>
        public event EventHandler MatrixColumnsChanged;

        public ObservableCollection<MatrixColumnViewModel> Columns { get; } =
            new ObservableCollection<MatrixColumnViewModel>();

        public ObservableCollection<OfficeMatrixRowViewModel> Rows { get; } =
            new ObservableCollection<OfficeMatrixRowViewModel>();

        public bool HasMatrix => _matrix != null;

        // ── Unclassified banner ───────────────────────────────────────────────

        public ObservableCollection<UnclassifiedAreaViewModel> Unclassified { get; } =
            new ObservableCollection<UnclassifiedAreaViewModel>();

        public bool HasUnclassified => Unclassified.Count > 0;

        public string UnclassifiedBannerText =>
            $"⚠ {Unclassified.Count} unclassified area(s) — totals are not final";

        // ── Problems ──────────────────────────────────────────────────────────

        public string ProblemsText
        {
            get => _problemsText;
            private set
            {
                if (Set(ref _problemsText, value))
                {
                    Raise(nameof(HasProblems));
                }
            }
        }

        public bool HasProblems => !string.IsNullOrEmpty(ProblemsText);

        // ── Right panel: FAR gauge & bars ─────────────────────────────────────

        public string BigZfaText { get; private set; } = "—";

        public string AllowedLineText { get; private set; } = "Compute the matrix to see totals.";

        public double FarFraction { get; private set; }

        public Brush GaugeBrush { get; private set; } = new SolidColorBrush(UnderFarColor);

        public string FarUsedText { get; private set; } = "—";

        public string RemainingText { get; private set; } = "—";

        public string MaxFarText { get; private set; } = "—";

        public bool HasFarChip { get; private set; }

        public string FarChipText { get; private set; } = "";

        public Brush FarChipBackground { get; private set; } = Brushes.Transparent;

        public Brush FarChipForeground { get; private set; } = Brushes.White;

        public ObservableCollection<BarItemViewModel> BuildingBars { get; } =
            new ObservableCollection<BarItemViewModel>();

        public ObservableCollection<BarItemViewModel> DeductionBars { get; } =
            new ObservableCollection<BarItemViewModel>();

        public string LastComputedText
        {
            get
            {
                MatrixSettings settings = _shell.Model.Matrix;
                if (settings.LastComputedUtc == null)
                {
                    return "🕒 Never computed for this model.";
                }
                DateTime local = settings.LastComputedUtc.Value.ToLocalTime();
                return $"🕒 Last computed: {local:yyyy-MM-dd HH:mm} · {settings.LastComputedBy}";
            }
        }

        // ── Compute ───────────────────────────────────────────────────────────

        private void Compute()
        {
            ZfaProjectModel model = _shell.Model;
            _shell.RunRevit(
                "Compute ZFA matrix",
                app =>
                {
                    var doc = app.ActiveUIDocument.Document;
                    using (var tx = new Autodesk.Revit.DB.Transaction(doc, "BauTools: Add ZFA parameters"))
                    {
                        tx.Start();
                        bool created = new ZfaParameterService().EnsureZfaCategoryParameter(doc);
                        if (created)
                        {
                            tx.Commit();
                        }
                        else
                        {
                            // Nothing changed; keep the undo history clean.
                            tx.RollBack();
                        }
                    }

                    HarvestResult harvest = _harvest.Harvest(doc, model);
                    Dictionary<string, MatrixSyncCell> syncCells = MeasureSync(doc, model);

                    // Stamp and persist immediately, so "Last computed" survives
                    // closing the file even without Save Configuration.
                    model.Matrix.LastComputedUtc = DateTime.UtcNow;
                    model.Matrix.LastComputedBy = Environment.UserName;
                    using (var tx = new Autodesk.Revit.DB.Transaction(doc, "BauTools: Record matrix computation"))
                    {
                        tx.Start();
                        _storage.Save(doc, model);
                        tx.Commit();
                    }
                    return Tuple.Create(harvest, syncCells);
                },
                result =>
                {
                    var computed = (Tuple<HarvestResult, Dictionary<string, MatrixSyncCell>>)result;
                    _lastHarvest = computed.Item1;
                    _syncCells = computed.Item2;
                    RecomputeFromHarvest();
                    Raise(nameof(LastComputedText));
                });
        }

        /// <summary>
        /// Step 2's check for every Typical / Duplex range of every building,
        /// keyed like the range dots (building + level), so each matrix row
        /// says whether its numbers come from an up-to-date copy.
        /// </summary>
        private static Dictionary<string, MatrixSyncCell> MeasureSync(
            Autodesk.Revit.DB.Document doc, ZfaProjectModel model)
        {
            var cells = new Dictionary<string, MatrixSyncCell>();
            var sync = new RangeSyncService();
            foreach (BuildingDefinition building in model.Buildings)
            {
                foreach (FloorGroup group in building.Groups.Where(g =>
                    g.Kind == FloorGroupKind.Standard || g.Kind == FloorGroupKind.Duplex))
                {
                    RangeInspection inspection = sync.Inspect(doc, model, building, group);
                    if (inspection.Report == null)
                    {
                        continue;
                    }
                    foreach (LevelSyncResult level in inspection.Report.Levels)
                    {
                        cells[RangeKey(building.Name, level.LevelUniqueId)] =
                            MatrixSyncCell.From(level, inspection.Report.State, group.Name);
                    }
                }
            }
            return cells;
        }

        /// <summary>Pure recompute (energy toggle, lot/FAR edits) — no Revit round-trip.</summary>
        private void RecomputeLocal()
        {
            if (_lastHarvest != null)
            {
                RecomputeFromHarvest();
            }
        }

        private void RecomputeFromHarvest()
        {
            ZfaProjectModel model = _shell.Model;
            var catalog = new CategoryCatalog(model.Categories);
            _matrix = _calculator.Calculate(_lastHarvest.Areas, catalog, model.Zoning);

            BuildColumns();
            BuildRows(model);
            BuildUnclassified(catalog);
            BuildRightPanel();

            ProblemsText = _lastHarvest.Problems.Count > 0
                ? string.Join(Environment.NewLine, _lastHarvest.Problems)
                : null;

            Raise(nameof(HasMatrix));
        }

        private void ClearMatrix()
        {
            _lastHarvest = null;
            _matrix = null;
            Rows.Clear();
            Columns.Clear();
            Unclassified.Clear();
            BuildingBars.Clear();
            DeductionBars.Clear();
            ProblemsText = null;
            Raise(nameof(HasMatrix));
            Raise(nameof(HasUnclassified));
            Raise(nameof(UnclassifiedBannerText));
            MatrixColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BuildColumns()
        {
            Columns.Clear();
            Columns.Add(MatrixColumnViewModel.LevelColumn(
                "LEVEL", "Level of the building. The color dot links the row to its Step 1 floor range."));
            Columns.Add(MatrixColumnViewModel.Number(
                "GROSS FLOOR AREA", 0,
                "Sum of the Main Gross role categories on the level. Unclassified areas are counted here (flagged) so nothing disappears."));
            Columns.Add(MatrixColumnViewModel.Number(
                "COMMERCIAL GROSS AREA", 1,
                "Sum of the Commercial Gross role categories — informational, never part of NET."));
            int cell = 2;
            foreach (string deduction in _matrix.DeductionColumns)
            {
                Columns.Add(MatrixColumnViewModel.Deduction(
                    deduction.ToUpperInvariant(), cell++,
                    $"Deduction category '{deduction}' — subtracted from gross to reach NET. Edit the list under Categories…"));
            }
            Columns.Add(MatrixColumnViewModel.Number(
                "NET FLOOR AREA", cell++,
                "GROSS FLOOR AREA minus every deduction column."));
            // The ENERGY cell keeps its slot in every row so cell indices stay
            // fixed, but the column is only shown while the toggle is on —
            // the same rule the XLSX export applies.
            int energyCell = cell++;
            if (_matrix.EnergyEnabled)
            {
                Columns.Add(MatrixColumnViewModel.Energy(
                    "ENERGY DEDUCTION", energyCell,
                    $"{FormatPercent(_matrix.EnergyPercent)}% of NET, taken off before ZFA. "
                    + "Untick 'Energy deduction' in the topbar to hide this column and take nothing off."));
            }
            Columns.Add(MatrixColumnViewModel.Number(
                "ZONING FLOOR AREA", cell,
                _matrix.EnergyEnabled
                    ? "NET minus ENERGY — the number checked against lot area × max FAR."
                    : "Equal to NET while the energy deduction is off — the number checked against lot area × max FAR."));
            Columns.Add(MatrixColumnViewModel.SyncColumn(
                "SYNC",
                "Step 2 check, measured when the matrix was computed: does this level still match the source of its "
                + "range? ⚠ or ✎ means its numbers come from an out-of-date copy — sync the range in Step 2. "
                + "Single floors are blank: they are drawn by hand."));
            MatrixColumnsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Numeric cells per row: GROSS, COMMERCIAL, one per deduction, NET, ENERGY, ZFA — whether or not ENERGY is shown.</summary>
        private int NumericCellCount => 2 + _matrix.DeductionColumns.Count + 3;

        private void BuildRows(ZfaProjectModel model)
        {
            Rows.Clear();
            int cellCount = NumericCellCount;
            Dictionary<string, Brush> rangeBrushes = BuildRangeBrushes(model);

            foreach (OfficeMatrixBuilding building in _matrix.Buildings)
            {
                Rows.Add(OfficeMatrixRowViewModel.BuildingHeader(building.BuildingName, cellCount));
                foreach (OfficeMatrixLine line in building.Levels)
                {
                    string key = RangeKey(building.BuildingName, line.LevelUniqueId);
                    rangeBrushes.TryGetValue(key, out Brush brush);
                    _syncCells.TryGetValue(key, out MatrixSyncCell sync);
                    Rows.Add(OfficeMatrixRowViewModel.LevelRow(
                        line, BuildCells(line, cellCount), brush, sync));
                }
                Rows.Add(OfficeMatrixRowViewModel.Totals(
                    "TOTALS", BuildCells(building.Totals, cellCount)));
            }
            if (_matrix.GrandTotal != null)
            {
                Rows.Add(OfficeMatrixRowViewModel.GrandTotal(
                    "TOTAL — ALL BUILDINGS", BuildCells(_matrix.GrandTotal, cellCount)));
            }
        }

        private string[] BuildCells(OfficeMatrixLine line, int cellCount)
        {
            var cells = new string[cellCount];
            int i = 0;
            cells[i++] = Format(line.MainGross);
            cells[i++] = Format(line.CommercialGross);
            foreach (string column in _matrix.DeductionColumns)
            {
                cells[i++] = line.Deductions.TryGetValue(column, out double deducted)
                    ? Format(deducted)
                    : "";
            }
            cells[i++] = Format(line.Net);
            cells[i++] = _matrix.EnergyEnabled ? Format(line.Energy) : "";
            cells[i] = Format(line.Zfa);
            return cells;
        }

        /// <summary>
        /// Level UniqueId → range color, per building: the same palette Step 1
        /// cycles by range position, so the dot matches the Step 1 card.
        /// </summary>
        private static Dictionary<string, Brush> BuildRangeBrushes(ZfaProjectModel model)
        {
            var map = new Dictionary<string, Brush>();
            foreach (BuildingDefinition building in model.Buildings)
            {
                for (int i = 0; i < building.Groups.Count; i++)
                {
                    FloorGroup group = building.Groups[i];
                    Color color = BuildingItemViewModel.GroupPalette[
                        i % BuildingItemViewModel.GroupPalette.Length];
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();

                    foreach (LevelRef level in MembersOf(group))
                    {
                        string key = RangeKey(building.Name, level.UniqueId);
                        if (!map.ContainsKey(key)) // first claim wins, like Step 1 ownership
                        {
                            map[key] = brush;
                        }
                    }
                }
            }
            return map;
        }

        private static IEnumerable<LevelRef> MembersOf(FloorGroup group)
        {
            switch (group.Kind)
            {
                case FloorGroupKind.Standard:
                    return FloorRangeMath.CollectStandardMembers(group);
                case FloorGroupKind.Duplex:
                    return FloorRangeMath.CollectDuplexMembers(group);
                default:
                    return group.SourceLevel != null
                        ? new[] { group.SourceLevel }
                        : Enumerable.Empty<LevelRef>();
            }
        }

        private static string RangeKey(string buildingName, string levelUniqueId) =>
            (buildingName ?? "") + "\n" + (levelUniqueId ?? "");

        private void BuildUnclassified(CategoryCatalog catalog)
        {
            Unclassified.Clear();
            List<string> names = catalog.All.Select(c => c.Name).ToList();
            foreach (UnclassifiedArea area in _lastHarvest.Unclassified)
            {
                Unclassified.Add(new UnclassifiedAreaViewModel(area, names));
            }
            Raise(nameof(HasUnclassified));
            Raise(nameof(UnclassifiedBannerText));
        }

        private void BuildRightPanel()
        {
            OfficeMatrix m = _matrix;

            BigZfaText = $"{m.TotalZfaSqFt:N0} sf ZFA";
            AllowedLineText = m.AllowedZfaSqFt > 0
                ? $"of {m.AllowedZfaSqFt:N0} sf allowed"
                : "no allowance set — enter lot area and max FAR";
            FarFraction = m.AllowedZfaSqFt > 0
                ? Math.Min(1.0, m.TotalZfaSqFt / m.AllowedZfaSqFt)
                : 0;
            GaugeBrush = new SolidColorBrush(m.IsOverFar ? OverFarColor : UnderFarColor);
            FarUsedText = m.LotAreaSqFt > 0 ? m.FarUsed.ToString("0.00") : "—";
            RemainingText = m.AllowedZfaSqFt > 0 ? $"{m.RemainingSqFt:N0} sf" : "—";
            MaxFarText = m.MaxFar > 0 ? m.MaxFar.ToString("0.00") : "—";

            HasFarChip = m.AllowedZfaSqFt > 0;
            if (m.IsOverFar)
            {
                FarChipText = $"OVER FAR by {-m.RemainingSqFt:N0} sf";
                FarChipBackground = new SolidColorBrush(OverFarColor);
            }
            else
            {
                FarChipText = $"✓ Under FAR — {m.RemainingSqFt:N0} sf remaining";
                FarChipBackground = new SolidColorBrush(UnderFarColor);
            }

            BuildingBars.Clear();
            // A harvest can legitimately come back empty (no area scheme assigned,
            // no areas yet): the panel must then show zeros, not throw on Max().
            double maxZfa = Math.Max(1e-9, m.Buildings.Count == 0
                ? 0
                : m.Buildings.Max(b => Math.Abs(b.Totals.Zfa)));
            foreach (OfficeMatrixBuilding building in m.Buildings)
            {
                BuildingBars.Add(new BarItemViewModel
                {
                    Label = building.BuildingName,
                    ValueText = $"{building.Totals.Zfa:N0} sf",
                    Fraction = Math.Abs(building.Totals.Zfa) / maxZfa,
                    Brush = new SolidColorBrush(BuildingBarColor)
                });
            }

            DeductionBars.Clear();
            var deductions = m.DeductionColumns
                .Select(name => new
                {
                    Name = name,
                    Total = m.DeductionTotals.TryGetValue(name, out double total) ? total : 0
                })
                .Where(d => d.Total > 0)
                .ToList();
            double maxDeduction = Math.Max(1e-9, deductions.Count == 0 ? 0 : deductions.Max(d => d.Total));
            foreach (var deduction in deductions)
            {
                DeductionBars.Add(new BarItemViewModel
                {
                    Label = deduction.Name,
                    ValueText = $"{deduction.Total:N0} sf",
                    Fraction = deduction.Total / maxDeduction,
                    Brush = new SolidColorBrush(DeductionBarColor)
                });
            }

            Raise(nameof(BigZfaText));
            Raise(nameof(AllowedLineText));
            Raise(nameof(FarFraction));
            Raise(nameof(GaugeBrush));
            Raise(nameof(FarUsedText));
            Raise(nameof(RemainingText));
            Raise(nameof(MaxFarText));
            Raise(nameof(HasFarChip));
            Raise(nameof(FarChipText));
            Raise(nameof(FarChipBackground));
        }

        private static string Format(double value) => value.ToString("N2");

        /// <summary>Nullable cells stay blank — the office sheet leaves them empty, not 0.00.</summary>
        private static string Format(double? value) => value.HasValue ? Format(value.Value) : "";

        private static string FormatPercent(double value) => value.ToString("0.##");

        // ── Actions ───────────────────────────────────────────────────────────

        private void ApplyClassifications()
        {
            List<(string, string, string)> assignments = Unclassified
                .Where(u => u.SelectedCategory != null)
                .Select(u => (u.Area.AreaUniqueId,
                              $"{u.Area.AreaName} on {u.Area.LevelName}",
                              u.SelectedCategory))
                .ToList();

            _shell.RunRevit(
                "Classify areas",
                app => _harvest.Classify(app.ActiveUIDocument.Document, assignments),
                result =>
                {
                    var operation = (OperationResult<string>)result;
                    if (operation.HasProblems)
                    {
                        _shell.ShowResult("Classify areas", operation.BuildSummary());
                    }
                    Compute();
                });
        }

        private void Export(IZfaMatrixExporter exporter, string filter)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter,
                FileName = $"ZFA Matrix {DateTime.Now:yyyy-MM-dd}{exporter.FileExtension}"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            exporter.Export(dialog.FileName, _matrix, _shell.Model.Zoning);
            _shell.StatusText = "Matrix exported to " + dialog.FileName;
        }

        private void UpdateDraftingView()
        {
            OfficeMatrix matrix = _matrix;
            ZfaProjectModel model = _shell.Model;
            _shell.RunRevit(
                "Update ZFA drafting view",
                app => new MatrixDraftingViewService()
                    .CreateOrUpdate(app.ActiveUIDocument.Document, matrix, model),
                result => _shell.ShowResult("ZFA Matrix — drafting view", (string)result));
        }

        private void EditCategories()
        {
            if (ShowCategoryEditorHandler == null)
            {
                return;
            }
            var request = new CategoryEditorRequest
            {
                Categories = _shell.Model.Categories
                    .Select(c => new CategoryDefinition(c.Name, c.Role))
                    .ToList()
            };
            if (!ShowCategoryEditorHandler(request))
            {
                return;
            }

            _shell.Model.Categories = request.Categories;
            // Matching changed — yesterday's harvest no longer applies.
            ClearMatrix();
            _shell.StatusText = "Categories updated — press Compute Matrix to re-harvest.";
            // Persist through the normal save flow; its completion also
            // rebuilds the Step 2 filter from the new list.
            _shell.Save();
        }
    }

    /// <summary>Input/output bag for the category editor dialog.</summary>
    internal sealed class CategoryEditorRequest
    {
        public List<CategoryDefinition> Categories { get; set; }
    }

    /// <summary>One DataGrid column of the matrix, built in code (dynamic deductions).</summary>
    internal sealed class MatrixColumnViewModel
    {
        public string Header { get; private set; }

        public string ToolTip { get; private set; }

        /// <summary>Index into the row's Cells array; -1 for the LEVEL column.</summary>
        public int CellIndex { get; private set; } = -1;

        public bool IsLevel { get; private set; }

        public bool IsEnergy { get; private set; }

        /// <summary>True for the dynamic deduction-category columns — the ones the DEDUCTIONS band spans.</summary>
        public bool IsDeduction { get; private set; }

        public static MatrixColumnViewModel LevelColumn(string header, string toolTip) =>
            new MatrixColumnViewModel { Header = header, ToolTip = toolTip, IsLevel = true };

        public static MatrixColumnViewModel Number(string header, int cellIndex, string toolTip) =>
            new MatrixColumnViewModel { Header = header, ToolTip = toolTip, CellIndex = cellIndex };

        public static MatrixColumnViewModel Deduction(string header, int cellIndex, string toolTip) =>
            new MatrixColumnViewModel
            { Header = header, ToolTip = toolTip, CellIndex = cellIndex, IsDeduction = true };

        public static MatrixColumnViewModel Energy(string header, int cellIndex, string toolTip) =>
            new MatrixColumnViewModel
            { Header = header, ToolTip = toolTip, CellIndex = cellIndex, IsEnergy = true };

        /// <summary>True for the SYNC status column — a chip, not a number.</summary>
        public bool IsSync { get; private set; }

        public static MatrixColumnViewModel SyncColumn(string header, string toolTip) =>
            new MatrixColumnViewModel { Header = header, ToolTip = toolTip, IsSync = true };
    }

    /// <summary>One level's Step 2 status as the matrix SYNC column shows it.</summary>
    internal sealed class MatrixSyncCell
    {
        public string Text { get; private set; }

        public Brush Foreground { get; private set; }

        public Brush Background { get; private set; }

        public string ToolTip { get; private set; }

        public static MatrixSyncCell From(LevelSyncResult level, RangeSyncState rangeState, string rangeName)
        {
            switch (level.State)
            {
                case LevelSyncState.Source:
                    return new MatrixSyncCell
                    {
                        Text = "SOURCE",
                        Foreground = Brushes.White,
                        Background = SyncPalette.SourceFg,
                        ToolTip = $"Source of {rangeName}: its copies are made from this level."
                    };
                case LevelSyncState.Matches:
                    return new MatrixSyncCell
                    {
                        Text = "✓",
                        Foreground = SyncPalette.OkFg,
                        Background = SyncPalette.OkBg,
                        ToolTip = $"Matches the source of {rangeName}."
                    };
                case LevelSyncState.Empty:
                    return new MatrixSyncCell
                    {
                        Text = "○ not synced",
                        Foreground = SyncPalette.NewFg,
                        Background = SyncPalette.NewBg,
                        ToolTip = $"{level.Detail}. Sync {rangeName} in Step 2."
                    };
            }

            bool edited = rangeState == RangeSyncState.CopyEdited;
            return new MatrixSyncCell
            {
                Text = edited ? "✎ edited" : rangeState == RangeSyncState.SourceChanged ? "⚠ source changed" : "⚠ differs",
                Foreground = edited ? SyncPalette.HandFg : SyncPalette.WarnFg,
                Background = edited ? SyncPalette.HandBg : SyncPalette.WarnBg,
                ToolTip = $"{level.Detail}. Sync {rangeName} in Step 2."
            };
        }
    }

    /// <summary>One row of the matrix table: a level, a building header, TOTALS or the grand total.</summary>
    internal sealed class OfficeMatrixRowViewModel
    {
        private static readonly Brush HeaderBackground = Frozen(Color.FromRgb(0xF1, 0xF5, 0xF9));
        private static readonly Brush TotalsBackground = Frozen(Color.FromRgb(0xE2, 0xE8, 0xF0));
        private static readonly Brush GrandBackground = Frozen(Color.FromRgb(0xDB, 0xEA, 0xFE));

        public string Level { get; private set; }

        /// <summary>Formatted numeric cells, aligned with the column CellIndex order. Blank = empty cell.</summary>
        public string[] Cells { get; private set; }

        public Brush RangeBrush { get; private set; } = Brushes.Transparent;

        public bool HasRangeDot { get; private set; }

        public Brush RowBackground { get; private set; } = Brushes.Transparent;

        public FontWeight RowFontWeight { get; private set; } = FontWeights.Normal;

        public string RowToolTip { get; private set; }

        public static OfficeMatrixRowViewModel BuildingHeader(string name, int cellCount) =>
            new OfficeMatrixRowViewModel
            {
                Level = name,
                Cells = Blank(cellCount),
                RowBackground = HeaderBackground,
                RowFontWeight = FontWeights.SemiBold,
                RowToolTip = $"Building '{name}' — its levels follow, closed by a TOTALS row."
            };

        /// <summary>Step 2 status of a level row; null on headers, totals and single floors.</summary>
        public MatrixSyncCell Sync { get; private set; }

        public bool HasSync => Sync != null;

        public static OfficeMatrixRowViewModel LevelRow(OfficeMatrixLine line, string[] cells, Brush rangeBrush,
            MatrixSyncCell sync) =>
            new OfficeMatrixRowViewModel
            {
                Level = line.LevelName,
                Cells = cells,
                Sync = sync,
                RangeBrush = rangeBrush ?? Brushes.Transparent,
                HasRangeDot = rangeBrush != null,
                RowToolTip = line.UnclassifiedCount > 0
                    ? $"{line.UnclassifiedCount} unclassified area(s) on this level — its numbers are not final."
                    : rangeBrush != null
                        ? "The dot color matches this level's Step 1 floor range."
                        : "This level belongs to no Step 1 floor range."
            };

        public static OfficeMatrixRowViewModel Totals(string label, string[] cells) =>
            new OfficeMatrixRowViewModel
            {
                Level = label,
                Cells = cells,
                RowBackground = TotalsBackground,
                RowFontWeight = FontWeights.SemiBold,
                RowToolTip = "Per-column sums of the building's level rows."
            };

        public static OfficeMatrixRowViewModel GrandTotal(string label, string[] cells) =>
            new OfficeMatrixRowViewModel
            {
                Level = label,
                Cells = cells,
                RowBackground = GrandBackground,
                RowFontWeight = FontWeights.Bold,
                RowToolTip = "Sum across all buildings — the ZFA checked against the FAR."
            };

        private static string[] Blank(int count)
        {
            var cells = new string[count];
            for (int i = 0; i < count; i++)
            {
                cells[i] = "";
            }
            return cells;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>One unclassified area line in the amber banner.</summary>
    internal sealed class UnclassifiedAreaViewModel : ViewModelBase
    {
        private string _selectedCategory;

        public UnclassifiedAreaViewModel(UnclassifiedArea area, List<string> categoryNames)
        {
            Area = area;
            Categories = categoryNames;
        }

        public UnclassifiedArea Area { get; }

        public string Building => Area.BuildingName;

        public string Level => Area.LevelName;

        public string Name => Area.AreaName;

        public double SquareFeet => Area.SquareFeet;

        /// <summary>The project's configurable category names.</summary>
        public List<string> Categories { get; }

        /// <summary>Null until the user picks — only picked rows are written back.</summary>
        public string SelectedCategory
        {
            get => _selectedCategory;
            set => Set(ref _selectedCategory, value);
        }
    }

    /// <summary>One horizontal bar of the right panel ("ZFA by building" / "Deductions by category").</summary>
    internal sealed class BarItemViewModel
    {
        public string Label { get; set; }

        public string ValueText { get; set; }

        /// <summary>0..1 relative to the longest bar of its group.</summary>
        public double Fraction { get; set; }

        public Brush Brush { get; set; }
    }
}
