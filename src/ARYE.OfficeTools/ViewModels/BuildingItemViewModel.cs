using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Infrastructure;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>
    /// One building being edited in Step 1. Owns the ordered Floor Range list
    /// and the per-level ownership map that makes range assignment exclusive:
    /// a level owned by one range shows up disabled in every other range's
    /// selectors, so an overlap can never be picked in the first place.
    /// </summary>
    internal sealed class BuildingItemViewModel : ViewModelBase
    {
        /// <summary>
        /// Cycled by range position so each range gets a distinct, repeatable
        /// swatch — the same palette a user sees consistently across sessions.
        /// Internal: the Step 3 matrix reuses it to dot each level row with
        /// its Step 1 range color (index = group position in the definition).
        /// </summary>
        internal static readonly Color[] GroupPalette =
        {
            Color.FromRgb(0x25, 0x63, 0xEB), // blue
            Color.FromRgb(0x7C, 0x3A, 0xED), // violet
            Color.FromRgb(0xDB, 0x27, 0x77), // pink
            Color.FromRgb(0x05, 0x96, 0x69), // green
            Color.FromRgb(0x08, 0x91, 0xB2), // cyan
            Color.FromRgb(0x4F, 0x46, 0xE5), // indigo
            Color.FromRgb(0xEA, 0x58, 0x0C), // orange
            Color.FromRgb(0x65, 0xA3, 0x0D), // olive
        };

        private static readonly Color UnassignedColor = Color.FromRgb(0x94, 0xA3, 0xB8);
        private static readonly Color WarningColor = Color.FromRgb(0xB4, 0x50, 0x09);

        private readonly Guid _id;
        private string _name;
        private NamedElementRef _scopeBox;
        private NamedElementRef _areaScheme;
        private int _assignedCount;
        private int _unassignedCount;

        /// <summary>levelIndex → owning range (first claim in range order wins), or null.</summary>
        private FloorRangeItemViewModel[] _ownerByIndex;

        public BuildingItemViewModel(BuildingDefinition definition, RevitContext context)
        {
            _id = definition.Id;
            _name = definition.Name;
            Context = context;
            AllLevels = context.Levels;
            _ownerByIndex = new FloorRangeItemViewModel[AllLevels.Count];

            _scopeBox = context.ScopeBoxes.FirstOrDefault(s => s.UniqueId == definition.ScopeBoxUniqueId);
            _areaScheme = context.AreaSchemes.FirstOrDefault(s => s.UniqueId == definition.AreaSchemeUniqueId);
            ZfaSchemeUniqueIds = new List<string>(definition.ZfaSchemeUniqueIds ?? new List<string>());

            LoadRanges(definition);

            AddSingleCommand = new RelayCommand(
                () => AddRange(FloorGroupKind.Atypical), HasFreeLevel);
            AddTypicalCommand = new RelayCommand(
                () => AddRange(FloorGroupKind.Standard), HasFreeLevel);
            AddDuplexCommand = new RelayCommand(
                () => AddRange(FloorGroupKind.Duplex), HasFreeLevel);

            NotifyStructureChanged();
        }

        public RevitContext Context { get; }

        public Guid Id => _id;

        /// <summary>
        /// The ZFA schemes ticked in Step 2. Not edited on this card; carried
        /// so writing Step 1 back into the model never drops them.
        /// </summary>
        public List<string> ZfaSchemeUniqueIds { get; set; }

        /// <summary>The document's levels, ordered ascending by elevation.</summary>
        public List<NamedElementRef> AllLevels { get; }

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public NamedElementRef ScopeBox
        {
            get => _scopeBox;
            set => Set(ref _scopeBox, value);
        }

        public NamedElementRef AreaScheme
        {
            get => _areaScheme;
            set => Set(ref _areaScheme, value);
        }

        /// <summary>Ordered floor range cards: Single / Typical / Duplex.</summary>
        public ObservableCollection<FloorRangeItemViewModel> Ranges { get; } =
            new ObservableCollection<FloorRangeItemViewModel>();

        /// <summary>Color-coded, top-down level list — which range each level belongs to.</summary>
        public ObservableCollection<LevelRangeRowViewModel> LevelRangePreview { get; } =
            new ObservableCollection<LevelRangeRowViewModel>();

        public RelayCommand AddSingleCommand { get; }

        public RelayCommand AddTypicalCommand { get; }

        public RelayCommand AddDuplexCommand { get; }

        public int AssignedCount
        {
            get => _assignedCount;
            private set => Set(ref _assignedCount, value);
        }

        public int UnassignedCount
        {
            get => _unassignedCount;
            private set
            {
                if (Set(ref _unassignedCount, value))
                {
                    Raise(nameof(HasUnassigned));
                }
            }
        }

        public bool HasUnassigned => _unassignedCount > 0;

        // ── Ownership queries (used by every range's selectors) ──────────

        public FloorRangeItemViewModel OwnerAt(int levelIndex) =>
            levelIndex >= 0 && levelIndex < _ownerByIndex.Length ? _ownerByIndex[levelIndex] : null;

        public int IndexOfLevel(string uniqueId) =>
            AllLevels.FindIndex(l => l.UniqueId == uniqueId);

        // ── Range add / remove ───────────────────────────────────────────

        private bool HasFreeLevel() => _ownerByIndex.Any(o => o == null);

        private void AddRange(FloorGroupKind kind)
        {
            int from = Array.FindIndex(_ownerByIndex, o => o == null);
            if (from < 0)
            {
                return;
            }
            int to = from;
            if (kind != FloorGroupKind.Atypical)
            {
                // A multi-level range should start on the lowest run of at
                // least two free levels, not on an isolated free level (e.g. a
                // shaft-bottom datum under an already-assigned cellar) — that
                // would make the new card read "replicates ×0" for no reason.
                for (int i = 0; i + 1 < _ownerByIndex.Length; i++)
                {
                    if (_ownerByIndex[i] == null && _ownerByIndex[i + 1] == null)
                    {
                        from = i;
                        to = i + 1;
                        break;
                    }
                }
            }

            string name = kind == FloorGroupKind.Atypical
                ? AllLevels[from].Name
                : (kind == FloorGroupKind.Standard ? "Typical " : "Duplex ")
                    + (Ranges.Count(r => r.Kind == kind) + 1);

            var range = new FloorRangeItemViewModel(
                this, kind, Guid.NewGuid(), name, from, to,
                sourceIndex: from, sourcePairIndex: 0, isLegacyNonContiguous: false);
            Ranges.Add(range);
            NotifyStructureChanged();
        }

        public void RemoveRange(FloorRangeItemViewModel range)
        {
            if (Ranges.Remove(range))
            {
                NotifyStructureChanged();
            }
        }

        // ── Recompute plumbing ───────────────────────────────────────────

        /// <summary>
        /// Recomputes ownership, every range's selector availability and the
        /// diagram. Called after any structural change; this recompute-always
        /// approach replaces the old eviction-based claim registry.
        /// </summary>
        internal void NotifyStructureChanged()
        {
            _ownerByIndex = new FloorRangeItemViewModel[AllLevels.Count];
            foreach (FloorRangeItemViewModel range in Ranges)
            {
                for (int i = range.FromIndex; i <= range.ToIndex && i < _ownerByIndex.Length; i++)
                {
                    if (_ownerByIndex[i] == null)
                    {
                        _ownerByIndex[i] = range;
                    }
                }
            }

            for (int i = 0; i < Ranges.Count; i++)
            {
                Ranges[i].SetColor(GroupPalette[i % GroupPalette.Length]);
                Ranges[i].RefreshFromOwnership();
            }

            NotifyDiagramChanged();
        }

        /// <summary>Rebuilds only the level diagram (source/pair choices moved).</summary>
        internal void NotifyDiagramChanged()
        {
            LevelRangePreview.Clear();
            int assigned = 0;
            for (int i = AllLevels.Count - 1; i >= 0; i--)
            {
                NamedElementRef level = AllLevels[i];
                FloorRangeItemViewModel owner = _ownerByIndex[i];
                var row = new LevelRangeRowViewModel
                {
                    LevelName = level.Name,
                    ElevationLabel = level.ElevationFt.ToString("0.00") + " ft"
                };

                if (owner == null)
                {
                    row.Label = "— unassigned";
                    row.IsUnassigned = true;
                    row.ColorBrush = new SolidColorBrush(UnassignedColor);
                    row.RowBackground = Brushes.Transparent;
                }
                else
                {
                    assigned++;
                    Color color = ((SolidColorBrush)owner.ColorBrush).Color;
                    row.Label = string.IsNullOrWhiteSpace(owner.Name) ? owner.KindBadge : owner.Name;
                    row.ColorBrush = owner.ColorBrush;
                    row.RowBackground = Tint(color);
                    row.ShowSourceChip = owner.IsSourceLevel(i);
                    int? pairNumber = owner.PairNumber(i);
                    if (pairNumber.HasValue)
                    {
                        row.PairChipText = "P" + pairNumber.Value;
                        row.ShowPairChip = true;
                    }
                    if (owner.IsUnpaired(i))
                    {
                        row.IsWarning = true;
                        row.Label = row.Label + " · unpaired";
                        row.ColorBrush = new SolidColorBrush(WarningColor);
                        row.RowBackground = Tint(WarningColor);
                    }
                }
                LevelRangePreview.Add(row);
            }

            AssignedCount = assigned;
            UnassignedCount = AllLevels.Count - assigned;
        }

        private static Brush Tint(Color c) => new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B));

        // ── Persistence ──────────────────────────────────────────────────

        public BuildingDefinition ToDefinition()
        {
            return new BuildingDefinition
            {
                Id = _id,
                Name = Name,
                ScopeBoxUniqueId = ScopeBox?.UniqueId,
                AreaSchemeUniqueId = AreaScheme?.UniqueId,
                ZfaSchemeUniqueIds = new List<string>(ZfaSchemeUniqueIds ?? new List<string>()),
                Groups = Ranges.Select(r => r.ToGroup()).ToList(),
                // RangeSyncService uses AtypicalLevels as its exclusion
                // list, so every Single Floor is written BOTH as an Atypical
                // group and as an AtypicalLevels entry.
                AtypicalLevels = Ranges
                    .Where(r => r.IsSingle)
                    .Select(r => r.ToAtypicalLevelRef())
                    .ToList()
            };
        }

        /// <summary>
        /// Rebuilds range cards from the persisted FloorGroup shape. Old saves
        /// (atypical levels without a matching Atypical group, non-contiguous
        /// checkbox-era standard groups) load losslessly — the latter carry a
        /// warning chip because saving normalizes them to their From–To span.
        /// </summary>
        private void LoadRanges(BuildingDefinition definition)
        {
            var atypicalGroupLevelIds = new HashSet<string>();

            foreach (FloorGroup group in definition.Groups)
            {
                if (group.Kind == FloorGroupKind.Atypical)
                {
                    int index = group.SourceLevel == null ? -1 : IndexOfLevel(group.SourceLevel.UniqueId);
                    if (index < 0)
                    {
                        continue; // the level no longer exists in the model
                    }
                    atypicalGroupLevelIds.Add(group.SourceLevel.UniqueId);
                    Ranges.Add(new FloorRangeItemViewModel(
                        this, FloorGroupKind.Atypical, group.Id,
                        string.IsNullOrWhiteSpace(group.Name) ? AllLevels[index].Name : group.Name,
                        index, index, index, 0, isLegacyNonContiguous: false));
                    continue;
                }

                List<LevelRef> members = group.Kind == FloorGroupKind.Duplex
                    ? FloorRangeMath.CollectDuplexMembers(group)
                    : FloorRangeMath.CollectStandardMembers(group);
                List<int> indices = members
                    .Select(m => IndexOfLevel(m.UniqueId))
                    .Where(i => i >= 0)
                    .OrderBy(i => i)
                    .ToList();
                if (indices.Count == 0)
                {
                    continue; // none of the referenced levels exist any more
                }

                int from = indices[0];
                int to = indices[indices.Count - 1];
                // Legacy checkbox saves could hold gaps; the card shows the
                // full From–To span and flags that saving will fill it in.
                bool legacy = indices.Count != members.Count
                    || indices.Count != to - from + 1;

                int sourceIndex = group.SourceLevel == null ? from : IndexOfLevel(group.SourceLevel.UniqueId);
                if (sourceIndex < from || sourceIndex > to)
                {
                    sourceIndex = from;
                }

                int sourcePairIndex = 0;
                if (group.Kind == FloorGroupKind.Duplex && (sourceIndex - from) % 2 == 0)
                {
                    sourcePairIndex = (sourceIndex - from) / 2;
                }

                Ranges.Add(new FloorRangeItemViewModel(
                    this, group.Kind, group.Id, group.Name,
                    from, to, sourceIndex, sourcePairIndex, legacy));
            }

            // Old saves listed atypical levels without an Atypical group —
            // migrate each leftover into an auto-named Single card.
            foreach (LevelRef level in definition.AtypicalLevels)
            {
                if (level == null || atypicalGroupLevelIds.Contains(level.UniqueId))
                {
                    continue;
                }
                int index = IndexOfLevel(level.UniqueId);
                if (index < 0)
                {
                    continue;
                }
                atypicalGroupLevelIds.Add(level.UniqueId);
                Ranges.Add(new FloorRangeItemViewModel(
                    this, FloorGroupKind.Atypical, Guid.NewGuid(), AllLevels[index].Name,
                    index, index, index, 0, isLegacyNonContiguous: false));
            }
        }

        public override string ToString() => Name;
    }
}
