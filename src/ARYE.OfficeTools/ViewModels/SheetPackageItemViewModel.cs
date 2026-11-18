using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Infrastructure;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// One editable sheet set. Every setter writes straight into the
    /// <see cref="SheetPackage"/> held by the project model, so "Save sets"
    /// persists edits with no copy-back step — and signals the parent, which
    /// re-runs the live preview.
    /// </summary>
    internal sealed class SheetPackageItemViewModel : ViewModelBase
    {
        public static readonly NamedElementRef NoViewTemplate = new NamedElementRef { UniqueId = null, Name = "(none)" };

        /// <summary>Sentinel: crop created views with the building's scope box from ZFA Step 1.</summary>
        public static readonly NamedElementRef BuildingScopeBox = new NamedElementRef { UniqueId = null, Name = "(building's scope box — ZFA Step 1)" };

        private string _titleBlockImagePath;
        private double _titleBlockSheetWidthIn = 36;
        private double _titleBlockSheetHeightIn = 24;
        private string _titleBlockImageStatus = "Pick a titleblock to see it here.";

        private readonly Action _onEdited;
        private RevitContext _context;

        public SheetPackageItemViewModel(SheetPackage package, RevitContext context, Action onEdited)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            _onEdited = () => { onEdited?.Invoke(); RaiseReadout(); RaiseSetup(); };
            ApplyContext(context);
        }

        public SheetPackage Package { get; }

        public IReadOnlyList<int> GridOptions { get; } = new[] { 1, 2, 3, 4 };

        public ObservableCollection<NamedElementRef> ViewTemplateOptions { get; } = new ObservableCollection<NamedElementRef>();

        public ObservableCollection<NamedElementRef> AreaSchemeOptions { get; } = new ObservableCollection<NamedElementRef>();

        public ObservableCollection<NamedElementRef> TitleBlockOptions { get; } = new ObservableCollection<NamedElementRef>();

        public ObservableCollection<string> SheetCollectionOptions { get; } = new ObservableCollection<string>();

        public ObservableCollection<NamedElementRef> ScopeBoxOptions { get; } = new ObservableCollection<NamedElementRef>();

        // ── Titleblock image (set by the parent after rendering on the Revit thread) ──

        public string TitleBlockImagePath { get => _titleBlockImagePath; private set => Set(ref _titleBlockImagePath, value); }

        public double TitleBlockSheetWidthIn { get => _titleBlockSheetWidthIn; private set => Set(ref _titleBlockSheetWidthIn, value); }

        public double TitleBlockSheetHeightIn { get => _titleBlockSheetHeightIn; private set => Set(ref _titleBlockSheetHeightIn, value); }

        public string TitleBlockImageStatus { get => _titleBlockImageStatus; private set => Set(ref _titleBlockImageStatus, value); }

        /// <summary>UniqueId of the titleblock the current image belongs to (null = none rendered yet).</summary>
        public string TitleBlockImageForUid { get; private set; }

        private IReadOnlyList<Services.TitleBlockLine> _titleBlockLines = Array.Empty<Services.TitleBlockLine>();
        private bool _snapToTitleBlock = true;
        private bool _showTitleBlockLines = true;

        /// <summary>The family's straight lines the usable area snaps to (empty when none could be read).</summary>
        public IReadOnlyList<Services.TitleBlockLine> TitleBlockLines { get => _titleBlockLines; private set => Set(ref _titleBlockLines, value); }

        /// <summary>Window preference, not saved with the set.</summary>
        public bool SnapToTitleBlock { get => _snapToTitleBlock; set => Set(ref _snapToTitleBlock, value); }

        public IReadOnlyList<double> SnapDistanceOptions { get; } = new[] { 0.15, 0.3, 0.5 };

        private double _snapDistanceIn = 0.3;

        /// <summary>Window preference, not saved with the set.</summary>
        public double SnapDistanceIn { get => _snapDistanceIn; set => Set(ref _snapDistanceIn, value); }

        /// <summary>Window preference, not saved with the set.</summary>
        public bool ShowTitleBlockLines { get => _showTitleBlockLines; set => Set(ref _showTitleBlockLines, value); }

        public void SetTitleBlockImage(string forUid, string imagePath, double sheetWidthIn, double sheetHeightIn, string status,
            IReadOnlyList<Services.TitleBlockLine> lines = null)
        {
            TitleBlockLines = lines ?? Array.Empty<Services.TitleBlockLine>();
            RaiseReadout();
            TitleBlockImageForUid = forUid;
            TitleBlockImagePath = imagePath;
            if (sheetWidthIn > 0 && sheetHeightIn > 0)
            {
                TitleBlockSheetWidthIn = sheetWidthIn;
                TitleBlockSheetHeightIn = sheetHeightIn;
            }
            TitleBlockImageStatus = status ?? string.Empty;
        }

        // ── Scope box preview (set by the parent after reading it on the Revit thread) ──

        private double _scopeBoxPreviewWidthFt;
        private double _scopeBoxPreviewHeightFt;
        private IReadOnlyList<Services.PreviewLine> _scopeBoxPreviewLines = Array.Empty<Services.PreviewLine>();
        private string _scopeBoxPreviewNote = "Pick a scope box to see it here.";

        public double ScopeBoxPreviewWidthFt { get => _scopeBoxPreviewWidthFt; private set => Set(ref _scopeBoxPreviewWidthFt, value); }

        public double ScopeBoxPreviewHeightFt { get => _scopeBoxPreviewHeightFt; private set => Set(ref _scopeBoxPreviewHeightFt, value); }

        public IReadOnlyList<Services.PreviewLine> ScopeBoxPreviewLines { get => _scopeBoxPreviewLines; private set => Set(ref _scopeBoxPreviewLines, value); }

        public string ScopeBoxPreviewNote { get => _scopeBoxPreviewNote; private set => Set(ref _scopeBoxPreviewNote, value); }

        /// <summary>The resolved scope box UniqueId (set's own, or the building's) the current preview belongs to. Null = none rendered yet.</summary>
        public string ScopeBoxPreviewForUid { get; private set; }

        public void SetScopeBoxPreview(string forUid, double widthFt, double heightFt, IReadOnlyList<Services.PreviewLine> lines, string note)
        {
            ScopeBoxPreviewForUid = forUid;
            ScopeBoxPreviewWidthFt = widthFt;
            ScopeBoxPreviewHeightFt = heightFt;
            ScopeBoxPreviewLines = lines ?? Array.Empty<Services.PreviewLine>();
            ScopeBoxPreviewNote = note ?? string.Empty;
        }

        private int _effectiveScaleDenominator;

        /// <summary>
        /// The run's resolved scale (0 = not previewed yet) — set by the parent
        /// after BuildPreview, from the same SmartScaleAdvisor result the "scale"
        /// chip shows. Feeds the sample content drawn in "View area on the
        /// titleblock", so what you see there is the actual scale Generate uses.
        /// </summary>
        public int EffectiveScaleDenominator { get => _effectiveScaleDenominator; private set => Set(ref _effectiveScaleDenominator, value); }

        public void SetEffectiveScale(int denominator)
        {
            EffectiveScaleDenominator = denominator;
            RaiseReadout();
        }

        // ── Readout under the viewer: area, scale and the size of every cell ──

        public string AreaReadout
        {
            get
            {
                double width = TitleBlockSheetWidthIn - Package.EffectiveMarginLeftIn - Package.EffectiveMarginRightIn;
                double height = TitleBlockSheetHeightIn - Package.EffectiveMarginTopIn - Package.EffectiveMarginBottomIn;
                return width > 0 && height > 0 ? $"view area {width:0.##} x {height:0.##} in" : "view area: no room left";
            }
        }

        public string ScaleReadout => EffectiveScaleDenominator > 0
            ? "scale " + new ScaleOption(EffectiveScaleDenominator).Label
            : "scale: not previewed yet";

        public IReadOnlyList<CellReadoutRow> CellRows
        {
            get
            {
                double width = TitleBlockSheetWidthIn - Package.EffectiveMarginLeftIn - Package.EffectiveMarginRightIn;
                double height = TitleBlockSheetHeightIn - Package.EffectiveMarginTopIn - Package.EffectiveMarginBottomIn;
                try
                {
                    IReadOnlyList<ARYE.OfficeTools.Core.Sheets.SlotRect> slots = ARYE.OfficeTools.Core.Sheets.SheetSlotLayout.ComputeGrid(
                        width, height, Package.Columns, Package.Rows, Package.ColumnCumulative, Package.RowCumulative);
                    return slots.Select((slot, i) => new CellReadoutRow
                    {
                        Number = (i + 1).ToString(CultureInfo.InvariantCulture),
                        Width = slot.WidthIn.ToString("0.##", CultureInfo.InvariantCulture) + " in",
                        Height = slot.HeightIn.ToString("0.##", CultureInfo.InvariantCulture) + " in"
                    }).ToList();
                }
                catch (ArgumentException)
                {
                    return new List<CellReadoutRow>();
                }
            }
        }

        private void RaiseReadout()
        {
            Raise(nameof(AreaReadout));
            Raise(nameof(ScaleReadout));
            Raise(nameof(CellRows));
        }

        // ── Scope box & usable-area margins ─────────────────────────────────

        public NamedElementRef ScopeBox
        {
            get => Find(ScopeBoxOptions, Package.ScopeBoxUniqueId) ?? BuildingScopeBox;
            set
            {
                string uid = value?.UniqueId;
                if ((Package.ScopeBoxUniqueId ?? string.Empty) == (uid ?? string.Empty)) return;
                Package.ScopeBoxUniqueId = uid;
                Raise();
                _onEdited?.Invoke();
            }
        }

        public bool ReapplyScopeBoxOnUpdate
        {
            get => Package.ReapplyScopeBoxOnUpdate;
            set { if (Package.ReapplyScopeBoxOnUpdate == value) return; Package.ReapplyScopeBoxOnUpdate = value; Raise(); _onEdited?.Invoke(); }
        }

        /// <summary>On (default): draws only Single/Typical/Duplex Floor Range levels. Off: every matching view in the model draws, Floor Range or not.</summary>
        public bool RestrictToFloorRangeLevels
        {
            get => Package.RestrictToFloorRangeLevels;
            set { if (Package.RestrictToFloorRangeLevels == value) return; Package.RestrictToFloorRangeLevels = value; Raise(); _onEdited?.Invoke(); }
        }

        /// <summary>Off (default, unchanged behavior): one view per level of a Typical/Duplex range. On: one view per source level, titled for the whole range.</summary>

        /// <summary>Auto + the standard imperial scales, so nobody has to know that 1/8" = 1'-0" is 96.</summary>
        public IReadOnlyList<ScaleOption> ScaleOptions { get; } = ScaleOption.Standard();

        public ScaleOption SelectedScale
        {
            get => ScaleOptions.FirstOrDefault(o => o.Denominator == Package.ScaleDenominatorOverride)
                   ?? (Package.ScaleDenominatorOverride > 0 ? new ScaleOption(Package.ScaleDenominatorOverride) : ScaleOptions[0]);
            set
            {
                int denominator = value?.Denominator ?? 0;
                if (Package.ScaleDenominatorOverride == denominator) return;
                Package.ScaleDenominatorOverride = denominator;
                Raise();
                Raise(nameof(ScaleOverrideText));
                _onEdited?.Invoke();
            }
        }

        public double MarginLeftIn
        {
            get => Package.EffectiveMarginLeftIn;
            set { if (Math.Abs(Package.EffectiveMarginLeftIn - value) < 1e-9 || value < 0) return; Package.MarginLeftIn = value; Raise(); _onEdited?.Invoke(); }
        }

        public double MarginRightIn
        {
            get => Package.EffectiveMarginRightIn;
            set { if (Math.Abs(Package.EffectiveMarginRightIn - value) < 1e-9 || value < 0) return; Package.MarginRightIn = value; Raise(); _onEdited?.Invoke(); }
        }

        public double MarginTopIn
        {
            get => Package.EffectiveMarginTopIn;
            set { if (Math.Abs(Package.EffectiveMarginTopIn - value) < 1e-9 || value < 0) return; Package.MarginTopIn = value; Raise(); _onEdited?.Invoke(); }
        }

        public double MarginBottomIn
        {
            get => Package.EffectiveMarginBottomIn;
            set { if (Math.Abs(Package.EffectiveMarginBottomIn - value) < 1e-9 || value < 0) return; Package.MarginBottomIn = value; Raise(); _onEdited?.Invoke(); }
        }

        public ObservableCollection<ParameterAssignmentViewModel> SheetParameters { get; } = new ObservableCollection<ParameterAssignmentViewModel>();

        public ObservableCollection<ParameterAssignmentViewModel> ViewParameters { get; } = new ObservableCollection<ParameterAssignmentViewModel>();

        public ObservableCollection<AdditionalViewItemViewModel> AdditionalViews { get; } = new ObservableCollection<AdditionalViewItemViewModel>();

        public string Display => $"{Name}  ·  {NumberPrefix}{StartNumber}";

        public string SourceCaption => IsAreaPlans ? "Area plans" : IsCeilingPlans ? "Ceiling plans" : "Floor plans";

        public string SlotsCaption => $"{Columns}×{Rows} per sheet";

        public string SheetOrganizationCaption => _context.SheetGroupingParameters.Count == 0
            ? "No text parameter groups sheets in this model's Project Browser — nothing to set."
            : $"Parameters of the active sheet browser organization" +
              (string.IsNullOrEmpty(_context.SheetBrowserOrganizationName) ? "" : $" “{_context.SheetBrowserOrganizationName}”") + ".";

        public string ViewOrganizationCaption => _context.ViewGroupingParameters.Count == 0
            ? "No text parameter groups views in this model's Project Browser — nothing to set."
            : $"Parameters of the active view browser organization" +
              (string.IsNullOrEmpty(_context.ViewBrowserOrganizationName) ? "" : $" “{_context.ViewBrowserOrganizationName}”") + ".";

        public bool HasAdditionalViews => AdditionalViews.Count > 0;

        // ── Identity & numbering ─────────────────────────────────────────────

        public string Name
        {
            get => Package.Name ?? string.Empty;
            set { if (Package.Name == value) return; Package.Name = value; Raise(); Raise(nameof(Display)); _onEdited?.Invoke(); }
        }

        public string NumberPrefix
        {
            get => Package.NumberPrefix ?? string.Empty;
            set { if (Package.NumberPrefix == value) return; Package.NumberPrefix = value; Raise(); Raise(nameof(Display)); _onEdited?.Invoke(); }
        }

        public int StartNumber
        {
            get => Package.StartNumber;
            set { if (Package.StartNumber == value) return; Package.StartNumber = value; Raise(); Raise(nameof(Display)); _onEdited?.Invoke(); }
        }

        public int NumberIncrement
        {
            get => Package.NumberIncrement;
            set { if (Package.NumberIncrement == value) return; Package.NumberIncrement = value; Raise(); _onEdited?.Invoke(); }
        }

        public NamedElementRef TitleBlock
        {
            get => Find(TitleBlockOptions, Package.TitleBlockTypeUniqueId);
            set { if (Package.TitleBlockTypeUniqueId == value?.UniqueId) return; Package.TitleBlockTypeUniqueId = value?.UniqueId; Raise(); _onEdited?.Invoke(); }
        }

        /// <summary>Existing collection name or a new one typed in. Empty = none.</summary>
        public string SheetCollectionName
        {
            get => Package.SheetCollectionName ?? string.Empty;
            set
            {
                string v = (value ?? string.Empty).Trim();
                if ((Package.SheetCollectionName ?? string.Empty) == v) return;
                Package.SheetCollectionName = v;
                Raise();
                Raise(nameof(SheetCollectionNote));
                _onEdited?.Invoke();
            }
        }

        public string SheetCollectionNote
        {
            get
            {
                string name = SheetCollectionName;
                if (name.Length == 0) return "No sheet collection — sheets stay uncollected.";
                bool exists = SheetCollectionOptions.Any(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
                return exists
                    ? $"Existing collection. New sheets join it; sheets this set already owns are moved into it on the next run."
                    : $"New collection — created on the next run.";
            }
        }

        // ── Source ───────────────────────────────────────────────────────────

        public bool IsFloorPlans { get => Package.SourceKind == SheetSourceKind.FloorPlans; set { if (value) SetSourceKind(SheetSourceKind.FloorPlans); } }

        public bool IsAreaPlans { get => Package.SourceKind == SheetSourceKind.AreaPlans; set { if (value) SetSourceKind(SheetSourceKind.AreaPlans); } }

        public bool IsCeilingPlans { get => Package.SourceKind == SheetSourceKind.CeilingPlans; set { if (value) SetSourceKind(SheetSourceKind.CeilingPlans); } }

        public bool IsAreaSchemeEnabled => IsAreaPlans;

        public NamedElementRef AreaScheme
        {
            get => Find(AreaSchemeOptions, Package.AreaSchemeUniqueId);
            set { if (Package.AreaSchemeUniqueId == value?.UniqueId) return; Package.AreaSchemeUniqueId = value?.UniqueId; Raise(); _onEdited?.Invoke(); }
        }

        /// <summary>Text after the level in every view name ("GROSS AREA"). Empty = just the level label.</summary>
        public string ViewNameSuffix
        {
            get => Package.ViewNameSuffix ?? string.Empty;
            set
            {
                string next = (value ?? string.Empty).Trim();
                if ((Package.ViewNameSuffix ?? string.Empty) == next) return;
                Package.ViewNameSuffix = next;
                Raise();
                _onEdited?.Invoke();
            }
        }

        public NamedElementRef ViewTemplate
        {
            get => Find(ViewTemplateOptions, Package.ViewTemplateUniqueId) ?? NoViewTemplate;
            set
            {
                string uniqueId = value?.UniqueId;
                if (Package.ViewTemplateUniqueId == uniqueId) return;
                Package.ViewTemplateUniqueId = uniqueId;
                Raise();
                _onEdited?.Invoke();
            }
        }

        // ── Layout ───────────────────────────────────────────────────────────

        public int Columns
        {
            get => Package.Columns;
            set
            {
                if (Package.Columns == value) return;
                Package.Columns = value;
                Package.ColumnCumulative = new List<double>(); // a different count means equal cells again
                Raise(); Raise(nameof(SlotsCaption)); Raise(nameof(ColumnCumulative)); Raise(nameof(CellsCaption)); Raise(nameof(HasCustomCells));
                _onEdited?.Invoke();
            }
        }

        public int Rows
        {
            get => Package.Rows;
            set
            {
                if (Package.Rows == value) return;
                Package.Rows = value;
                Package.RowCumulative = new List<double>();
                Raise(); Raise(nameof(SlotsCaption)); Raise(nameof(RowCumulative)); Raise(nameof(CellsCaption)); Raise(nameof(HasCustomCells));
                _onEdited?.Invoke();
            }
        }

        /// <summary>Dragged column sizes (empty = equal). Bound two-way to the layout control, which pushes once per drag.</summary>
        public double[] ColumnCumulative
        {
            get => (Package.ColumnCumulative ?? new List<double>()).ToArray();
            set { StoreCells(value, Package.Columns, true); }
        }

        public double[] RowCumulative
        {
            get => (Package.RowCumulative ?? new List<double>()).ToArray();
            set { StoreCells(value, Package.Rows, false); }
        }

        private void StoreCells(double[] value, int count, bool columns)
        {
            List<double> next = value == null || ARYE.OfficeTools.Core.Sheets.SheetSlotLayout.IsEqualCumulative(value, count)
                ? new List<double>() : new List<double>(value);
            List<double> current = columns ? Package.ColumnCumulative : Package.RowCumulative;
            if (current != null && current.SequenceEqual(next)) return;
            if (columns) Package.ColumnCumulative = next; else Package.RowCumulative = next;
            Raise(columns ? nameof(ColumnCumulative) : nameof(RowCumulative));
            Raise(nameof(CellsCaption)); Raise(nameof(HasCustomCells));
            _onEdited?.Invoke();
        }

        public bool HasCustomCells => !(ARYE.OfficeTools.Core.Sheets.SheetSlotLayout.IsEqualCumulative(Package.ColumnCumulative, Package.Columns)
                                        && ARYE.OfficeTools.Core.Sheets.SheetSlotLayout.IsEqualCumulative(Package.RowCumulative, Package.Rows));

        public string CellsCaption => HasCustomCells
            ? "Custom cell sizes. One scale still applies to the whole set, sized for the smallest cell."
            : "All cells equal.";

        private RelayCommand _equalCellsCommand;

        public RelayCommand EqualCellsCommand => _equalCellsCommand ?? (_equalCellsCommand = new RelayCommand(MakeCellsEqual, () => HasCustomCells));

        public void MakeCellsEqual()
        {
            if (!HasCustomCells) return;
            Package.ColumnCumulative = new List<double>();
            Package.RowCumulative = new List<double>();
            Raise(nameof(ColumnCumulative)); Raise(nameof(RowCumulative)); Raise(nameof(CellsCaption)); Raise(nameof(HasCustomCells));
            _onEdited?.Invoke();
        }

        public bool DrawSeparatorLines
        {
            get => Package.DrawSeparatorLines;
            set { if (Package.DrawSeparatorLines == value) return; Package.DrawSeparatorLines = value; Raise(); _onEdited?.Invoke(); }
        }

        public ObservableCollection<string> LineStyleOptions { get; } = new ObservableCollection<string>();

        public const string DefaultLineStyle = "Thin Lines";

        public string SeparatorLineStyle
        {
            get => string.IsNullOrEmpty(Package.SeparatorLineStyle) ? DefaultLineStyle : Package.SeparatorLineStyle;
            set
            {
                string next = value == DefaultLineStyle ? string.Empty : value ?? string.Empty;
                if ((Package.SeparatorLineStyle ?? string.Empty) == next) return;
                Package.SeparatorLineStyle = next;
                Raise(); _onEdited?.Invoke();
            }
        }

        public double TitleStripWidthIn
        {
            get => Package.TitleStripWidthIn;
            set { if (Math.Abs(Package.TitleStripWidthIn - value) < 1e-9) return; Package.TitleStripWidthIn = value; Raise(); _onEdited?.Invoke(); }
        }

        public string ScaleOverrideText
        {
            get => Package.ScaleDenominatorOverride > 0 ? Package.ScaleDenominatorOverride.ToString(CultureInfo.InvariantCulture) : string.Empty;
            set
            {
                int parsed = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d > 0 ? d : 0;
                if (Package.ScaleDenominatorOverride == parsed) return;
                Package.ScaleDenominatorOverride = parsed;
                Raise();
                _onEdited?.Invoke();
            }
        }

        // ── Additional views ─────────────────────────────────────────────────

        /// <summary>Adds picked views (skipping ones already in the set) and re-previews.</summary>
        public int AddViews(IEnumerable<(PlaceableViewRef View, bool OwnSheet)> picked)
        {
            int added = 0;
            foreach ((PlaceableViewRef view, bool ownSheet) in picked)
            {
                if (view == null || Package.AdditionalViews.Any(a => a.ViewUniqueId == view.UniqueId))
                {
                    continue;
                }
                var reference = new AdditionalViewRef
                {
                    ViewUniqueId = view.UniqueId, ViewName = view.Name, ViewTypeName = view.ViewTypeName, OwnSheet = ownSheet
                };
                Package.AdditionalViews.Add(reference);
                AdditionalViews.Add(new AdditionalViewItemViewModel(reference, view.OnSheetNumber, OnAdditionalEdited));
                added++;
            }
            if (added > 0)
            {
                Raise(nameof(HasAdditionalViews));
                _onEdited?.Invoke();
            }
            return added;
        }

        public void RemoveView(AdditionalViewItemViewModel item)
        {
            if (item == null) return;
            Package.AdditionalViews.Remove(item.Reference);
            AdditionalViews.Remove(item);
            Raise(nameof(HasAdditionalViews));
            _onEdited?.Invoke();
        }

        private void OnAdditionalEdited() => _onEdited?.Invoke();

        // ── Document snapshot ────────────────────────────────────────────────

        public void ApplyContext(RevitContext context)
        {
            _context = context ?? new RevitContext();

            TitleBlockOptions.Clear();
            foreach (NamedElementRef titleBlock in _context.TitleBlockTypes) TitleBlockOptions.Add(titleBlock);

            AreaSchemeOptions.Clear();
            foreach (NamedElementRef scheme in _context.AreaSchemes) AreaSchemeOptions.Add(scheme);

            SheetCollectionOptions.Clear();
            foreach (NamedElementRef c in _context.SheetCollections) SheetCollectionOptions.Add(c.Name);

            ScopeBoxOptions.Clear();
            ScopeBoxOptions.Add(BuildingScopeBox);
            foreach (NamedElementRef sb in _context.ScopeBoxes) ScopeBoxOptions.Add(sb);

            AreaTableOptions.Clear();
            foreach (NamedElementRef schedule in _context.AreaSchedules) AreaTableOptions.Add(schedule);
            AreaTableViewTemplateOptions.Clear();
            AreaTableViewTemplateOptions.Add(NoViewTemplate);
            foreach (NamedElementRef template in _context.ScheduleTemplates) AreaTableViewTemplateOptions.Add(template);

            LineStyleOptions.Clear();
            foreach (string style in _context.LineStyleNames) LineStyleOptions.Add(style);
            if (!LineStyleOptions.Contains(DefaultLineStyle)) LineStyleOptions.Insert(0, DefaultLineStyle);
            if (!string.IsNullOrEmpty(Package.SeparatorLineStyle) && !LineStyleOptions.Contains(Package.SeparatorLineStyle))
            {
                LineStyleOptions.Add(Package.SeparatorLineStyle); // a style from another model stays visible, never silently swapped
            }

            RebuildTemplateOptions();
            RebuildParameterRows(SheetParameters, Package.SheetParameterValues, _context.SheetGroupingParameters);
            RebuildParameterRows(ViewParameters, Package.ViewParameterValues, _context.ViewGroupingParameters);

            AdditionalViews.Clear();
            var onSheet = _context.PlaceableViews.ToDictionary(v => v.UniqueId, v => v.OnSheetNumber, StringComparer.Ordinal);
            foreach (AdditionalViewRef reference in Package.AdditionalViews ?? (Package.AdditionalViews = new List<AdditionalViewRef>()))
            {
                PlaceableViewRef live = _context.PlaceableViews.FirstOrDefault(v => v.UniqueId == reference.ViewUniqueId);
                if (live != null)
                {
                    reference.ViewName = live.Name; // keep the display cache honest
                    reference.ViewTypeName = live.ViewTypeName;
                }
                AdditionalViews.Add(new AdditionalViewItemViewModel(reference,
                    onSheet.TryGetValue(reference.ViewUniqueId ?? string.Empty, out string number) ? number : string.Empty,
                    OnAdditionalEdited) { IsMissing = live == null });
            }

            Raise(nameof(TitleBlock));
            Raise(nameof(ScopeBox));
            Raise(nameof(AreaScheme));
            Raise(nameof(ViewTemplate));
            Raise(nameof(AreaTable));
            Raise(nameof(AreaTableViewTemplate));
            Raise(nameof(SeparatorLineStyle));
            RaiseSetup();
            Raise(nameof(SheetCollectionName));
            Raise(nameof(SheetCollectionNote));
            Raise(nameof(SheetOrganizationCaption));
            Raise(nameof(ViewOrganizationCaption));
            Raise(nameof(HasAdditionalViews));
        }

        /// <summary>
        /// One row per parameter the active browser organization uses. A
        /// stored value for a parameter the organization no longer uses is
        /// kept and shown, flagged — never silently discarded.
        /// </summary>
        private void RebuildParameterRows(ObservableCollection<ParameterAssignmentViewModel> rows,
            List<ParameterValueAssignment> stored, List<GroupingParameterInfo> active)
        {
            rows.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GroupingParameterInfo info in active)
            {
                ParameterValueAssignment assignment = stored.FirstOrDefault(a => string.Equals(a.ParameterName, info.Name, StringComparison.OrdinalIgnoreCase));
                if (assignment == null)
                {
                    assignment = new ParameterValueAssignment { ParameterName = info.Name, Value = string.Empty };
                    stored.Add(assignment);
                }
                rows.Add(new ParameterAssignmentViewModel(assignment, info.Values, true, OnAdditionalEdited));
                seen.Add(info.Name);
            }
            foreach (ParameterValueAssignment orphan in stored.Where(a => !seen.Contains(a.ParameterName ?? string.Empty) && !string.IsNullOrWhiteSpace(a.Value)).ToList())
            {
                rows.Add(new ParameterAssignmentViewModel(orphan, new List<string>(), false, OnAdditionalEdited));
            }
        }

        private void SetSourceKind(SheetSourceKind kind)
        {
            if (Package.SourceKind == kind) return;
            Package.SourceKind = kind;
            RebuildTemplateOptions();
            Raise(nameof(IsFloorPlans));
            Raise(nameof(IsAreaPlans));
            Raise(nameof(IsCeilingPlans));
            Raise(nameof(IsAreaSchemeEnabled));
            Raise(nameof(SourceCaption));
            Raise(nameof(ViewTemplate));
            _onEdited?.Invoke();
        }

        /// <summary>Says how many templates fit the chosen source and how many were left out — a missing template is never unexplained.</summary>
        public string ViewTemplateCaption
        {
            get
            {
                List<NamedElementRef> fitting = IsAreaPlans ? _context.AreaPlanTemplates : IsCeilingPlans ? _context.CeilingPlanTemplates : _context.FloorPlanTemplates;
                string kind = SourceCaption.ToLowerInvariant();
                int hidden = Math.Max(0, _context.TotalViewTemplates - fitting.Count);
                if (_context.TotalViewTemplates == 0)
                {
                    return "This model has no view templates yet — create one in Revit (View › View Templates), then Refresh.";
                }
                if (fitting.Count == 0)
                {
                    return $"None of the model's {_context.TotalViewTemplates} view templates can be assigned to {kind}. "
                        + "Create one from a view of that type, then Refresh.";
                }
                return hidden == 0
                    ? $"{fitting.Count} template{(fitting.Count == 1 ? "" : "s")} fit {kind}."
                    : $"{fitting.Count} template{(fitting.Count == 1 ? "" : "s")} fit {kind} · {hidden} other{(hidden == 1 ? "" : "s")} in the model "
                      + "apply to different view types and are not offered. Created or changed one in Revit? Refresh.";
            }
        }

        private void RebuildTemplateOptions()
        {
            // Rebuilding a bound collection makes WPF momentarily push a null selection back through the
            // setter; capture the stored choice first and restore it, so a rebuild can never wipe it.
            string preserved = Package.ViewTemplateUniqueId;
            List<NamedElementRef> matching = IsAreaPlans ? _context.AreaPlanTemplates : IsCeilingPlans ? _context.CeilingPlanTemplates : _context.FloorPlanTemplates;
            IEnumerable<NamedElementRef> other = IsAreaPlans
                ? _context.FloorPlanTemplates.Concat(_context.CeilingPlanTemplates)
                : IsCeilingPlans ? _context.FloorPlanTemplates.Concat(_context.AreaPlanTemplates)
                : _context.AreaPlanTemplates.Concat(_context.CeilingPlanTemplates);

            ViewTemplateOptions.Clear();
            ViewTemplateOptions.Add(NoViewTemplate);
            foreach (NamedElementRef template in matching) ViewTemplateOptions.Add(template);

            if (!string.IsNullOrEmpty(preserved)
                && matching.All(t => t.UniqueId != preserved)
                && other.Any(t => t.UniqueId == preserved))
            {
                preserved = null;
            }
            Package.ViewTemplateUniqueId = preserved;
            Raise(nameof(ViewTemplate));
            Raise(nameof(ViewTemplateCaption));
        }

        // ── Area table ───────────────────────────────────────────────────────

        public ObservableCollection<NamedElementRef> AreaTableOptions { get; } = new ObservableCollection<NamedElementRef>();

        public ObservableCollection<NamedElementRef> AreaTableViewTemplateOptions { get; } = new ObservableCollection<NamedElementRef>();

        public IReadOnlyList<AreaTablePlacementOption> PlacementOptions { get; } = new[]
        {
            new AreaTablePlacementOption(ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.BesideViewLeft, "Beside its view, in a small strip at the left"),
            new AreaTablePlacementOption(ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.UnderView, "Under its view"),
            new AreaTablePlacementOption(ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.BesideView, "Beside its view (to the right)"),
            new AreaTablePlacementOption(ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.OwnCell, "In a cell of its own"),
            new AreaTablePlacementOption(ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.FixedEdge, "In a fixed place on the sheet")
        };

        public IReadOnlyList<AreaTableEdgeOption> EdgeOptions { get; } = new[]
        {
            new AreaTableEdgeOption(ARYE.OfficeTools.Core.Sheets.AreaTableEdge.Right, "Right column"),
            new AreaTableEdgeOption(ARYE.OfficeTools.Core.Sheets.AreaTableEdge.Left, "Left column"),
            new AreaTableEdgeOption(ARYE.OfficeTools.Core.Sheets.AreaTableEdge.Bottom, "Bottom row"),
            new AreaTableEdgeOption(ARYE.OfficeTools.Core.Sheets.AreaTableEdge.Top, "Top row")
        };

        private string _buildingName = string.Empty;

        /// <summary>The selected building's name, set by the parent — only used for the example table name.</summary>
        public string BuildingName
        {
            get => _buildingName;
            set { if (_buildingName == value) return; _buildingName = value ?? string.Empty; Raise(nameof(AreaTableNameSample)); }
        }

        public bool AddAreaTable
        {
            get => Package.AddAreaTable;
            set
            {
                if (Package.AddAreaTable == value) return;
                Package.AddAreaTable = value;
                Raise();
                _onEdited?.Invoke();
            }
        }

        public NamedElementRef AreaTable
        {
            get => Find(AreaTableOptions, Package.AreaTableTemplateUniqueId);
            set
            {
                if (Package.AreaTableTemplateUniqueId == value?.UniqueId) return;
                Package.AreaTableTemplateUniqueId = value?.UniqueId;
                Raise();
                _onEdited?.Invoke();
            }
        }

        public NamedElementRef AreaTableViewTemplate
        {
            get => Find(AreaTableViewTemplateOptions, Package.AreaTableViewTemplateUniqueId) ?? NoViewTemplate;
            set
            {
                string uid = value?.UniqueId;
                if ((Package.AreaTableViewTemplateUniqueId ?? string.Empty) == (uid ?? string.Empty)) return;
                Package.AreaTableViewTemplateUniqueId = uid;
                Raise();
                _onEdited?.Invoke();
            }
        }

        public string AreaTableNamePattern
        {
            get => Package.AreaTableNamePattern ?? string.Empty;
            set
            {
                string next = (value ?? string.Empty).Trim();
                if ((Package.AreaTableNamePattern ?? string.Empty) == next) return;
                Package.AreaTableNamePattern = next;
                Raise();
                Raise(nameof(AreaTableNameSample));
                _onEdited?.Invoke();
            }
        }

        public string AreaTableBuildingValue
        {
            get => Package.AreaTableBuildingValue ?? string.Empty;
            set
            {
                string next = (value ?? string.Empty).Trim();
                if ((Package.AreaTableBuildingValue ?? string.Empty) == next) return;
                Package.AreaTableBuildingValue = next;
                Raise();
                Raise(nameof(AreaTableNameSample));
                _onEdited?.Invoke();
            }
        }

        public AreaTablePlacementOption SelectedPlacement
        {
            get => PlacementOptions.First(o => o.Placement == Package.AreaTablePlacement);
            set
            {
                if (value == null || Package.AreaTablePlacement == value.Placement) return;
                Package.AreaTablePlacement = value.Placement;
                Raise();
                Raise(nameof(IsEdgePlacement));
                Raise(nameof(UsesGap));
                _onEdited?.Invoke();
            }
        }

        public AreaTableEdgeOption SelectedEdge
        {
            get => EdgeOptions.First(o => o.Edge == Package.AreaTableEdge);
            set
            {
                if (value == null || Package.AreaTableEdge == value.Edge) return;
                Package.AreaTableEdge = value.Edge;
                Raise();
                _onEdited?.Invoke();
            }
        }

        public bool IsEdgePlacement => Package.AreaTablePlacement == ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.FixedEdge;

        public bool UsesGap => Package.AreaTablePlacement == ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.UnderView
                               || Package.AreaTablePlacement == ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.BesideView
                               || Package.AreaTablePlacement == ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.BesideViewLeft
                               || Package.AreaTablePlacement == ARYE.OfficeTools.Core.Sheets.AreaTablePlacement.FixedEdge;

        public string AreaTableGapText
        {
            get => Package.AreaTableGapIn.ToString("0.##", CultureInfo.InvariantCulture);
            set
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double gap) || gap < 0 || gap > 12) { Raise(); return; }
                if (Math.Abs(Package.AreaTableGapIn - gap) < 1e-9) return;
                Package.AreaTableGapIn = gap;
                Raise();
                _onEdited?.Invoke();
            }
        }

        /// <summary>What a table would be called for a 2nd-floor view: shows the effect of the name pattern as you type.</summary>
        public string AreaTableNameSample => ARYE.OfficeTools.Core.Sheets.SheetNaming.ComposeTableName(
            Package.AreaTableNamePattern, Package.Name, string.IsNullOrEmpty(_buildingName) ? "BLDG A" : _buildingName,
            Package.AreaTableBuildingValue, "2ND FLOOR");

        public string AreaTableLevel => LevelOf("areatable");

        public string AreaTableWhy => WhyOf("areatable");

        // ── Setup: which settings are still empty ────────────────────────────

        private int _openStep = 1;
        private bool _buildingHasScopeBox = true;

        public bool Step1Open { get => _openStep == 1; set => SetOpenStep(1, value); }

        public bool Step2Open { get => _openStep == 2; set => SetOpenStep(2, value); }

        public bool Step3Open { get => _openStep == 3; set => SetOpenStep(3, value); }

        /// <summary>One step open at a time. Closing the open one leaves none open.</summary>
        private void SetOpenStep(int step, bool open)
        {
            int next = open ? step : (_openStep == step ? 0 : _openStep);
            if (next == _openStep) return;
            _openStep = next;
            Raise(nameof(Step1Open));
            Raise(nameof(Step2Open));
            Raise(nameof(Step3Open));
        }

        public void OpenStep(int step) => SetOpenStep(step, true);

        /// <summary>Whether the selected building already carries a scope box (ZFA Step 1) — set by the parent.</summary>
        public bool BuildingHasScopeBox
        {
            get => _buildingHasScopeBox;
            set { if (_buildingHasScopeBox == value) return; _buildingHasScopeBox = value; RaiseSetup(); }
        }

        /// <summary>Every setting that is empty and matters, required ones first. Recomputed on demand, so it is never stale.</summary>
        public IReadOnlyList<SettingIssue> Issues
        {
            get
            {
                var list = new List<SettingIssue>();
                if (IsAreaPlans && string.IsNullOrEmpty(Package.AreaSchemeUniqueId))
                {
                    list.Add(new SettingIssue("scheme", "Area scheme", true, 1, "Needed to find the area plans. Without it nothing can be generated."));
                }
                if (Package.AddAreaTable && (string.IsNullOrEmpty(Package.AreaTableTemplateUniqueId)
                    || AreaTableOptions.All(o => o.UniqueId != Package.AreaTableTemplateUniqueId)))
                {
                    list.Add(new SettingIssue("areatable", "Table to copy", true, 1,
                        "The set adds an area table to each view, but no area schedule is picked to copy. Pick one, or turn the area table off."));
                }
                if (string.IsNullOrEmpty(Package.TitleBlockTypeUniqueId))
                {
                    list.Add(new SettingIssue("titleblock", "Titleblock", true, 2, "Decides the sheet size and every slot size. Without it nothing can be generated."));
                }
                if (string.IsNullOrEmpty(Package.ViewTemplateUniqueId))
                {
                    list.Add(new SettingIssue("template", "View template", false, 1, "Without a template, new views keep whatever their source view had."));
                }
                foreach (ParameterAssignmentViewModel row in ViewParameters.Where(r => r.IsActive && string.IsNullOrWhiteSpace(r.Value)))
                {
                    list.Add(new SettingIssue("viewparam", row.Name, false, 1, "The views will have no value in the Project Browser grouping."));
                }
                if (string.IsNullOrEmpty(Package.ScopeBoxUniqueId) && !_buildingHasScopeBox)
                {
                    list.Add(new SettingIssue("scopebox", "Scope box", false, 2, "Neither this set nor the building has one: views keep the full extent of their source and may look off-center."));
                }
                if (string.IsNullOrWhiteSpace(Package.SheetCollectionName))
                {
                    list.Add(new SettingIssue("collection", "Sheet collection", false, 2, "The sheets will not belong to any Sheet Collection."));
                }
                foreach (ParameterAssignmentViewModel row in SheetParameters.Where(r => r.IsActive && string.IsNullOrWhiteSpace(r.Value)))
                {
                    list.Add(new SettingIssue("sheetparam", row.Name, false, 2, "The sheets will have no value in the Project Browser grouping."));
                }
                return list.OrderByDescending(i => i.IsRequired).ThenBy(i => i.Step).ToList();
            }
        }

        public int RequiredCount => Issues.Count(i => i.IsRequired);

        public int RecommendedCount => Issues.Count(i => !i.IsRequired);

        public bool HasRequired => RequiredCount > 0;

        public int SetupTotal =>
            (IsAreaPlans ? 1 : 0) + (Package.AddAreaTable ? 1 : 0) + 3 + ViewParameters.Count(r => r.IsActive) + SheetParameters.Count(r => r.IsActive) + 1;

        public int SetupFilled => Math.Max(0, SetupTotal - Issues.Count);

        public double SetupPercent => SetupTotal == 0 ? 100 : 100.0 * SetupFilled / SetupTotal;

        public string SetupCaption => $"{SetupFilled} of {SetupTotal} settings filled";

        public string AreaSchemeLevel => LevelOf("scheme");
        public string TitleBlockLevel => LevelOf("titleblock");
        public string ViewTemplateLevel => LevelOf("template");
        public string ScopeBoxLevel => LevelOf("scopebox");
        public string CollectionLevel => LevelOf("collection");

        public string AreaSchemeWhy => WhyOf("scheme");
        public string TitleBlockWhy => WhyOf("titleblock");
        public string ViewTemplateWhy => WhyOf("template");
        public string ScopeBoxWhy => WhyOf("scopebox");
        public string CollectionWhy => WhyOf("collection");

        private string LevelOf(string key) => Issues.FirstOrDefault(i => i.Key == key)?.Level ?? string.Empty;

        private string WhyOf(string key) => Issues.FirstOrDefault(i => i.Key == key)?.Why ?? string.Empty;

        /// <summary>"Required", "Recommended" or "Complete" for step 1 or 2; the layout step has nothing to fill.</summary>
        private string StepState(int step)
        {
            IReadOnlyList<SettingIssue> mine = Issues;
            return mine.Any(i => i.Step == step && i.IsRequired) ? "Required"
                : mine.Any(i => i.Step == step) ? "Recommended" : "Complete";
        }

        private string StepTag(int step)
        {
            IReadOnlyList<SettingIssue> mine = Issues.Where(i => i.Step == step).ToList();
            int required = mine.Count(i => i.IsRequired), recommended = mine.Count - required;
            return required > 0 ? $"{required} required" : recommended > 0 ? $"{recommended} recommended" : "complete";
        }

        public string Step1State => StepState(1);
        public string Step2State => StepState(2);
        public string Step3State => "Complete";
        public string Step1Tag => StepTag(1);
        public string Step2Tag => StepTag(2);

        public string Step1Summary => (IsAreaPlans ? (AreaScheme?.Name ?? "No area scheme") : SourceCaption) + " · " + SourceCaptionSuffix;

        public string Step2Summary => (TitleBlock != null ? TitleBlock.Name.Split(':')[0].Trim() : "No titleblock") + " · " + NumberPrefix + StartNumber;

        public string Step3Summary => $"{Columns} × {Rows} per sheet" + (Package.DrawSeparatorLines ? " · separators drawn" : string.Empty);

        private string SourceCaptionSuffix => IsAreaPlans ? "area plans" : IsCeilingPlans ? "ceiling plans" : "floor plans";

        /// <summary>The list tag: Required, Recommended or Ready.</summary>
        public string ListStatus => HasRequired ? "Required" : RecommendedCount > 0 ? "Recommended" : "Ready";

        public string ListStatusCaption => HasRequired ? $"{RequiredCount} required"
            : RecommendedCount > 0 ? $"{RecommendedCount} recommended" : "ready";

        public string NumberCaption => NumberPrefix + StartNumber;

        private void RaiseSetup()
        {
            foreach (string name in new[]
            {
                nameof(Issues), nameof(RequiredCount), nameof(RecommendedCount), nameof(HasRequired), nameof(SetupTotal), nameof(SetupFilled),
                nameof(SetupPercent), nameof(SetupCaption), nameof(AreaSchemeLevel), nameof(TitleBlockLevel), nameof(ViewTemplateLevel),
                nameof(ScopeBoxLevel), nameof(CollectionLevel), nameof(AreaSchemeWhy), nameof(TitleBlockWhy), nameof(ViewTemplateWhy),
                nameof(ScopeBoxWhy), nameof(CollectionWhy), nameof(AreaTableLevel), nameof(AreaTableWhy), nameof(AreaTableNameSample), nameof(Step1State), nameof(Step2State), nameof(Step1Tag), nameof(Step2Tag),
                nameof(Step1Summary), nameof(Step2Summary), nameof(Step3Summary), nameof(ListStatus), nameof(ListStatusCaption), nameof(NumberCaption)
            })
            {
                Raise(name);
            }
        }

        private static NamedElementRef Find(IEnumerable<NamedElementRef> options, string uniqueId) =>
            string.IsNullOrEmpty(uniqueId) ? null : options.FirstOrDefault(o => o.UniqueId == uniqueId);
    }

    /// <summary>A position of the area table, as the selector shows it.</summary>
    internal sealed class AreaTablePlacementOption
    {
        public AreaTablePlacementOption(ARYE.OfficeTools.Core.Sheets.AreaTablePlacement placement, string label)
        {
            Placement = placement;
            Label = label;
        }

        public ARYE.OfficeTools.Core.Sheets.AreaTablePlacement Placement { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }

    /// <summary>An edge of the usable area, as the selector shows it.</summary>
    internal sealed class AreaTableEdgeOption
    {
        public AreaTableEdgeOption(ARYE.OfficeTools.Core.Sheets.AreaTableEdge edge, string label)
        {
            Edge = edge;
            Label = label;
        }

        public ARYE.OfficeTools.Core.Sheets.AreaTableEdge Edge { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }

    /// <summary>One setting that is still empty: which, how serious, why it matters and which step holds it.</summary>
    internal sealed class SettingIssue
    {
        public SettingIssue(string key, string label, bool isRequired, int step, string why)
        {
            Key = key;
            Label = label;
            IsRequired = isRequired;
            Step = step;
            Why = why;
        }

        public string Key { get; }

        public string Label { get; }

        public bool IsRequired { get; }

        public string Level => IsRequired ? "Required" : "Recommended";

        public int Step { get; }

        public string Why { get; }
    }

    /// <summary>One row of the cell-size table under the viewer.</summary>
    internal sealed class CellReadoutRow
    {
        public string Number { get; set; }

        public string Width { get; set; }

        public string Height { get; set; }
    }

    /// <summary>A view scale choice: 0 = recommend automatically, otherwise a standard denominator.</summary>
    internal sealed class ScaleOption
    {
        public ScaleOption(int denominator)
        {
            Denominator = denominator;
            Label = denominator <= 0 ? "Auto — largest standard scale that fits" : $"{ImperialLabel(denominator)}  (1:{denominator})";
        }

        public int Denominator { get; }

        public string Label { get; }

        public override string ToString() => Label;

        public override bool Equals(object obj) => obj is ScaleOption o && o.Denominator == Denominator;

        public override int GetHashCode() => Denominator;

        public static IReadOnlyList<ScaleOption> Standard()
        {
            var list = new List<ScaleOption> { new ScaleOption(0) };
            foreach (int d in ARYE.OfficeTools.Core.Sheets.SmartScaleAdvisor.StandardScaleDenominators) list.Add(new ScaleOption(d));
            return list;
        }

        private static string ImperialLabel(int d)
        {
            switch (d)
            {
                case 12: return "1\" = 1'-0\"";
                case 16: return "3/4\" = 1'-0\"";
                case 24: return "1/2\" = 1'-0\"";
                case 32: return "3/8\" = 1'-0\"";
                case 48: return "1/4\" = 1'-0\"";
                case 64: return "3/16\" = 1'-0\"";
                case 96: return "1/8\" = 1'-0\"";
                case 128: return "3/32\" = 1'-0\"";
                case 192: return "1/16\" = 1'-0\"";
                case 384: return "1/32\" = 1'-0\"";
                default: return $"1/{d}";
            }
        }
    }

    /// <summary>One browser-organization parameter of a set and the value to write.</summary>
    internal sealed class ParameterAssignmentViewModel : ViewModelBase
    {
        private readonly ParameterValueAssignment _assignment;
        private readonly Action _onEdited;

        public ParameterAssignmentViewModel(ParameterValueAssignment assignment, IEnumerable<string> options, bool isActive, Action onEdited)
        {
            _assignment = assignment;
            _onEdited = onEdited;
            IsActive = isActive;
            foreach (string o in options) Options.Add(o);
        }

        public string Name => _assignment.ParameterName;

        /// <summary>False when the stored parameter is no longer used by the active browser organization.</summary>
        public bool IsActive { get; }

        public string Note => IsActive ? string.Empty : "not used by the active browser organization — still written if it exists";

        /// <summary>"Recommended" while an active row is empty, so the editor can mark it.</summary>
        public string Level => IsActive && string.IsNullOrWhiteSpace(Value) ? "Recommended" : string.Empty;

        public ObservableCollection<string> Options { get; } = new ObservableCollection<string>();

        public string Value
        {
            get => _assignment.Value ?? string.Empty;
            set
            {
                string v = (value ?? string.Empty).Trim();
                if ((_assignment.Value ?? string.Empty) == v) return;
                _assignment.Value = v;
                Raise();
                Raise(nameof(Level));
                _onEdited?.Invoke();
            }
        }
    }

    /// <summary>One additional existing view of a set.</summary>
    internal sealed class AdditionalViewItemViewModel : ViewModelBase
    {
        private readonly Action _onEdited;

        public AdditionalViewItemViewModel(AdditionalViewRef reference, string onSheetNumber, Action onEdited)
        {
            Reference = reference;
            OnSheetNumber = onSheetNumber ?? string.Empty;
            _onEdited = onEdited;
        }

        public AdditionalViewRef Reference { get; }

        public string Name => Reference.ViewName ?? Reference.ViewUniqueId;

        public string TypeName => Reference.ViewTypeName ?? string.Empty;

        public string OnSheetNumber { get; }

        public bool IsMissing { get; set; }

        public string Status => IsMissing ? "⚠ no longer in the model" : OnSheetNumber.Length > 0 ? "on sheet " + OnSheetNumber : "not on a sheet";

        public bool OwnSheet
        {
            get => Reference.OwnSheet;
            set { if (Reference.OwnSheet == value) return; Reference.OwnSheet = value; Raise(); Raise(nameof(LayoutCaption)); _onEdited?.Invoke(); }
        }

        public string LayoutCaption => OwnSheet ? "own sheet (1×1)" : "next free slot";
    }
}
