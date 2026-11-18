using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Core.Results;
using ARYE.OfficeTools.Core.Sheets;
using ARYE.OfficeTools.Infrastructure;
using ARYE.OfficeTools.Services;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// Sheet Diagrammer — sheet sets. The left column shows the building's
    /// typical floors (read-only, from the ZFA tool) and the project's sets;
    /// the middle card edits the selected set; the right panel shows what a
    /// run against the selected building would do: the NEW / UPDATE / KEPT
    /// views, the additional views, and a mini drawing of every sheet.
    ///
    /// The preview is live — every structural edit re-reads the model through
    /// the host's Revit dispatcher — and it is what enables "Generate and
    /// Place". A set the overlap guard flags never runs.
    /// </summary>
    internal sealed class SheetLayoutViewModel : ViewModelBase
    {
        private const double MiniSheetWidth = 176;

        private static readonly Brush NewBrush = Freeze("#059669");
        private static readonly Brush UpdateBrush = Freeze("#0891B2");
        private static readonly Brush KeptBrush = Freeze("#64748B");
        private static readonly Brush AdditionalBrush = Freeze("#7C3AED");
        private static readonly Brush NewSlotFill = Freeze("#1F059669");
        private static readonly Brush FilledSlotFill = Freeze("#FFFFFF");
        private static readonly Brush EmptySlotFill = Freeze("#F1F5F9");
        private static readonly Brush SlotBorderBrush = Freeze("#E2E8F0");
        private static readonly Brush StripBrush = Freeze("#E2E8F0");
        private static readonly Brush TextBrushDark = Freeze("#0F172A");

        private readonly IRevitShell _shell;
        private readonly SheetPackageService _service = new SheetPackageService();

        private ZfaProjectModel _model = new ZfaProjectModel();
        private RevitContext _context = new RevitContext();
        private SheetPackageItemViewModel _selectedPackage;
        private AreaMappingViewModel.BuildingChoice _selectedBuilding;
        private SheetPackagePreview _preview;
        private string _overlapReason;
        private string _previewError;
        private bool _loading;
        private bool _previewInFlight;
        private bool _previewQueued;

        public SheetLayoutViewModel(IRevitShell shell)
        {
            _shell = shell;
            AddPackageCommand = new RelayCommand(AddPackage, () => !_shell.IsBusy);
            DuplicatePackageCommand = new RelayCommand(DuplicatePackage, () => SelectedPackage != null && !_shell.IsBusy);
            RemovePackageCommand = new RelayCommand(RemovePackage, () => SelectedPackage != null && !_shell.IsBusy);
            PreviewCommand = new RelayCommand(RunPreview, CanPreview);
            GenerateCommand = new RelayCommand(Generate, CanGenerate);
            AddAdditionalViewsCommand = new RelayCommand(AddAdditionalViews, () => SelectedPackage != null && !_shell.IsBusy);
            RemoveAdditionalViewCommand = new RelayCommand(
                p => RemoveAdditionalView(p as AdditionalViewItemViewModel), _ => SelectedPackage != null);
            GoToIssueCommand = new RelayCommand(p => GoToIssue(p as SettingIssue));
        }

        // ── Collections ──────────────────────────────────────────────────────

        public ObservableCollection<SheetPackageItemViewModel> Packages { get; } = new ObservableCollection<SheetPackageItemViewModel>();

        public ObservableCollection<AreaMappingViewModel.BuildingChoice> Buildings { get; } = new ObservableCollection<AreaMappingViewModel.BuildingChoice>();

        public ObservableCollection<FloorRangeRowViewModel> TypicalFloorRows { get; } = new ObservableCollection<FloorRangeRowViewModel>();

        public ObservableCollection<SheetPreviewRowViewModel> ViewRows { get; } = new ObservableCollection<SheetPreviewRowViewModel>();

        public ObservableCollection<SheetContentRowViewModel> ContentRows { get; } = new ObservableCollection<SheetContentRowViewModel>();

        public bool HasContentCheck => _preview != null && !_preview.IsBlocked && _preview.ContentRows.Count > 0;

        public bool ContentCheckPasses => _preview != null && _preview.ContentUnexpectedCount == 0;

        public string ContentVerdictText
        {
            get
            {
                if (_preview == null || _preview.ContentRows.Count == 0) return string.Empty;
                int expected = _preview.ContentExpectedCount, unexpected = _preview.ContentUnexpectedCount;
                return unexpected == 0
                    ? $"✓ Only these {expected} view{(expected == 1 ? "" : "s")} will be on this set's sheets."
                    : $"⚠ {unexpected} view{(unexpected == 1 ? " is" : "s are")} on this set's sheets without being in the expected {expected}. "
                      + "They are never removed or moved by a run.";
            }
        }

        public ObservableCollection<MiniSheetViewModel> MiniSheets { get; } = new ObservableCollection<MiniSheetViewModel>();

        public ObservableCollection<string> Notes { get; } = new ObservableCollection<string>();

        public RelayCommand AddPackageCommand { get; }

        public RelayCommand DuplicatePackageCommand { get; }

        public RelayCommand RemovePackageCommand { get; }

        public RelayCommand PreviewCommand { get; }

        public RelayCommand GenerateCommand { get; }

        public RelayCommand AddAdditionalViewsCommand { get; }

        public RelayCommand RemoveAdditionalViewCommand { get; }

        public RelayCommand GoToIssueCommand { get; }

        /// <summary>Raised with a setting key when the user asks to jump to an empty setting; the view scrolls to it and focuses it.</summary>
        public event Action<string> FocusRequested;

        private void GoToIssue(SettingIssue issue)
        {
            if (issue == null || SelectedPackage == null) return;
            SelectedPackage.OpenStep(issue.Step);
            FocusRequested?.Invoke(issue.Key);
        }

        /// <summary>
        /// Set by the window: opens the view picker with every placeable view
        /// and the UniqueIds already in the set; returns the picked views with
        /// their own-sheet flag, or null when cancelled.
        /// </summary>
        public Func<IList<PlaceableViewRef>, ISet<string>, IList<(PlaceableViewRef View, bool OwnSheet)>> PickViewsHandler { get; set; }

        // ── Selection ────────────────────────────────────────────────────────

        public SheetPackageItemViewModel SelectedPackage
        {
            get => _selectedPackage;
            set
            {
                if (Set(ref _selectedPackage, value))
                {
                    Raise(nameof(HasPackage));
                    SyncBuildingScopeBox();
                    RaiseResult();
                    RefreshOverlap();
                    RequestTitleBlockImage();
                    RequestScopeBoxPreview();
                    RequestPreview();
                }
            }
        }

        public AreaMappingViewModel.BuildingChoice SelectedBuilding
        {
            get => _selectedBuilding;
            set
            {
                if (Set(ref _selectedBuilding, value))
                {
                    RebuildTypicalFloorRows();
                    SyncBuildingScopeBox();
                    Raise(nameof(BuildingHasFloorRanges));
                    Raise(nameof(BuildingCaption));
                    Raise(nameof(GenerateTooltip));
                    RaiseResult();
                    RequestPreview();
                }
            }
        }

        public bool HasPackage => SelectedPackage != null;

        public bool HasBuildings => Buildings.Count > 0;

        public bool BuildingHasFloorRanges =>
            SelectedBuilding != null && SheetPackageService.CollectFloorRangeLevelIds(SelectedBuilding.Definition).Count > 0;

        public string BuildingCaption
        {
            get
            {
                if (SelectedBuilding == null)
                {
                    return Buildings.Count == 0
                        ? "No buildings yet — define them in Zoning Floor Area › Step 1."
                        : "Select a building.";
                }
                int levels = SheetPackageService.CollectFloorRangeLevelIds(SelectedBuilding.Definition).Count;
                return levels == 0
                    ? "This building has no Floor Ranges — nothing can be drawn until they are set in Zoning Floor Area › Step 1."
                    : $"{levels} level(s) in floor ranges · read-only here.";
            }
        }

        // ── Result panel: verdict, numbers, what is missing ──────────────────

        public ObservableCollection<SheetContentRowViewModel> ExpectedRows { get; } = new ObservableCollection<SheetContentRowViewModel>();

        public ObservableCollection<SheetContentRowViewModel> UnexpectedRows { get; } = new ObservableCollection<SheetContentRowViewModel>();

        public ObservableCollection<TableRowViewModel> TableRows { get; } = new ObservableCollection<TableRowViewModel>();

        public bool HasTables => TableRows.Count > 0;

        public string TablesCountText => TableRows.Count.ToString(CultureInfo.InvariantCulture);

        public ObservableCollection<PlannedViewChip> PlannedViews { get; } = new ObservableCollection<PlannedViewChip>();

        public string PlannedViewsCaption => PlannedViews.Count == 0 ? "No views yet" : $"These {PlannedViews.Count} view{(PlannedViews.Count == 1 ? "" : "s")} will exist";

        public IReadOnlyList<SettingIssue> Issues => SelectedPackage?.Issues ?? (IReadOnlyList<SettingIssue>)Array.Empty<SettingIssue>();

        public bool HasIssues => Issues.Count > 0;

        public bool HasRequiredIssues => SelectedPackage != null && SelectedPackage.HasRequired;

        private bool Blocked => HasRequiredIssues || HasConflict;

        /// <summary>"Ok", "Warn", "Bad" or "Neutral" — drives the banner colors.</summary>
        public string VerdictKind
        {
            get
            {
                if (SelectedPackage == null) return "Neutral";
                if (Blocked) return "Bad";
                if (_preview == null) return "Neutral";
                return _preview.ContentUnexpectedCount > 0 ? "Warn" : "Ok";
            }
        }

        public string VerdictTitle
        {
            get
            {
                if (SelectedPackage == null) return "No set selected";
                if (Blocked) return "Cannot generate yet";
                if (_preview == null) return "Preview not run yet";
                int expected = _preview.ContentExpectedCount, unexpected = _preview.ContentUnexpectedCount;
                return unexpected > 0
                    ? $"{unexpected} older view{(unexpected == 1 ? " is" : "s are")} still on these sheets"
                    : $"Only these {expected} view{(expected == 1 ? "" : "s")} will be on this set's sheets";
            }
        }

        public string VerdictDetail
        {
            get
            {
                if (SelectedPackage == null) return "Select or add a set on the left.";
                if (Blocked)
                {
                    var parts = new List<string>();
                    if (HasRequiredIssues)
                    {
                        List<SettingIssue> req = Issues.Where(i => i.IsRequired).ToList();
                        parts.Add($"{req.Count} required setting{(req.Count == 1 ? " is" : "s are")} empty: {string.Join(", ", req.Select(i => i.Label))}.");
                    }
                    if (HasConflict) parts.Add(ConflictText);
                    return string.Join(" ", parts);
                }
                if (_preview == null) return "Press Preview to see what this run would do.";
                return _preview.ContentUnexpectedCount > 0
                    ? $"This run will place the {_preview.ContentExpectedCount} views below. Views a run built earlier for levels now covered by a range stay where they are until you remove them."
                    : $"{_preview.SheetCount} sheet{(_preview.SheetCount == 1 ? "" : "s")}. Nothing outside the list.";
            }
        }

        public bool KpiDim => _preview == null || Blocked;

        public double KpiOpacity => KpiDim ? 0.4 : 1.0;

        public string KpiViews => KpiDim ? "—" : (_preview.ContentExpectedCount > 0 ? _preview.ContentExpectedCount : _preview.ViewCount).ToString(CultureInfo.InvariantCulture);

        public string KpiSheets => KpiDim ? "—" : _preview.SheetCount.ToString(CultureInfo.InvariantCulture);

        public string KpiScale => KpiDim || _preview.ScaleDenominator <= 0 ? "—" : "1/" + _preview.ScaleDenominator.ToString(CultureInfo.InvariantCulture);

        public string KpiScaleCaption => _preview == null ? "scale" : _preview.ScaleIsOverride ? "scale, fixed by you"
            : _preview.ScaleIsFromTemplate ? "scale, from the template" : "scale, fits the smallest cell";

        public string KpiCells => SelectedPackage == null ? "—" : $"{SelectedPackage.Columns}×{SelectedPackage.Rows}";

        public string KpiCollection => SelectedPackage == null ? string.Empty : (SelectedPackage.Package.SheetCollectionName ?? string.Empty).Trim();

        public bool HasKpiCollection => KpiCollection.Length > 0;

        public string NewCount => _preview == null ? "0" : _preview.NewViewCount.ToString(CultureInfo.InvariantCulture);

        public string UpdateCount => _preview == null ? "0" : _preview.UpdateViewCount.ToString(CultureInfo.InvariantCulture);

        public string KeptCount => _preview == null ? "0" : _preview.KeptViewCount.ToString(CultureInfo.InvariantCulture);

        public string PlaceCount => _preview == null ? "0" : _preview.AdditionalToPlaceCount.ToString(CultureInfo.InvariantCulture);

        public string ExpectedCountText => ExpectedRows.Count.ToString(CultureInfo.InvariantCulture);

        public string UnexpectedCountText => UnexpectedRows.Count.ToString(CultureInfo.InvariantCulture);

        public bool HasUnexpected => UnexpectedRows.Count > 0;

        public bool HasNotes => Notes.Count > 0;

        public string NotesCountText => Notes.Count.ToString(CultureInfo.InvariantCulture);

        public string ViewsCountText => ViewRows.Count.ToString(CultureInfo.InvariantCulture);

        public string FooterNote => SelectedPackage == null ? string.Empty
            : HasRequiredIssues ? "Fill the required settings to enable Generate"
            : _preview == null ? "Preview not run yet"
            : KpiViews + " views · " + KpiSheets + " sheets";

        // ── Unsaved changes ──

        private bool _dirty;

        public bool IsDirty
        {
            get => _dirty;
            private set { if (Set(ref _dirty, value)) Raise(nameof(DirtyCaption)); }
        }

        public string DirtyCaption => _dirty ? "Unsaved changes" : "All changes saved";

        /// <summary>Called by the host once the sets are written to the model.</summary>
        public void MarkSaved() => IsDirty = false;

        private void SyncBuildingScopeBox()
        {
            bool has = !string.IsNullOrEmpty(SelectedBuilding?.Definition?.ScopeBoxUniqueId);
            string buildingName = SelectedBuilding?.Definition?.Name ?? string.Empty;
            foreach (SheetPackageItemViewModel p in Packages)
            {
                p.BuildingHasScopeBox = has;
                p.BuildingName = buildingName;
            }
        }

        private void RaiseResult()
        {
            foreach (string name in new[]
            {
                nameof(Issues), nameof(HasIssues), nameof(HasRequiredIssues), nameof(VerdictKind), nameof(VerdictTitle), nameof(VerdictDetail),
                nameof(KpiDim), nameof(KpiOpacity), nameof(KpiViews), nameof(KpiSheets), nameof(KpiScale), nameof(KpiScaleCaption), nameof(KpiCells),
                nameof(KpiCollection), nameof(HasKpiCollection), nameof(NewCount), nameof(UpdateCount), nameof(KeptCount), nameof(PlaceCount),
                nameof(ExpectedCountText), nameof(UnexpectedCountText), nameof(HasUnexpected), nameof(HasNotes), nameof(NotesCountText),
                nameof(ViewsCountText), nameof(HasTables), nameof(TablesCountText), nameof(FooterNote), nameof(PlannedViewsCaption), nameof(GenerateTooltip)
            })
            {
                Raise(name);
            }
        }

        // ── Preview surface ──────────────────────────────────────────────────

        public bool HasPreview => _preview != null && !_preview.IsBlocked && _overlapReason == null;

        public string SheetChipText => _preview == null ? "— sheets" : $"{_preview.SheetCount} sheet{(_preview.SheetCount == 1 ? "" : "s")}";

        public string ScaleChipText
        {
            get
            {
                if (_preview == null || _preview.ScaleDenominator <= 0) return "scale —";
                return _preview.ScaleIsOverride ? $"scale 1/{_preview.ScaleDenominator} (manual)"
                    : _preview.ScaleIsFromTemplate ? $"scale 1/{_preview.ScaleDenominator} (template)"
                    : $"scale 1/{_preview.ScaleDenominator}";
            }
        }

        public string ViewChipText => _preview == null
            ? "— views"
            : $"{_preview.ViewCount} view{(_preview.ViewCount == 1 ? "" : "s")}" +
              (_preview.AdditionalToPlaceCount + _preview.AdditionalPlacedCount > 0 ? $" + {_preview.AdditionalToPlaceCount + _preview.AdditionalPlacedCount} additional" : "");

        public string CollectionChipText => _preview == null || string.IsNullOrEmpty(_preview.SheetCollectionName)
            ? string.Empty
            : $"collection {_preview.SheetCollectionName}{(_preview.SheetCollectionWillBeCreated ? " (new)" : "")}";

        public bool HasCollectionChip => !string.IsNullOrEmpty(CollectionChipText);

        public string BreakdownText => _preview == null
            ? string.Empty
            : $"{_preview.NewViewCount} new · {_preview.UpdateViewCount} update · {_preview.KeptViewCount} kept · {_preview.NewSheetCount} new sheet(s)" +
              (_preview.AdditionalToPlaceCount > 0 ? $" · {_preview.AdditionalToPlaceCount} additional to place" : "");

        public string ConflictText => _overlapReason ?? _preview?.BlockReason ?? _previewError;

        public bool HasConflict => !string.IsNullOrEmpty(ConflictText);

        public string LastGeneratedText
        {
            get
            {
                if (SelectedPackage == null || SelectedBuilding == null) return string.Empty;
                SheetPackageRunRecord record = _model.FindSheetRun(SelectedPackage.Package.Id, SelectedBuilding.Definition.Id);
                if (record == null) return "🕒 Never generated for this set and building.";
                DateTime local = record.LastRunUtc.ToLocalTime();
                return $"🕒 Last generated: {local:yyyy-MM-dd HH:mm} · {record.User} · {record.SheetCount} sheet(s) · {record.ViewCount} view(s)";
            }
        }

        public string PreviewTooltip => SelectedPackage == null
            ? "Select a set on the left first."
            : SelectedBuilding == null
                ? "Select a building — a run is always one set for one building."
                : "Re-read the model and show what this run would do. Nothing changes until you press Generate and Place.";

        public string GenerateTooltip
        {
            get
            {
                if (HasRequiredIssues)
                {
                    return "Fill in first: " + string.Join(", ", Issues.Where(i => i.IsRequired).Select(i => i.Label)) + ".";
                }
                if (_overlapReason != null) return _overlapReason;
                if (SelectedPackage == null) return "Select a set on the left first.";
                if (SelectedBuilding == null) return "Select a building — a run is always one set for one building.";
                if (!BuildingHasFloorRanges) return SheetPackageService.NoFloorRangesReason(SelectedBuilding.Definition);
                if (_preview == null) return "Run Preview first — Generate applies the previewed diff.";
                if (_preview.IsBlocked) return _preview.BlockReason;
                return "Create the missing views, update the ones this set already owns, place the additional views and put everything "
                    + "on the set's sheets. Existing viewports are never moved, and sheets or views this set does not own are never touched.";
            }
        }

        // ── Loading ──────────────────────────────────────────────────────────

        public void LoadFrom(ZfaProjectModel model, RevitContext context)
        {
            _loading = true;
            try
            {
                _model = model ?? new ZfaProjectModel();
                _context = context ?? new RevitContext();
                if (_model.SheetPackages == null) _model.SheetPackages = SheetPackage.DefaultSeed();

                Guid keepPackage = SelectedPackage?.Package.Id ?? Guid.Empty;
                Packages.Clear();
                foreach (SheetPackage package in _model.SheetPackages)
                {
                    Packages.Add(new SheetPackageItemViewModel(package, _context, OnPackageEdited));
                }

                string keepBuilding = SelectedBuilding?.Definition.Id.ToString();
                Buildings.Clear();
                foreach (BuildingDefinition building in _model.Buildings)
                {
                    Buildings.Add(new AreaMappingViewModel.BuildingChoice(building));
                }

                _selectedBuilding = Buildings.FirstOrDefault(b => b.Definition.Id.ToString() == keepBuilding) ?? Buildings.FirstOrDefault();
                _selectedPackage = Packages.FirstOrDefault(p => p.Package.Id == keepPackage) ?? Packages.FirstOrDefault();
                RebuildTypicalFloorRows();
                SyncBuildingScopeBox();
                IsDirty = false;
                Raise(nameof(SelectedBuilding));
                Raise(nameof(SelectedPackage));
                Raise(nameof(HasPackage));
                Raise(nameof(HasBuildings));
                Raise(nameof(BuildingHasFloorRanges));
                Raise(nameof(BuildingCaption));
                RefreshOverlap();
            }
            finally
            {
                _loading = false;
            }
            RequestTitleBlockImage();
            RequestScopeBoxPreview();
            RequestPreview();
        }

        private void RebuildTypicalFloorRows()
        {
            TypicalFloorRows.Clear();
            BuildingDefinition b = SelectedBuilding?.Definition;
            if (b == null) return;
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (FloorGroup group in b.Groups ?? new List<FloorGroup>())
            {
                if (group == null) continue;
                bool duplex = group.Kind == FloorGroupKind.Duplex;
                bool single = group.Kind == FloorGroupKind.Atypical;
                List<LevelRef> members = duplex ? FloorRangeMath.CollectDuplexMembers(group) : FloorRangeMath.CollectStandardMembers(group);
                foreach (LevelRef m in members)
                {
                    if (m != null && !string.IsNullOrEmpty(m.UniqueId)) covered.Add(m.UniqueId);
                }
                string levels = members.Count == 0 ? "—"
                    : members.Count == 1 ? members[0].Name
                    : $"{members.First().Name} – {members.Last().Name}";
                string source = single || members.Count <= 1 ? null : group.SourceLevel?.Name;
                TypicalFloorRows.Add(new FloorRangeRowViewModel
                {
                    GroupName = group.Name ?? group.Kind.ToString(),
                    KindCaption = duplex ? "Duplex" : single ? "Single" : "Typical",
                    LevelsCaption = levels + (string.IsNullOrEmpty(source) ? "" : $"  (source {source})") + (members.Count > 1 ? $"  · {members.Count} levels" : "")
                });
            }
            // Single floors stored only in the exclusion list (not as a group) — listed once, never twice.
            foreach (LevelRef singleLevel in b.AtypicalLevels ?? new List<LevelRef>())
            {
                if (singleLevel == null || string.IsNullOrEmpty(singleLevel.UniqueId) || covered.Contains(singleLevel.UniqueId)) continue;
                TypicalFloorRows.Add(new FloorRangeRowViewModel { GroupName = singleLevel.Name, KindCaption = "Single", LevelsCaption = singleLevel.Name });
            }
        }

        // ── Set list commands ────────────────────────────────────────────────

        private void AddPackage()
        {
            var package = new SheetPackage { Name = UniqueName("New set"), SourceKind = SheetSourceKind.AreaPlans };
            _model.SheetPackages.Add(package);
            var item = new SheetPackageItemViewModel(package, _context, OnPackageEdited);
            Packages.Add(item);
            IsDirty = true;
            SelectedPackage = item;
        }

        private void DuplicatePackage()
        {
            SheetPackage source = SelectedPackage.Package;
            SheetPackage copy = source.CloneAsNew(UniqueName((source.Name ?? "Set") + " copy"));
            _model.SheetPackages.Add(copy);
            var item = new SheetPackageItemViewModel(copy, _context, OnPackageEdited);
            Packages.Add(item);
            IsDirty = true;
            SelectedPackage = item;
        }

        /// <summary>Removes the definition only — nothing is deleted in Revit.</summary>
        private void RemovePackage()
        {
            SheetPackageItemViewModel item = SelectedPackage;
            _model.SheetPackages.Remove(item.Package);
            Packages.Remove(item);
            IsDirty = true;
            SelectedPackage = Packages.FirstOrDefault();
        }

        private void AddAdditionalViews()
        {
            if (SelectedPackage == null || PickViewsHandler == null) return;
            var already = new HashSet<string>(SelectedPackage.Package.AdditionalViews.Select(a => a.ViewUniqueId), StringComparer.Ordinal);
            IList<(PlaceableViewRef View, bool OwnSheet)> picked = PickViewsHandler(_context.PlaceableViews, already);
            if (picked == null) return;
            SelectedPackage.AddViews(picked);
        }

        private void RemoveAdditionalView(AdditionalViewItemViewModel item) => SelectedPackage?.RemoveView(item);

        private string UniqueName(string candidate)
        {
            string baseName = string.IsNullOrWhiteSpace(candidate) ? "Set" : candidate.Trim();
            var taken = new HashSet<string>(_model.SheetPackages.Select(p => p.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string next = baseName + " " + i.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(next)) return next;
            }
            return baseName;
        }

        // ── Live preview ─────────────────────────────────────────────────────

        private void OnPackageEdited()
        {
            IsDirty = true;
            RaiseResult();
            RefreshOverlap();
            RequestTitleBlockImage();
            RequestScopeBoxPreview();
            RequestPreview();
        }

        private bool _imageInFlight;
        private bool _scopeBoxPreviewInFlight;

        /// <summary>
        /// Reads the resolved scope box (the set's own, else the building's from
        /// ZFA Step 1) on the Revit thread and draws its extent plus whatever
        /// walls fall inside it — an honestly-incomplete sanity check, not a
        /// rendered view, so the user can confirm the crop area before Generate.
        /// </summary>
        private void RequestScopeBoxPreview()
        {
            SheetPackageItemViewModel item = SelectedPackage;
            if (item == null || _loading || _scopeBoxPreviewInFlight) return;

            string buildingScopeBoxUid = SelectedBuilding?.Definition?.ScopeBoxUniqueId;
            string uid = !string.IsNullOrEmpty(item.Package.ScopeBoxUniqueId) ? item.Package.ScopeBoxUniqueId : buildingScopeBoxUid;
            if (string.IsNullOrEmpty(uid))
            {
                item.SetScopeBoxPreview(null, 0, 0, Array.Empty<PreviewLine>(),
                    "No scope box — neither this set nor the building (ZFA Step 1) has one.");
                return;
            }
            if (item.ScopeBoxPreviewForUid == uid) return;

            _scopeBoxPreviewInFlight = true;
            _shell.RunRevit("Preview scope box",
                app => ScopeBoxPreviewService.Build(app.ActiveUIDocument.Document, uid),
                result =>
                {
                    _scopeBoxPreviewInFlight = false;
                    var preview = (ScopeBoxPreviewData)result;
                    item.SetScopeBoxPreview(uid, preview.WidthFt, preview.HeightFt, preview.Lines, preview.Note);
                    if (SelectedPackage == item)
                    {
                        RequestScopeBoxPreview(); // the scope box changed again while this one was rendering
                    }
                },
                ex =>
                {
                    _scopeBoxPreviewInFlight = false;
                    item.SetScopeBoxPreview(uid, 0, 0, Array.Empty<PreviewLine>(), "Could not read the scope box: " + ex.Message);
                });
        }

        /// <summary>
        /// Renders the selected set's titleblock (once per type — cached) so the
        /// usable-area rectangle sits on the real sheet. Runs on the Revit thread
        /// in a transaction that is rolled back; the model never changes.
        /// </summary>
        private void RequestTitleBlockImage()
        {
            SheetPackageItemViewModel item = SelectedPackage;
            if (item == null || _loading || _imageInFlight) return;
            string uid = item.Package.TitleBlockTypeUniqueId;
            if (string.IsNullOrEmpty(uid))
            {
                item.SetTitleBlockImage(null, null, 0, 0, "Pick a titleblock to see it here.");
                return;
            }
            if (item.TitleBlockImageForUid == uid) return;

            _imageInFlight = true;
            _shell.RunRevit("Render titleblock",
                app => TitleBlockImageService.Render(app.ActiveUIDocument.Document, uid),
                result =>
                {
                    _imageInFlight = false;
                    var image = (TitleBlockImage)result;
                    SheetPackageItemViewModel target = Packages.FirstOrDefault(p => p.Package.TitleBlockTypeUniqueId == uid) ?? item;
                    foreach (SheetPackageItemViewModel p in Packages.Where(p => p.Package.TitleBlockTypeUniqueId == uid))
                    {
                        p.SetTitleBlockImage(uid, image.ImagePath, image.SheetWidthIn, image.SheetHeightIn,
                            image.ImagePath != null ? LinesStatus(image) : "Titleblock could not be rendered: " + image.FailureReason + " — showing the sheet outline only.",
                            image.Lines);
                    }
                    if (SelectedPackage != null && SelectedPackage.TitleBlockImageForUid != SelectedPackage.Package.TitleBlockTypeUniqueId)
                    {
                        RequestTitleBlockImage(); // the titleblock changed while rendering
                    }
                },
                ex =>
                {
                    _imageInFlight = false;
                    item.SetTitleBlockImage(uid, null, 0, 0, "Titleblock could not be rendered: " + ex.Message + " — showing the sheet outline only.");
                });
        }

        private static string LinesStatus(TitleBlockImage image)
        {
            if (image.Lines.Count == 0)
            {
                return "No lines could be read from this titleblock family: the view area has no snap targets (1/8 in steps only).";
            }
            return image.IgnoredCurveCount > 0
                ? $"{image.Lines.Count} straight lines to snap to; {image.IgnoredCurveCount} diagonal or curved ones are not snap targets."
                : null;
        }

        private void RefreshOverlap()
        {
            PackageOverlap overlap = SelectedPackage == null
                ? null
                : PackageOverlapDetector.Detect(_model.SheetPackages ?? new List<SheetPackage>()).FirstOrDefault(o => o.PackageId == SelectedPackage.Package.Id);
            _overlapReason = overlap?.Description;
            Raise(nameof(ConflictText));
            Raise(nameof(HasConflict));
            Raise(nameof(HasPreview));
            Raise(nameof(GenerateTooltip));
        }

        private bool CanPreview() => SelectedPackage != null && SelectedBuilding != null && !_shell.IsBusy;

        private bool CanGenerate() =>
            SelectedPackage != null && SelectedBuilding != null && BuildingHasFloorRanges && !_shell.IsBusy
            && !HasRequiredIssues && _overlapReason == null && _preview != null && !_preview.IsBlocked;

        private void RequestPreview()
        {
            if (_loading) return;
            if (SelectedPackage == null || SelectedBuilding == null)
            {
                ApplyPreview(null, null);
                return;
            }
            if (_previewInFlight)
            {
                _previewQueued = true;
                return;
            }
            RunPreview();
        }

        private void RunPreview()
        {
            if (SelectedPackage == null || SelectedBuilding == null)
            {
                ApplyPreview(null, null);
                return;
            }
            SheetPackage package = SelectedPackage.Package;
            BuildingDefinition building = SelectedBuilding.Definition;
            ZfaProjectModel model = _model;
            _previewInFlight = true;
            _shell.RunRevit("Preview sheet set",
                app => _service.BuildPreview(app.ActiveUIDocument.Document, package, building, model, App.Log),
                result =>
                {
                    _previewInFlight = false;
                    ApplyPreview((SheetPackagePreview)result, null);
                    DrainQueuedPreview();
                },
                ex =>
                {
                    _previewInFlight = false;
                    ApplyPreview(null, "The model could not be read: " + ex.Message);
                    DrainQueuedPreview();
                });
        }

        private void DrainQueuedPreview()
        {
            if (!_previewQueued) return;
            _previewQueued = false;
            RequestPreview();
        }

        private void ApplyPreview(SheetPackagePreview preview, string error)
        {
            _preview = preview;
            _previewError = error;
            SelectedPackage?.SetEffectiveScale(preview != null && preview.ScaleDenominator > 0 ? preview.ScaleDenominator : 0);
            ViewRows.Clear();
            MiniSheets.Clear();
            Notes.Clear();
            ContentRows.Clear();
            ExpectedRows.Clear();
            UnexpectedRows.Clear();
            PlannedViews.Clear();
            TableRows.Clear();
            if (preview != null)
            {
                foreach (SheetTableRow row in preview.TableRows) TableRows.Add(new TableRowViewModel(row));
                foreach (SheetContentRow row in preview.ContentRows)
                {
                    var vm = new SheetContentRowViewModel(row);
                    ContentRows.Add(vm);
                    (row.IsExpected ? ExpectedRows : UnexpectedRows).Add(vm);
                }
                foreach (SheetPreviewViewRow row in preview.ViewRows.Where(r => !r.IsAdditional && r.Status != SheetPreviewViewRow.StatusKept))
                {
                    PlannedViews.Add(new PlannedViewChip(row.LevelName ?? row.ViewName));
                }
                foreach (SheetPreviewViewRow row in preview.ViewRows) ViewRows.Add(new SheetPreviewRowViewModel(row));
                var newLabels = new HashSet<string>(
                    preview.ViewRows.Where(r => r.Status == SheetPreviewViewRow.StatusNew).Select(r => r.LevelName ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase);
                foreach (SheetPreviewSheet sheet in preview.Sheets) MiniSheets.Add(new MiniSheetViewModel(sheet, newLabels));
                foreach (string note in preview.Notes) Notes.Add(note);
            }
            RaiseResult();
            Raise(nameof(HasContentCheck));
            Raise(nameof(ContentCheckPasses));
            Raise(nameof(ContentVerdictText));
            Raise(nameof(HasPreview));
            Raise(nameof(SheetChipText));
            Raise(nameof(ScaleChipText));
            Raise(nameof(ViewChipText));
            Raise(nameof(CollectionChipText));
            Raise(nameof(HasCollectionChip));
            Raise(nameof(BreakdownText));
            Raise(nameof(ConflictText));
            Raise(nameof(HasConflict));
            Raise(nameof(LastGeneratedText));
            Raise(nameof(PreviewTooltip));
            Raise(nameof(GenerateTooltip));
        }

        // ── Run ──────────────────────────────────────────────────────────────

        private void Generate()
        {
            SheetPackage package = SelectedPackage.Package;
            BuildingDefinition building = SelectedBuilding.Definition;
            ZfaProjectModel model = _model;
            _shell.RunRevit($"Generate sheets ({package.Name} / {building.Name})",
                app => _service.Generate(app.ActiveUIDocument.Document, package, building, model, App.Log),
                result =>
                {
                    var operation = (OperationResult<PlacedViewportInfo>)result;
                    _shell.ShowResult($"Sheet set — {package.Name} / {building.Name}", operation.BuildSummary());
                    if (operation.Succeeded.Count > 0) MarkSaved(); // a run that did something also saves the sets
                    RequestPreview();
                });
        }

        private static Brush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        // ── Row / mini-sheet shapes ──────────────────────────────────────────

        internal sealed class TableRowViewModel
        {
            public TableRowViewModel(SheetTableRow row)
            {
                Name = row.TableName;
                Status = row.Status;
                SheetNumber = row.SheetNumber;
                Detail = row.Detail;
            }

            public string Name { get; }

            public string Status { get; }

            public string SheetNumber { get; }

            public string Detail { get; }
        }

        internal sealed class PlannedViewChip
        {
            public PlannedViewChip(string name)
            {
                Name = name;
                IsRange = name != null && name.IndexOf(" TO ", StringComparison.Ordinal) >= 0;
            }

            public string Name { get; }

            /// <summary>True for the one view that stands for a whole Typical or Duplex range.</summary>
            public bool IsRange { get; }
        }

        internal sealed class FloorRangeRowViewModel
        {
            public string GroupName { get; set; }
            public string KindCaption { get; set; }
            public string LevelsCaption { get; set; }

            /// <summary>Drives the tag color: Typical and Duplex ranges read as "Complete" (green), Single floors stay neutral.</summary>
            public string TagState => KindCaption == "Single" ? string.Empty : "Complete";
        }

        internal sealed class SheetContentRowViewModel
        {
            private static readonly Brush OkBrush = Freeze("#059669");
            private static readonly Brush WarnBrush = Freeze("#D97706");

            public SheetContentRowViewModel(SheetContentRow row)
            {
                SheetNumber = row.SheetNumber;
                ViewName = row.ViewName;
                Detail = row.Detail;
                Mark = row.IsExpected ? "✓" : "⚠";
                MarkBrush = row.IsExpected ? OkBrush : WarnBrush;
            }

            public string SheetNumber { get; }

            public string ViewName { get; }

            public string Detail { get; }

            public string Mark { get; }

            public Brush MarkBrush { get; }
        }

        internal sealed class SheetPreviewRowViewModel
        {
            public SheetPreviewRowViewModel(SheetPreviewViewRow row)
            {
                LevelName = row.LevelName;
                ViewName = row.ViewName;
                Status = row.IsAdditional ? (row.Status == SheetPreviewViewRow.StatusNew ? "PLACE" : "PLACED") : row.Status;
                if (row.IsAdditional)
                {
                    ColorBrush = row.Status == SheetPreviewViewRow.StatusNew ? AdditionalBrush : KeptBrush;
                    Detail = row.Detail ?? "existing view placed as-is";
                }
                else if (row.Status == SheetPreviewViewRow.StatusNew)
                {
                    ColorBrush = NewBrush;
                    Detail = "view will be created and placed in a free slot";
                }
                else if (row.Status == SheetPreviewViewRow.StatusUpdate)
                {
                    ColorBrush = UpdateBrush;
                    Detail = "already owned — template, scale and browser values re-applied, viewport not moved";
                }
                else
                {
                    ColorBrush = KeptBrush;
                    Detail = "no longer a source level — kept exactly as it is";
                }
                if (!string.IsNullOrEmpty(row.Detail) && !row.IsAdditional)
                {
                    Detail = row.Detail;
                }
            }

            public string LevelName { get; }
            public string ViewName { get; }
            public string Status { get; }
            public string Detail { get; }
            public Brush ColorBrush { get; }
        }

        internal sealed class MiniSheetViewModel
        {
            public MiniSheetViewModel(SheetPreviewSheet sheet, HashSet<string> newLabels)
            {
                SheetNumber = sheet.SheetNumber;
                SheetName = sheet.SheetName;
                StatusCaption = sheet.StatusCaption;
                IsNew = sheet.IsNew;
                Columns = Math.Max(1, sheet.Columns);
                Rows = Math.Max(1, sheet.Rows);
                double ratio = sheet.SheetWidthIn > 0 && sheet.SheetHeightIn > 0 ? sheet.SheetHeightIn / sheet.SheetWidthIn : 0.75;
                Width = MiniSheetWidth;
                Height = Math.Round(MiniSheetWidth * ratio);
                StripWidth = sheet.SheetWidthIn > 0 ? Math.Round(MiniSheetWidth * sheet.TitleStripWidthIn / sheet.SheetWidthIn) : 16;
                CaptionBrush = sheet.IsNew ? NewBrush : KeptBrush;
                foreach (string label in sheet.SlotLabels)
                {
                    bool empty = string.Equals(label, SheetPackageService.EmptySlotLabel, StringComparison.OrdinalIgnoreCase);
                    Slots.Add(new MiniSlotViewModel(label, empty, !empty && newLabels.Contains(label ?? string.Empty)));
                }
            }

            public string SheetNumber { get; }
            public string SheetName { get; }
            public string StatusCaption { get; }
            public bool IsNew { get; }
            public int Columns { get; }
            public int Rows { get; }
            public double Width { get; }
            public double Height { get; }
            public double StripWidth { get; }
            public Brush CaptionBrush { get; }
            public Brush StripFill => StripBrush;
            public ObservableCollection<MiniSlotViewModel> Slots { get; } = new ObservableCollection<MiniSlotViewModel>();
        }

        internal sealed class MiniSlotViewModel
        {
            public MiniSlotViewModel(string label, bool isEmpty, bool isNew)
            {
                Label = label;
                IsEmpty = isEmpty;
                IsNew = isNew;
                Fill = isNew ? NewSlotFill : isEmpty ? EmptySlotFill : FilledSlotFill;
                Stroke = isNew ? NewBrush : SlotBorderBrush;
                TextBrush = isNew ? NewBrush : isEmpty ? KeptBrush : TextBrushDark;
                Opacity = isEmpty ? 0.6 : 1.0;
            }

            public string Label { get; }
            public bool IsEmpty { get; }
            public bool IsNew { get; }
            public Brush Fill { get; }
            public Brush Stroke { get; }
            public Brush TextBrush { get; }
            public double Opacity { get; }
        }
    }
}
