using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ARYE.OfficeTools.Core.Levels;
using ARYE.OfficeTools.Core.Model;

namespace ARYE.OfficeTools.ViewModels
{
    /// <summary>Backs the standalone "Generate levels" dialog — plans a run of
    /// evenly-spaced levels, previews collisions against the current model,
    /// and only touches Revit once the user confirms.</summary>
    internal sealed class GenerateLevelsViewModel : ViewModelBase
    {
        private readonly List<LevelRef> _existingLevels;

        private double _baseElevationFt;
        private int _count = 10;
        private double _typicalHeightFt = 10.0;
        private string _namePrefix = "L";
        private int _startNumber = 1;
        private int _namePadding = 2;
        private bool _createViews = true;
        private string _statusText;
        private bool _canGenerate;

        public GenerateLevelsViewModel(List<LevelRef> existingLevels)
        {
            _existingLevels = existingLevels ?? new List<LevelRef>();
            GenerateCommand = new RelayCommand(() => RequestClose?.Invoke(true), () => _canGenerate);
            Preview();
        }

        public double BaseElevationFt
        {
            get => _baseElevationFt;
            set { if (Set(ref _baseElevationFt, value)) Preview(); }
        }

        public int Count
        {
            get => _count;
            set { if (Set(ref _count, value)) Preview(); }
        }

        public double TypicalHeightFt
        {
            get => _typicalHeightFt;
            set { if (Set(ref _typicalHeightFt, value)) Preview(); }
        }

        public string NamePrefix
        {
            get => _namePrefix;
            set { if (Set(ref _namePrefix, value)) Preview(); }
        }

        public int StartNumber
        {
            get => _startNumber;
            set { if (Set(ref _startNumber, value)) Preview(); }
        }

        public int NamePadding
        {
            get => _namePadding;
            set { if (Set(ref _namePadding, value)) Preview(); }
        }

        /// <summary>Also create a Floor Plan view for each level created.</summary>
        public bool CreateViews
        {
            get => _createViews;
            set => Set(ref _createViews, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        public ObservableCollection<LevelPlanRowViewModel> PreviewRows { get; } =
            new ObservableCollection<LevelPlanRowViewModel>();

        /// <summary>Set after every Preview(); the command consumes whatever this
        /// holds when the user clicks Generate, so command and preview never disagree.</summary>
        public LevelPlanResult PlanResult { get; private set; }

        public RelayCommand GenerateCommand { get; }

        /// <summary>The window sets this to close itself with a DialogResult.</summary>
        public Action<bool> RequestClose { get; set; }

        private void Preview()
        {
            PreviewRows.Clear();
            _canGenerate = false;
            PlanResult = null;

            if (Count <= 0 || TypicalHeightFt <= 0)
            {
                StatusText = "Set a positive count and a positive floor-to-floor height.";
                return;
            }

            var request = new LevelGenerationRequest
            {
                BaseElevationFt = BaseElevationFt,
                Count = Count,
                TypicalHeightFt = TypicalHeightFt,
                NamePrefix = NamePrefix,
                StartNumber = StartNumber,
                NamePadding = NamePadding
            };

            PlanResult = BatchLevelPlanner.Plan(request, _existingLevels);

            foreach (PlannedLevel level in PlanResult.ToCreate)
            {
                PreviewRows.Add(new LevelPlanRowViewModel
                {
                    Name = level.Name,
                    ElevationLabel = level.ElevationFt.ToString("0.00") + " ft",
                    StatusText = "Will create",
                    IsSkipped = false
                });
            }
            foreach (LevelPlanIssue issue in PlanResult.Skipped)
            {
                PreviewRows.Add(new LevelPlanRowViewModel
                {
                    Name = issue.Level.Name,
                    ElevationLabel = issue.Level.ElevationFt.ToString("0.00") + " ft",
                    StatusText = issue.Kind == LevelPlanIssueKind.NameCollision
                        ? "Skipped — name already used by \"" + issue.ExistingLevelName + "\""
                        : "Skipped — elevation matches \"" + issue.ExistingLevelName + "\"",
                    IsSkipped = true
                });
            }

            _canGenerate = PlanResult.ToCreate.Count > 0;
            StatusText = PlanResult.ToCreate.Count + " level(s) to create" +
                (PlanResult.Skipped.Count > 0 ? ", " + PlanResult.Skipped.Count + " skipped (already in the model)." : ".");
        }
    }

    internal sealed class LevelPlanRowViewModel
    {
        public string Name { get; set; }

        public string ElevationLabel { get; set; }

        public string StatusText { get; set; }

        public bool IsSkipped { get; set; }
    }
}
