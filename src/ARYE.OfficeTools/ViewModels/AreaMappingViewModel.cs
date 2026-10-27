using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using Autodesk.Revit.DB;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Zoning;
using ARYE.OfficeTools.Infrastructure;
using ARYE.OfficeTools.Services;
using Color = System.Windows.Media.Color;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// Step 2 — Sync Ranges. Every Typical / Duplex range is a mirror of its
    /// source level. The tab lists the building's ranges with a measured
    /// status, shows the selected range level by level against its source,
    /// and syncs with one button after an inline confirmation that names what
    /// will be deleted. Nothing changes in the model until that confirmation.
    /// </summary>
    internal sealed class AreaMappingViewModel : ViewModelBase
    {
        private readonly ShellViewModel _shell;
        private readonly RangeSyncService _sync = new RangeSyncService();

        private BuildingChoice _selectedBuilding;
        private RangeItemViewModel _selectedRange;
        private string _selectedBuildingParameter;
        private List<RangeItemViewModel> _pendingSync;
        private bool _loading;

        public AreaMappingViewModel(ShellViewModel shell)
        {
            _shell = shell;

            CheckCommand = new RelayCommand(CheckAll, () => !_shell.IsBusy && SelectedBuilding != null);
            SyncCommand = new RelayCommand(
                () => AskToSync(new List<RangeItemViewModel> { SelectedRange }),
                () => !_shell.IsBusy && SelectedRange?.CanSync == true);
            SyncAllCommand = new RelayCommand(
                () => AskToSync(OutOfDateRanges().ToList()),
                () => !_shell.IsBusy && OutOfDateRanges().Any());
            ConfirmSyncCommand = new RelayCommand(RunSync, () => !_shell.IsBusy && _pendingSync != null);
            CancelSyncCommand = new RelayCommand(() => SetPending(null));
        }

        public RelayCommand CheckCommand { get; }

        public RelayCommand SyncCommand { get; }

        public RelayCommand SyncAllCommand { get; }

        public RelayCommand ConfirmSyncCommand { get; }

        public RelayCommand CancelSyncCommand { get; }

        // ── Topbar ───────────────────────────────────────────────────────────

        public ObservableCollection<BuildingChoice> Buildings { get; } = new ObservableCollection<BuildingChoice>();

        /// <summary>One checkbox per area scheme of the model; the ticked ones hold the building's ZFA areas.</summary>
        public ObservableCollection<SchemeOptionViewModel> SchemeOptions { get; } =
            new ObservableCollection<SchemeOptionViewModel>();

        public BuildingChoice SelectedBuilding
        {
            get => _selectedBuilding;
            set
            {
                if (Set(ref _selectedBuilding, value))
                {
                    RebuildRanges();
                    RebuildSchemeOptions();
                    if (!_loading)
                    {
                        CheckAll();
                    }
                }
            }
        }

        /// <summary>Ticks the building's ZFA schemes (its own list, or the legacy fallback).</summary>
        private void RebuildSchemeOptions()
        {
            SchemeOptions.Clear();
            if (SelectedBuilding == null)
            {
                return;
            }
            var ticked = new HashSet<string>(ZfaSchemeResolver.Resolve(_shell.Model, SelectedBuilding.Definition));
            foreach (NamedElementRef scheme in _shell.Context.AreaSchemes)
            {
                SchemeOptions.Add(new SchemeOptionViewModel(scheme, ticked.Contains(scheme.UniqueId), OnSchemeToggled));
            }
        }

        /// <summary>
        /// Writes the ticked schemes into the building — in the shared model
        /// AND in its Step 1 card, so the next Step 1 write-back keeps them.
        /// Persisted by Save Configuration and by the save after every sync.
        /// </summary>
        private void OnSchemeToggled()
        {
            if (SelectedBuilding == null)
            {
                return;
            }
            List<string> ticked = SchemeOptions.Where(o => o.IsChecked).Select(o => o.UniqueId).ToList();
            SelectedBuilding.Definition.ZfaSchemeUniqueIds = ticked;
            BuildingItemViewModel card = _shell.Buildings.Buildings
                .FirstOrDefault(b => b.Id == SelectedBuilding.Definition.Id);
            if (card != null)
            {
                card.ZfaSchemeUniqueIds = new List<string>(ticked);
            }
            _shell.StatusText = "Area schemes changed — press Save Configuration to keep them (a sync saves them too).";
            CheckAll();
        }

        public string SyncAllText => $"Sync all out of date ({OutOfDateRanges().Count()})";

        // ── Ranges list ──────────────────────────────────────────────────────

        public ObservableCollection<RangeItemViewModel> Ranges { get; } = new ObservableCollection<RangeItemViewModel>();

        public RangeItemViewModel SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (value != null && !value.IsSelectable)
                {
                    return; // single floors are listed for completeness only
                }
                if (Set(ref _selectedRange, value))
                {
                    SetPending(null);
                    ClearResult();
                    RebuildDetail();
                }
            }
        }

        private IEnumerable<RangeItemViewModel> OutOfDateRanges() =>
            Ranges.Where(r => r.CanSync && r.Inspection?.Report != null
                              && r.Inspection.Report.State != RangeSyncState.InSync);

        private void RebuildRanges()
        {
            Guid? keep = _selectedRange?.Group.Id;
            Ranges.Clear();
            if (SelectedBuilding != null)
            {
                foreach (FloorGroup group in SelectedBuilding.Definition.Groups)
                {
                    Ranges.Add(new RangeItemViewModel(group));
                }
            }
            _selectedRange = null;
            SelectedRange = Ranges.FirstOrDefault(r => r.Group.Id == keep && r.IsSelectable)
                            ?? Ranges.FirstOrDefault(r => r.IsSelectable);
            Raise(nameof(SelectedRange));
            Raise(nameof(HasNoRanges));
            Raise(nameof(SyncAllText));
        }

        public bool HasNoRanges => SelectedBuilding != null && !Ranges.Any(r => r.IsSelectable);

        // ── Detail of the selected range ─────────────────────────────────────

        public bool HasDetail => SelectedRange != null;

        public string DetailTitle => SelectedRange?.Name ?? string.Empty;

        public string DetailWhy { get; private set; } = string.Empty;

        public string StatePillText => SelectedRange?.PillText ?? string.Empty;

        public Brush StatePillBackground => SelectedRange?.PillBackground;

        public Brush StatePillForeground => SelectedRange?.PillForeground;

        public string SyncButtonToolTip =>
            SelectedRange?.Inspection?.Report?.State == RangeSyncState.InSync
                ? "Already in sync. Syncing again rebuilds every copy: harmless, but not needed."
                : "Rebuild every copy of this range from the source. You confirm before anything is deleted.";

        public string LastSyncedText { get; private set; } = string.Empty;

        public string UnenclosedText { get; private set; } = string.Empty;

        public bool HasUnenclosed => !string.IsNullOrEmpty(UnenclosedText);

        public ObservableCollection<string> Notes { get; } = new ObservableCollection<string>();

        public ObservableCollection<SyncLevelRowViewModel> LevelRows { get; } =
            new ObservableCollection<SyncLevelRowViewModel>();

        public ObservableCollection<SourceAreaRowViewModel> SourceAreaRows { get; } =
            new ObservableCollection<SourceAreaRowViewModel>();

        private void RebuildDetail()
        {
            LevelRows.Clear();
            SourceAreaRows.Clear();
            Notes.Clear();
            RangeItemViewModel range = SelectedRange;
            RangeInspection inspection = range?.Inspection;

            DetailWhy = range == null ? string.Empty : range.WhyText;
            LastSyncedText = inspection?.LastRun == null
                ? "never synced"
                : $"last synced {inspection.LastRun.LastRunUtc.ToLocalTime():MMM d, yyyy · h:mm tt} by {inspection.LastRun.User}";
            UnenclosedText = string.Empty;

            if (inspection != null)
            {
                foreach (string note in inspection.Notes)
                {
                    Notes.Add(note);
                }
                if (inspection.Report != null)
                {
                    string scheme = null;
                    foreach (LevelSyncResult level in inspection.Report.Levels)
                    {
                        if (level.SchemeName != scheme)
                        {
                            scheme = level.SchemeName;
                            LevelRows.Add(SyncLevelRowViewModel.Header($"{scheme} · area plans"));
                        }
                        LevelRows.Add(new SyncLevelRowViewModel(level));
                    }
                    if (inspection.Report.UnenclosedSourceAreas.Count > 0)
                    {
                        UnenclosedText =
                            $"⚠ {inspection.Report.UnenclosedSourceAreas.Count} area(s) on the source report 0 sf because "
                            + "their boundary is not closed, so they are not copied: "
                            + string.Join(", ", inspection.Report.UnenclosedSourceAreas)
                            + ". Close the boundary on the source and sync again.";
                    }
                }
                bool duplex = SelectedRange.Group.Kind == FloorGroupKind.Duplex;
                foreach (string scheme in inspection.SchemeNames)
                {
                    inspection.SourceLineCounts.TryGetValue(scheme, out int lines);
                    List<SourceAreaInfo> areas = inspection.SourceAreas
                        .Where(a => a.SchemeName == scheme)
                        .OrderBy(a => a.LevelName).ThenByDescending(a => a.SquareFeet)
                        .ToList();
                    SourceAreaRows.Add(SourceAreaRowViewModel.Header(
                        $"{scheme} · {Plural(areas.Count, "area")} and {Plural(lines, "boundary line")}"));
                    foreach (SourceAreaInfo area in areas)
                    {
                        SourceAreaRows.Add(new SourceAreaRowViewModel(area, duplex));
                    }
                }
            }

            Raise(nameof(HasDetail));
            Raise(nameof(DetailTitle));
            Raise(nameof(DetailWhy));
            Raise(nameof(StatePillText));
            Raise(nameof(StatePillBackground));
            Raise(nameof(StatePillForeground));
            Raise(nameof(SyncButtonToolTip));
            Raise(nameof(LastSyncedText));
            Raise(nameof(UnenclosedText));
            Raise(nameof(HasUnenclosed));
        }

        // ── Confirmation panel ───────────────────────────────────────────────

        public bool IsConfirming => _pendingSync != null;

        public string ConfirmTitle { get; private set; } = string.Empty;

        public ObservableCollection<string> ConfirmLines { get; } = new ObservableCollection<string>();

        public string ConfirmDeleteText { get; private set; } = string.Empty;

        public string ConfirmButtonText { get; private set; } = string.Empty;

        private void AskToSync(List<RangeItemViewModel> ranges)
        {
            ClearResult();
            SetPending(ranges.Where(r => r.CanSync).ToList());
        }

        private void SetPending(List<RangeItemViewModel> ranges)
        {
            _pendingSync = ranges != null && ranges.Count > 0 ? ranges : null;
            ConfirmLines.Clear();
            ConfirmTitle = string.Empty;
            ConfirmDeleteText = string.Empty;
            ConfirmButtonText = string.Empty;

            if (_pendingSync != null)
            {
                if (_pendingSync.Count == 1)
                {
                    RangeItemViewModel range = _pendingSync[0];
                    RangeInspection inspection = range.Inspection;
                    List<string> copies = CopyLevels(inspection);
                    List<string> sources = inspection.Report.Levels
                        .Where(l => l.State == LevelSyncState.Source)
                        .Select(l => l.LevelName).Distinct().ToList();
                    double gross = inspection.Report.Levels
                        .Where(l => l.State == LevelSyncState.Source).Sum(l => l.GrossSquareFeet);
                    double deductions = inspection.Report.Levels
                        .Where(l => l.State == LevelSyncState.Source).Sum(l => l.DeductionSquareFeet);

                    ConfirmTitle = $"Sync {range.Name}?";
                    ConfirmLines.Add(
                        $"Source {string.Join(" / ", sources)}: "
                        + $"{Plural(inspection.SourceAreas.Count(a => a.SquareFeet > 0), "area")} "
                        + $"({gross:N2} sf gross, {deductions:N2} sf deductions) and "
                        + $"{Plural(inspection.SourceLineCount, "boundary line")}, on {string.Join(" and ", inspection.SchemeNames)}.");
                    ConfirmLines.Add(
                        $"On {Plural(copies.Count, "level")} ({copies.First()} – {copies.Last()}), delete every area "
                        + $"and boundary line of this building on the {string.Join(" and ", inspection.SchemeNames)} plans:");
                    ConfirmDeleteText = DescribeDeletions(inspection);
                    ConfirmLines.Add("Copy the source onto each of them, then measure every level against the source.");
                    ConfirmButtonText = $"Delete and sync {Plural(copies.Count, "level")}";
                }
                else
                {
                    ConfirmTitle = $"Sync {_pendingSync.Count} ranges?";
                    foreach (RangeItemViewModel range in _pendingSync)
                    {
                        ConfirmLines.Add($"{range.Name}: {Plural(CopyLevels(range.Inspection).Count, "level")} on "
                            + $"{string.Join(" and ", range.Inspection.SchemeNames)}, "
                            + DescribeDeletions(range.Inspection) + ".");
                    }
                    ConfirmDeleteText = "Each copy is rebuilt from its own source and measured afterwards.";
                    ConfirmButtonText = $"Delete and sync {_pendingSync.Count} ranges";
                }
            }

            Raise(nameof(IsConfirming));
            Raise(nameof(ConfirmTitle));
            Raise(nameof(ConfirmDeleteText));
            Raise(nameof(ConfirmButtonText));
        }

        /// <summary>Distinct copy levels, bottom-up as the report lists them — one level counts once across schemes.</summary>
        private static List<string> CopyLevels(RangeInspection inspection) =>
            inspection.Report.Levels
                .Where(l => l.State != LevelSyncState.Source)
                .Select(l => l.LevelName)
                .Distinct()
                .ToList();

        private static string DescribeDeletions(RangeInspection inspection)
        {
            string text = $"{Plural(inspection.EarlierCopiesToDelete, "earlier copied element")}";
            if (inspection.HandDrawnToDelete.Count > 0)
            {
                text += " and hand-drawn elements on "
                    + string.Join(", ", inspection.HandDrawnToDelete.Select(h =>
                        $"{h.LevelName} ({h.SchemeName}: {Plural(h.Areas, "area")}"
                        + $"{(h.Lines > 0 ? ", " + Plural(h.Lines, "line") : "")})"));
            }
            return text;
        }

        private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

        // ── Result banner ────────────────────────────────────────────────────

        public string ResultText { get; private set; } = string.Empty;

        public bool HasResult => !string.IsNullOrEmpty(ResultText);

        public bool ResultIsClean { get; private set; }

        private void ClearResult()
        {
            ResultText = string.Empty;
            Raise(nameof(ResultText));
            Raise(nameof(HasResult));
        }

        // ── Building parameter stamp ─────────────────────────────────────────

        public const string NoBuildingParameter = "(none)";

        public ObservableCollection<string> BuildingParameterOptions { get; } = new ObservableCollection<string>();

        public string SelectedBuildingParameter
        {
            get => _selectedBuildingParameter;
            set
            {
                if (Set(ref _selectedBuildingParameter, value))
                {
                    // Persisted by Save Configuration and by the save after every sync.
                    _shell.Model.Propagation.BuildingParameterName =
                        value == NoBuildingParameter ? null : value;
                }
            }
        }

        // ── Loading ──────────────────────────────────────────────────────────

        /// <summary>Called by the shell after every document refresh / save.</summary>
        public void LoadFrom(ZfaProjectModel model)
        {
            _loading = true;
            try
            {
                string keepBuilding = SelectedBuilding?.Definition.Id.ToString();
                Buildings.Clear();
                foreach (BuildingDefinition building in model.Buildings)
                {
                    Buildings.Add(new BuildingChoice(building));
                }
                _selectedBuilding = null;
                SelectedBuilding = Buildings.FirstOrDefault(b => b.Definition.Id.ToString() == keepBuilding)
                                   ?? Buildings.FirstOrDefault();

                BuildingParameterOptions.Clear();
                BuildingParameterOptions.Add(NoBuildingParameter);
                foreach (string name in _shell.Context.AreaTextParameters)
                {
                    BuildingParameterOptions.Add(name);
                }
                string stored = model.Propagation.BuildingParameterName;
                _selectedBuildingParameter = !string.IsNullOrEmpty(stored) && BuildingParameterOptions.Contains(stored)
                    ? stored
                    : NoBuildingParameter;
                Raise(nameof(SelectedBuildingParameter));
            }
            finally
            {
                _loading = false;
            }
            CheckAll();
        }

        // ── Check & sync ─────────────────────────────────────────────────────

        /// <summary>Measures every range of the selected building. Read-only; runs on tab open, after load and after each sync.</summary>
        public void CheckAll()
        {
            if (SelectedBuilding == null || _shell.IsBusy)
            {
                return;
            }
            BuildingDefinition building = SelectedBuilding.Definition;
            ZfaProjectModel model = _shell.Model;
            List<FloorGroup> groups = Ranges.Where(r => r.IsSelectable).Select(r => r.Group).ToList();

            _shell.RunRevit(
                "Check ranges",
                app =>
                {
                    Document doc = app.ActiveUIDocument.Document;
                    return groups.Select(g => _sync.Inspect(doc, model, building, g)).ToList();
                },
                result => ApplyInspections((List<RangeInspection>)result));
        }

        private void ApplyInspections(IEnumerable<RangeInspection> inspections)
        {
            foreach (RangeInspection inspection in inspections)
            {
                Ranges.FirstOrDefault(r => r.Group.Id == inspection.GroupId)?.Apply(inspection);
            }
            Raise(nameof(SyncAllText));
            RebuildDetail();
        }

        private void RunSync()
        {
            List<RangeItemViewModel> pending = _pendingSync;
            SetPending(null);
            BuildingDefinition building = SelectedBuilding.Definition;
            ZfaProjectModel model = _shell.Model;
            List<FloorGroup> groups = pending.Select(r => r.Group).ToList();

            _shell.RunRevit(
                groups.Count == 1 ? $"Sync {groups[0].Name}" : $"Sync {groups.Count} ranges",
                app => _sync.Sync(app.ActiveUIDocument.Document, model, building, groups, App.Log),
                result => ShowOutcomes((List<RangeSyncOutcome>)result));
        }

        private void ShowOutcomes(List<RangeSyncOutcome> outcomes)
        {
            ApplyInspections(outcomes.Where(o => o.After != null).Select(o => o.After));

            var lines = new List<string>();
            bool clean = true;
            foreach (RangeSyncOutcome outcome in outcomes)
            {
                RangeSyncReport after = outcome.After?.Report;
                if (!outcome.Committed || after == null)
                {
                    clean = false;
                    lines.Add($"✕ {outcome.GroupName}: not synced. "
                        + string.Join(" ", outcome.Result.Failed.Select(f => f.ToString())));
                    continue;
                }
                int matching = after.CopyCount - after.MismatchCount;
                bool ok = after.MismatchCount == 0 && outcome.Result.Failed.Count == 0;
                clean &= ok;
                lines.Add($"{(ok ? "✓" : "⚠")} Synced {outcome.GroupName}: {Plural(outcome.PlansRebuilt, "area plan")} rebuilt, "
                    + $"{Plural(outcome.AreasCreated, "area")} copied, {matching} of {after.CopyCount} copies match the source. "
                    + $"{Plural(outcome.Result.Failed.Count + outcome.Result.Skipped.Count, "problem")}.");
            }
            ResultIsClean = clean;
            ResultText = string.Join(Environment.NewLine, lines);
            Raise(nameof(ResultIsClean));
            Raise(nameof(ResultText));
            Raise(nameof(HasResult));

            List<RangeSyncOutcome> withProblems = outcomes.Where(o => o.Result.HasProblems).ToList();
            if (withProblems.Count > 0)
            {
                _shell.ShowResult("Sync ranges: problems",
                    string.Join(Environment.NewLine + Environment.NewLine,
                        withProblems.Select(o => o.GroupName + Environment.NewLine + o.Result.BuildSummary())));
            }
        }

        // ── Choice types ─────────────────────────────────────────────────────

        internal sealed class BuildingChoice
        {
            public BuildingChoice(BuildingDefinition definition) => Definition = definition;

            public BuildingDefinition Definition { get; }

            public string Name => Definition.Name;

            public override string ToString() => Name;
        }

    }

    /// <summary>One area scheme checkbox of the Step 2 topbar.</summary>
    internal sealed class SchemeOptionViewModel : ViewModelBase
    {
        private readonly Action _changed;
        private bool _isChecked;

        public SchemeOptionViewModel(NamedElementRef scheme, bool isChecked, Action changed)
        {
            Name = scheme.Name;
            UniqueId = scheme.UniqueId;
            _isChecked = isChecked;
            _changed = changed;
        }

        public string Name { get; }

        public string UniqueId { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (Set(ref _isChecked, value))
                {
                    _changed();
                }
            }
        }
    }

    /// <summary>Shared palette of the four range states.</summary>
    internal static class SyncPalette
    {
        public static readonly Brush OkFg = Frozen(0x04, 0x78, 0x57);
        public static readonly Brush OkBg = Frozen(0xD1, 0xFA, 0xE5);
        public static readonly Brush WarnFg = Frozen(0xB4, 0x53, 0x09);
        public static readonly Brush WarnBg = Frozen(0xFE, 0xF3, 0xC7);
        public static readonly Brush HandFg = Frozen(0x7C, 0x3A, 0xED);
        public static readonly Brush HandBg = Frozen(0xED, 0xE9, 0xFE);
        public static readonly Brush NewFg = Frozen(0x33, 0x41, 0x55);
        public static readonly Brush NewBg = Frozen(0xE2, 0xE8, 0xF0);
        public static readonly Brush BadFg = Frozen(0xDC, 0x26, 0x26);
        public static readonly Brush BadBg = Frozen(0xFE, 0xE2, 0xE2);
        public static readonly Brush SourceFg = Frozen(0x25, 0x63, 0xEB);
        public static readonly Brush SourceRow = Frozen(0xEF, 0xF6, 0xFF);
        public static readonly Brush Muted = Frozen(0xCB, 0xD5, 0xE1);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>One entry of the ranges list: a Step 1 range and its measured state.</summary>
    internal sealed class RangeItemViewModel : ViewModelBase
    {
        public RangeItemViewModel(FloorGroup group)
        {
            Group = group;
            IsSelectable = group.Kind == FloorGroupKind.Standard || group.Kind == FloorGroupKind.Duplex;
            if (!IsSelectable)
            {
                PillText = string.Empty;
                StripeBrush = SyncPalette.Muted;
            }
            else
            {
                SetState("… checking", SyncPalette.NewFg, SyncPalette.NewBg, SyncPalette.Muted,
                    "Measuring the copies against the source…");
            }
        }

        public FloorGroup Group { get; }

        /// <summary>Single floors are listed but never selected: they have nothing to copy.</summary>
        public bool IsSelectable { get; }

        public RangeInspection Inspection { get; private set; }

        public bool CanSync => IsSelectable && Inspection != null && Inspection.BlockReason == null
                               && Inspection.Report != null;

        public string Name => Group.Name;

        public string Subtitle
        {
            get
            {
                if (!IsSelectable)
                {
                    return "Single floor · drawn by hand";
                }
                string source = Group.Kind == FloorGroupKind.Duplex
                    ? $"{Group.SourceLevel?.Name} / {Group.SourceUpperLevel?.Name}"
                    : Group.SourceLevel?.Name;
                int copies = Group.Kind == FloorGroupKind.Duplex
                    ? Group.TargetPairs.Count * 2
                    : Group.TargetLevels.Count;
                return $"{(Group.Kind == FloorGroupKind.Duplex ? "Duplex" : "Typical")} · source {source} · {copies} copies";
            }
        }

        public string ToolTip => IsSelectable
            ? WhyText
            : "Single floors have nothing to copy. Draw their areas directly on their own area plan.";

        public string PillText { get; private set; }

        public bool HasPill => !string.IsNullOrEmpty(PillText);

        public Brush PillForeground { get; private set; }

        public Brush PillBackground { get; private set; }

        public Brush StripeBrush { get; private set; }

        public string WhyText { get; private set; } = string.Empty;

        public double Opacity => IsSelectable ? 1.0 : 0.55;

        public void Apply(RangeInspection inspection)
        {
            Inspection = inspection;
            if (inspection.BlockReason != null)
            {
                SetState("✕ Can't sync", SyncPalette.BadFg, SyncPalette.BadBg, SyncPalette.BadFg,
                    inspection.BlockReason);
                return;
            }

            RangeSyncReport report = inspection.Report;
            string when = inspection.LastRun == null
                ? string.Empty
                : $" ({inspection.LastRun.LastRunUtc.ToLocalTime():MMM d, yyyy · h:mm tt})";
            List<string> differing = report.Levels
                .Where(l => l.State == LevelSyncState.Differs || l.State == LevelSyncState.Empty)
                .Select(l => l.LevelName)
                .ToList();
            string levels = string.Join(", ", differing);

            switch (report.State)
            {
                case RangeSyncState.InSync:
                    SetState("✓ In sync", SyncPalette.OkFg, SyncPalette.OkBg, SyncPalette.OkFg,
                        "Every copy matches the source: same areas, same categories, same square footage.");
                    break;
                case RangeSyncState.SourceChanged:
                    SetState("⚠ Source changed", SyncPalette.WarnFg, SyncPalette.WarnBg, SyncPalette.WarnFg,
                        $"The source was edited after the last sync{when}. The copies still hold the old version.");
                    break;
                case RangeSyncState.CopyEdited:
                    SetState("✎ Edited on a copy", SyncPalette.HandFg, SyncPalette.HandBg, SyncPalette.HandFg,
                        $"A copy was changed by hand on {levels}. Copies are overwritten on every sync, "
                        + "so make that change on the source instead.");
                    break;
                case RangeSyncState.CopiesDiffer:
                    SetState("⚠ Copies differ", SyncPalette.WarnFg, SyncPalette.WarnBg, SyncPalette.WarnFg,
                        $"The copies on {levels} do not match the source. The last sync was recorded before "
                        + "change tracking existed, so the cause is unknown.");
                    break;
                default:
                    SetState("○ Never synced", SyncPalette.NewFg, SyncPalette.NewBg, SyncPalette.Muted,
                        "This range has never been synced. The copies do not match the source yet.");
                    break;
            }
        }

        private void SetState(string pill, Brush fg, Brush bg, Brush stripe, string why)
        {
            PillText = pill;
            PillForeground = fg;
            PillBackground = bg;
            StripeBrush = stripe;
            WhyText = why;
            Raise(nameof(PillText));
            Raise(nameof(HasPill));
            Raise(nameof(PillForeground));
            Raise(nameof(PillBackground));
            Raise(nameof(StripeBrush));
            Raise(nameof(WhyText));
            Raise(nameof(ToolTip));
        }
    }

    /// <summary>One row of the check table — a level, or the header that opens a scheme's block.</summary>
    internal sealed class SyncLevelRowViewModel
    {
        private SyncLevelRowViewModel(string header)
        {
            HeaderText = header.ToUpperInvariant();
            IsHeader = true;
        }

        public static SyncLevelRowViewModel Header(string text) => new SyncLevelRowViewModel(text);

        public bool IsHeader { get; }

        public bool IsRow => !IsHeader;

        public string HeaderText { get; }

        public SyncLevelRowViewModel(LevelSyncResult level)
        {
            LevelName = level.LevelName;
            IsSource = level.State == LevelSyncState.Source;
            AreaCount = level.AreaCount.ToString();
            Gross = level.GrossSquareFeet.ToString("N2");
            Deductions = level.DeductionSquareFeet.ToString("N2");
            Net = level.NetSquareFeet.ToString("N2");
            Detail = level.Detail;
            RowBackground = IsSource ? SyncPalette.SourceRow : Brushes.Transparent;

            switch (level.State)
            {
                case LevelSyncState.Source:
                    StatusText = "SOURCE";
                    StatusForeground = Brushes.White;
                    StatusBackground = SyncPalette.SourceFg;
                    Detail = "Copies are made from this level.";
                    break;
                case LevelSyncState.Matches:
                    StatusText = "✓ matches";
                    StatusForeground = SyncPalette.OkFg;
                    StatusBackground = SyncPalette.OkBg;
                    break;
                case LevelSyncState.Empty:
                    StatusText = "○ empty";
                    StatusForeground = SyncPalette.NewFg;
                    StatusBackground = SyncPalette.NewBg;
                    break;
                default:
                    StatusText = "⚠ differs";
                    StatusForeground = SyncPalette.WarnFg;
                    StatusBackground = SyncPalette.WarnBg;
                    break;
            }
        }

        public string LevelName { get; }

        public bool IsSource { get; }

        public string AreaCount { get; }

        public string Gross { get; }

        public string Deductions { get; }

        public string Net { get; }

        public string StatusText { get; }

        public Brush StatusForeground { get; }

        public Brush StatusBackground { get; }

        public Brush RowBackground { get; }

        public string Detail { get; }
    }

    /// <summary>One area of the "What gets copied" table, or the header that opens a scheme's block.</summary>
    internal sealed class SourceAreaRowViewModel
    {
        private SourceAreaRowViewModel(string header)
        {
            HeaderText = header.ToUpperInvariant();
            IsHeader = true;
        }

        public static SourceAreaRowViewModel Header(string text) => new SourceAreaRowViewModel(text);

        public bool IsHeader { get; }

        public bool IsRow => !IsHeader;

        public string HeaderText { get; }

        public SourceAreaRowViewModel(SourceAreaInfo area, bool showLevel)
        {
            Name = showLevel ? $"{area.Name} ({area.LevelName})" : area.Name;
            Category = string.IsNullOrWhiteSpace(area.Category) ? "Unclassified" : area.Category.Trim();
            SquareFeet = area.SquareFeet > 0 ? area.SquareFeet.ToString("N2") : "0 · not closed";
            IsUnclassified = string.IsNullOrWhiteSpace(area.Category);
            CategoryForeground = IsUnclassified ? SyncPalette.WarnFg : SyncPalette.NewFg;
        }

        public string Name { get; }

        public string Category { get; }

        public bool IsUnclassified { get; }

        public Brush CategoryForeground { get; }

        public string SquareFeet { get; }
    }
}
