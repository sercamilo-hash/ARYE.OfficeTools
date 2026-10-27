using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.Infrastructure;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>Step 1 — buildings and their floor ranges.</summary>
    internal sealed class BuildingsViewModel : ViewModelBase
    {
        private readonly ShellViewModel _shell;
        private BuildingItemViewModel _selectedBuilding;
        private CopySourceOption _selectedCopySource;

        public BuildingsViewModel(ShellViewModel shell)
        {
            _shell = shell;
            AddBuildingCommand = new RelayCommand(AddBuilding);
            RemoveBuildingCommand = new RelayCommand(RemoveBuilding, () => SelectedBuilding != null);
            RefreshCopySources();
        }

        public ObservableCollection<BuildingItemViewModel> Buildings { get; } =
            new ObservableCollection<BuildingItemViewModel>();

        /// <summary>"Start blank" plus one entry per existing building — what a
        /// new building is seeded from when Add is pressed.</summary>
        public ObservableCollection<CopySourceOption> CopySources { get; } =
            new ObservableCollection<CopySourceOption>();

        public RelayCommand AddBuildingCommand { get; }

        public RelayCommand RemoveBuildingCommand { get; }

        public BuildingItemViewModel SelectedBuilding
        {
            get => _selectedBuilding;
            set => Set(ref _selectedBuilding, value);
        }

        public CopySourceOption SelectedCopySource
        {
            get => _selectedCopySource;
            set => Set(ref _selectedCopySource, value);
        }

        public RevitContext Context => _shell.Context;

        public void LoadFrom(ZfaProjectModel model, RevitContext context)
        {
            Buildings.Clear();
            foreach (BuildingDefinition definition in model.Buildings)
            {
                Buildings.Add(new BuildingItemViewModel(definition, context));
            }
            SelectedBuilding = Buildings.FirstOrDefault();
            RefreshCopySources();
            Raise(nameof(Context));
        }

        public void WriteInto(ZfaProjectModel model)
        {
            model.Buildings = Buildings.Select(b => b.ToDefinition()).ToList();
        }

        private void AddBuilding()
        {
            string name = "BLDG " + (char)('A' + Buildings.Count);
            BuildingItemViewModel source = SelectedCopySource?.Building;

            BuildingDefinition definition;
            if (source == null)
            {
                definition = new BuildingDefinition { Name = name };
            }
            else
            {
                // ToDefinition() hands back fresh lists, so only identities and
                // the per-building bits need resetting: a copied building gets
                // its own Ids (they key propagation tags and sheet packages) and
                // starts without a scope box — it sits somewhere else on the lot.
                // Area scheme and every floor range come across as-is.
                definition = source.ToDefinition();
                definition.Id = Guid.NewGuid();
                definition.Name = name;
                definition.ScopeBoxUniqueId = null;
                foreach (FloorGroup group in definition.Groups)
                {
                    group.Id = Guid.NewGuid();
                }
            }

            var item = new BuildingItemViewModel(definition, _shell.Context);
            Buildings.Add(item);
            SelectedBuilding = item;
            RefreshCopySources();
        }

        private void RemoveBuilding()
        {
            if (SelectedBuilding == null)
            {
                return;
            }
            int index = Buildings.IndexOf(SelectedBuilding);
            Buildings.Remove(SelectedBuilding);
            SelectedBuilding = Buildings.Count > 0
                ? Buildings[Math.Min(index, Buildings.Count - 1)]
                : null;
            RefreshCopySources();
        }

        private void RefreshCopySources()
        {
            BuildingItemViewModel keep = _selectedCopySource?.Building;
            CopySources.Clear();
            CopySources.Add(new CopySourceOption(null));
            foreach (BuildingItemViewModel building in Buildings)
            {
                CopySources.Add(new CopySourceOption(building));
            }
            SelectedCopySource =
                CopySources.FirstOrDefault(o => o.Building != null && ReferenceEquals(o.Building, keep))
                ?? CopySources[0];
        }
    }

    /// <summary>One entry of the "New building copies" selector.</summary>
    internal sealed class CopySourceOption : ViewModelBase
    {
        public CopySourceOption(BuildingItemViewModel building)
        {
            Building = building;
            if (building != null)
            {
                building.PropertyChanged += OnBuildingChanged;
            }
        }

        /// <summary>Null means "start blank".</summary>
        public BuildingItemViewModel Building { get; }

        public string Label => Building == null ? "— start blank —" : Building.Name;

        private void OnBuildingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BuildingItemViewModel.Name))
            {
                Raise(nameof(Label));
            }
        }

        public override string ToString() => Label;
    }
}
