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
    /// One floor range card in Step 1. Three kinds share the card shape:
    /// Single Floor (Kind=Atypical, one hand-authored level), Typical Range
    /// (Kind=Standard, contiguous From–To span replicated from a source level
    /// inside the span) and Duplex Range (Kind=Duplex, contiguous span paired
    /// bottom-up two-by-two with a source pair). Exclusivity is enforced by
    /// disabling foreign-owned levels in the selectors — never by eviction.
    /// </summary>
    internal sealed class FloorRangeItemViewModel : ViewModelBase
    {
        private readonly BuildingItemViewModel _building;
        private readonly Guid _id;
        private string _name;
        private int _fromIndex;
        private int _toIndex;
        private int _sourceIndex;
        private int _sourcePairIndex;
        private bool _isLegacyNonContiguous;
        private bool _syncing;

        private LevelOptionViewModel _selectedFrom;
        private LevelOptionViewModel _selectedTo;
        private LevelOptionViewModel _selectedLevel;
        private NamedElementRef _selectedSource;
        private PairOptionViewModel _selectedSourcePair;
        private Brush _colorBrush = Brushes.Transparent;

        public FloorRangeItemViewModel(
            BuildingItemViewModel building, FloorGroupKind kind, Guid id, string name,
            int fromIndex, int toIndex, int sourceIndex, int sourcePairIndex,
            bool isLegacyNonContiguous)
        {
            _building = building;
            Kind = kind;
            _id = id;
            _name = name;
            _fromIndex = Math.Min(fromIndex, toIndex);
            _toIndex = Math.Max(fromIndex, toIndex);
            _sourceIndex = Clamp(sourceIndex, _fromIndex, _toIndex);
            _sourcePairIndex = sourcePairIndex;
            _isLegacyNonContiguous = isLegacyNonContiguous;

            for (int i = 0; i < building.AllLevels.Count; i++)
            {
                NamedElementRef level = building.AllLevels[i];
                FromOptions.Add(new LevelOptionViewModel(level) { Index = i });
                ToOptions.Add(new LevelOptionViewModel(level) { Index = i });
                LevelOptions.Add(new LevelOptionViewModel(level) { Index = i });
            }

            MoveUpCommand = new RelayCommand(() => Shift(+1), () => CanShift(+1));
            MoveDownCommand = new RelayCommand(() => Shift(-1), () => CanShift(-1));
            DeleteCommand = new RelayCommand(() => _building.RemoveRange(this));
        }

        public FloorGroupKind Kind { get; }

        public Guid Id => _id;

        public bool IsSingle => Kind == FloorGroupKind.Atypical;

        public bool IsTypical => Kind == FloorGroupKind.Standard;

        public bool IsDuplex => Kind == FloorGroupKind.Duplex;

        public string KindBadge =>
            IsSingle ? "SINGLE FLOOR" : IsTypical ? "TYPICAL" : "DUPLEX";

        public string Name
        {
            get => _name;
            set
            {
                if (Set(ref _name, value))
                {
                    _building.NotifyStructureChanged();
                }
            }
        }

        public int FromIndex => _fromIndex;

        public int ToIndex => _toIndex;

        public int SpanCount => _toIndex - _fromIndex + 1;

        public RelayCommand MoveUpCommand { get; }

        public RelayCommand MoveDownCommand { get; }

        public RelayCommand DeleteCommand { get; }

        // ── Selector options ─────────────────────────────────────────────

        public ObservableCollection<LevelOptionViewModel> FromOptions { get; } =
            new ObservableCollection<LevelOptionViewModel>();

        public ObservableCollection<LevelOptionViewModel> ToOptions { get; } =
            new ObservableCollection<LevelOptionViewModel>();

        /// <summary>Single Floor only: the one level selector.</summary>
        public ObservableCollection<LevelOptionViewModel> LevelOptions { get; } =
            new ObservableCollection<LevelOptionViewModel>();

        /// <summary>Typical only: source candidates — the range's own levels.</summary>
        public ObservableCollection<NamedElementRef> SourceOptions { get; } =
            new ObservableCollection<NamedElementRef>();

        /// <summary>Duplex only: the auto-generated pairs to pick a source from.</summary>
        public ObservableCollection<PairOptionViewModel> PairOptions { get; } =
            new ObservableCollection<PairOptionViewModel>();

        public LevelOptionViewModel SelectedFrom
        {
            get => _selectedFrom;
            set
            {
                if (Set(ref _selectedFrom, value) && !_syncing && value != null)
                {
                    ApplyAnchoredSpan(value.Index, _toIndex, anchorIsLower: true);
                }
            }
        }

        public LevelOptionViewModel SelectedTo
        {
            get => _selectedTo;
            set
            {
                if (Set(ref _selectedTo, value) && !_syncing && value != null)
                {
                    ApplyAnchoredSpan(value.Index, _fromIndex, anchorIsLower: false);
                }
            }
        }

        public LevelOptionViewModel SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (Set(ref _selectedLevel, value) && !_syncing && value != null)
                {
                    ApplySpan(value.Index, value.Index);
                }
            }
        }

        public NamedElementRef SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (Set(ref _selectedSource, value) && !_syncing && value != null)
                {
                    _sourceIndex = _building.IndexOfLevel(value.UniqueId);
                    Raise(nameof(SecondaryLabel));
                    _building.NotifyDiagramChanged();
                }
            }
        }

        public PairOptionViewModel SelectedSourcePair
        {
            get => _selectedSourcePair;
            set
            {
                if (Set(ref _selectedSourcePair, value) && !_syncing && value != null)
                {
                    _sourcePairIndex = value.Index;
                    Raise(nameof(PairsSummary));
                    _building.NotifyDiagramChanged();
                }
            }
        }

        // ── Card labels ──────────────────────────────────────────────────

        public Brush ColorBrush
        {
            get => _colorBrush;
            private set => Set(ref _colorBrush, value);
        }

        public string SecondaryLabel
        {
            get
            {
                if (IsSingle)
                {
                    return "hand-authored · no propagation";
                }
                if (IsTypical)
                {
                    return "replicates ×" + (SpanCount - 1);
                }
                int pairs = SpanCount / 2;
                return pairs + (pairs == 1 ? " pair" : " pairs");
            }
        }

        public string PairsSummary
        {
            get
            {
                if (!IsDuplex)
                {
                    return string.Empty;
                }
                List<string> parts = new List<string>();
                int pairCount = SpanCount / 2;
                for (int i = 0; i < pairCount; i++)
                {
                    NamedElementRef lower = _building.AllLevels[_fromIndex + i * 2];
                    NamedElementRef upper = _building.AllLevels[_fromIndex + i * 2 + 1];
                    string part = lower.Name + "+" + upper.Name;
                    if (i == _sourcePairIndex)
                    {
                        part += " (source)";
                    }
                    parts.Add(part);
                }
                return parts.Count == 0
                    ? "No pairs — the range needs at least two levels."
                    : "Pairs generated automatically: " + string.Join(" · ", parts);
            }
        }

        public bool HasOddWarning => IsDuplex && SpanCount % 2 == 1;

        public string OddWarning
        {
            get
            {
                if (!HasOddWarning)
                {
                    return string.Empty;
                }
                NamedElementRef unpaired = _building.AllLevels[_toIndex];
                string text = SpanCount + (SpanCount == 1 ? " level" : " levels")
                    + " in range — " + unpaired.Name + " left unpaired.";
                bool canExtend = _toIndex + 1 < _building.AllLevels.Count;
                bool canShrink = _toIndex - 1 >= _fromIndex;
                if (canExtend && canShrink)
                {
                    text += " Extend to " + _building.AllLevels[_toIndex + 1].Name
                        + " or shrink to " + _building.AllLevels[_toIndex - 1].Name + ".";
                }
                else if (canExtend)
                {
                    text += " Extend to " + _building.AllLevels[_toIndex + 1].Name + ".";
                }
                else if (canShrink)
                {
                    text += " Shrink to " + _building.AllLevels[_toIndex - 1].Name + ".";
                }
                return text;
            }
        }

        public bool IsLegacyNonContiguous => _isLegacyNonContiguous;

        public string LegacyWarning => "legacy non-contiguous group — will become contiguous on save";

        // ── Diagram queries (used by the building's preview builder) ─────

        public bool Covers(int levelIndex) => levelIndex >= _fromIndex && levelIndex <= _toIndex;

        public bool IsSourceLevel(int levelIndex)
        {
            if (IsTypical)
            {
                return levelIndex == _sourceIndex;
            }
            if (IsDuplex)
            {
                return SpanCount >= 2 && levelIndex == _fromIndex + _sourcePairIndex * 2;
            }
            return false;
        }

        /// <summary>1-based pair number of a duplex member, or null (non-duplex / unpaired).</summary>
        public int? PairNumber(int levelIndex)
        {
            if (!IsDuplex || !Covers(levelIndex) || IsUnpaired(levelIndex))
            {
                return null;
            }
            return (levelIndex - _fromIndex) / 2 + 1;
        }

        /// <summary>True for the odd leftover level of a duplex span.</summary>
        public bool IsUnpaired(int levelIndex) =>
            IsDuplex && SpanCount % 2 == 1 && levelIndex == _toIndex;

        // ── Recompute plumbing ───────────────────────────────────────────

        internal void SetColor(Color color)
        {
            ColorBrush = new SolidColorBrush(color);
        }

        /// <summary>
        /// Re-derives option availability, selections and labels after any
        /// structural change in the building. A From/To/Level candidate is
        /// available whenever the level itself is free (or already ours) —
        /// never judged by the span it would form, otherwise a range parked on
        /// an isolated free level could never be moved past a neighbouring
        /// range (both ends blocked each other). The picked level is honoured
        /// and the other end snaps to fit; see <see cref="ApplyAnchoredSpan"/>.
        /// </summary>
        internal void RefreshFromOwnership()
        {
            _syncing = true;
            try
            {
                for (int i = 0; i < FromOptions.Count; i++)
                {
                    RefreshSingleOption(FromOptions[i], i);
                    RefreshSingleOption(ToOptions[i], i);
                    RefreshSingleOption(LevelOptions[i], i);
                }

                _selectedFrom = FromOptions[_fromIndex];
                _selectedTo = ToOptions[_toIndex];
                _selectedLevel = LevelOptions[_fromIndex];
                Raise(nameof(SelectedFrom));
                Raise(nameof(SelectedTo));
                Raise(nameof(SelectedLevel));

                if (IsTypical)
                {
                    SourceOptions.Clear();
                    for (int i = _fromIndex; i <= _toIndex; i++)
                    {
                        SourceOptions.Add(_building.AllLevels[i]);
                    }
                    _sourceIndex = Clamp(_sourceIndex, _fromIndex, _toIndex);
                    _selectedSource = _building.AllLevels[_sourceIndex];
                    Raise(nameof(SelectedSource));
                }

                if (IsDuplex)
                {
                    PairOptions.Clear();
                    int pairCount = SpanCount / 2;
                    for (int i = 0; i < pairCount; i++)
                    {
                        PairOptions.Add(new PairOptionViewModel(
                            i,
                            _building.AllLevels[_fromIndex + i * 2],
                            _building.AllLevels[_fromIndex + i * 2 + 1]));
                    }
                    _sourcePairIndex = pairCount == 0 ? 0 : Clamp(_sourcePairIndex, 0, pairCount - 1);
                    _selectedSourcePair = pairCount == 0 ? null : PairOptions[_sourcePairIndex];
                    Raise(nameof(SelectedSourcePair));
                }

                Raise(nameof(SecondaryLabel));
                Raise(nameof(PairsSummary));
                Raise(nameof(HasOddWarning));
                Raise(nameof(OddWarning));
                Raise(nameof(IsLegacyNonContiguous));
            }
            finally
            {
                _syncing = false;
            }
        }

        private void RefreshSingleOption(LevelOptionViewModel option, int candidate)
        {
            FloorRangeItemViewModel owner = _building.OwnerAt(candidate);
            bool available = owner == null || ReferenceEquals(owner, this);
            option.Update(available, available ? null : owner.Name);
        }

        /// <summary>
        /// The level the user just picked is always kept in the span. If the
        /// other end now sits on the wrong side (From above To, or To below
        /// From) the span collapses to the picked level and the user sets the
        /// other end next. Otherwise the span extends from the picked level
        /// toward the other end, stopping short of the first level owned by
        /// another range — so the range can never swallow a foreign level, and
        /// it can always be relocated past one.
        /// </summary>
        private void ApplyAnchoredSpan(int anchor, int wantedOther, bool anchorIsLower)
        {
            int reached = anchor;
            bool wrongSide = anchorIsLower ? wantedOther < anchor : wantedOther > anchor;
            if (!wrongSide)
            {
                int step = wantedOther >= anchor ? 1 : -1;
                for (int i = anchor + step; step > 0 ? i <= wantedOther : i >= wantedOther; i += step)
                {
                    FloorRangeItemViewModel owner = _building.OwnerAt(i);
                    if (owner != null && !ReferenceEquals(owner, this))
                    {
                        break;
                    }
                    reached = i;
                }
            }
            ApplySpan(anchor, reached);
        }

        private void ApplySpan(int a, int b)
        {
            int lo = Math.Min(a, b);
            int hi = Math.Max(a, b);
            if (lo == _fromIndex && hi == _toIndex)
            {
                // No structural change, but the combos may hold a swapped
                // selection that needs syncing back.
                RefreshFromOwnership();
                return;
            }
            _fromIndex = lo;
            _toIndex = hi;
            _sourceIndex = Clamp(_sourceIndex, lo, hi);
            _isLegacyNonContiguous = false;
            _building.NotifyStructureChanged();
        }

        private bool CanShift(int delta)
        {
            int newFrom = _fromIndex + delta;
            int newTo = _toIndex + delta;
            if (newFrom < 0 || newTo >= _building.AllLevels.Count)
            {
                return false;
            }
            // Shifting by one only enters one new level: above To going up,
            // below From going down. It must not be owned by another range.
            int entering = delta > 0 ? newTo : newFrom;
            FloorRangeItemViewModel owner = _building.OwnerAt(entering);
            return owner == null || ReferenceEquals(owner, this);
        }

        private void Shift(int delta)
        {
            _fromIndex += delta;
            _toIndex += delta;
            _sourceIndex = Clamp(_sourceIndex + delta, _fromIndex, _toIndex);
            _isLegacyNonContiguous = false;
            _building.NotifyStructureChanged();
        }

        private static int Clamp(int value, int lo, int hi) =>
            value < lo ? lo : value > hi ? hi : value;

        // ── Persistence ──────────────────────────────────────────────────

        public FloorGroup ToGroup()
        {
            List<LevelRef> span = SpanLevelRefs();
            switch (Kind)
            {
                case FloorGroupKind.Atypical:
                    return new FloorGroup
                    {
                        Id = _id,
                        Name = Name,
                        Kind = FloorGroupKind.Atypical,
                        SourceLevel = span[0]
                    };
                case FloorGroupKind.Duplex:
                    return FloorRangeMath.BuildDuplexGroup(_id, Name, span, _sourcePairIndex);
                default:
                    return FloorRangeMath.BuildStandardGroup(
                        _id, Name, span, ToLevelRef(_building.AllLevels[_sourceIndex]));
            }
        }

        /// <summary>Single Floor only: the exclusion-list entry that must ALSO
        /// be written to <see cref="BuildingDefinition.AtypicalLevels"/>.</summary>
        public LevelRef ToAtypicalLevelRef() => ToLevelRef(_building.AllLevels[_fromIndex]);

        private List<LevelRef> SpanLevelRefs()
        {
            var span = new List<LevelRef>();
            for (int i = _fromIndex; i <= _toIndex; i++)
            {
                span.Add(ToLevelRef(_building.AllLevels[i]));
            }
            return span;
        }

        private static LevelRef ToLevelRef(NamedElementRef level) => new LevelRef
        {
            UniqueId = level.UniqueId,
            Name = level.Name,
            ElevationFt = level.ElevationFt
        };

        public override string ToString() => Name;
    }

    /// <summary>
    /// One level entry in a From/To/Level ComboBox. Disabled (with the owning
    /// range named in the text) when picking it would cover a level that
    /// belongs to another range.
    /// </summary>
    internal sealed class LevelOptionViewModel : ViewModelBase
    {
        private bool _isAvailable = true;
        private string _ownerName;

        public LevelOptionViewModel(NamedElementRef level)
        {
            Level = level;
        }

        public NamedElementRef Level { get; }

        /// <summary>Position of the level in the ascending-by-elevation level list.</summary>
        public int Index { get; internal set; }

        public bool IsAvailable
        {
            get => _isAvailable;
            private set => Set(ref _isAvailable, value);
        }

        public string OwnerName
        {
            get => _ownerName;
            private set => Set(ref _ownerName, value);
        }

        public string DisplayText => IsAvailable || string.IsNullOrEmpty(OwnerName)
            ? Level.Name
            : Level.Name + "  — in " + OwnerName;

        internal void Update(bool isAvailable, string ownerName)
        {
            IsAvailable = isAvailable;
            OwnerName = ownerName;
            Raise(nameof(DisplayText));
        }
    }

    /// <summary>One auto-generated duplex pair offered as source-pair candidate.</summary>
    internal sealed class PairOptionViewModel
    {
        public PairOptionViewModel(int index, NamedElementRef lower, NamedElementRef upper)
        {
            Index = index;
            Lower = lower;
            Upper = upper;
        }

        public int Index { get; }

        public NamedElementRef Lower { get; }

        public NamedElementRef Upper { get; }

        public string Display => Lower.Name + " + " + Upper.Name;

        public override string ToString() => Display;
    }
}
