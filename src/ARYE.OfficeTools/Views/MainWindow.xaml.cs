using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ARYE.OfficeTools.ViewModels;

namespace ARYE.OfficeTools.Views
{
    public partial class MainWindow : Window
    {
        private readonly ShellViewModel _viewModel;

        internal MainWindow(ShellViewModel viewModel, IntPtr revitMainWindowHandle)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            viewModel.ShowResultHandler = (header, summary) =>
                new ResultDialog(header, summary, this).ShowDialog();
            viewModel.ZoningMatrix.ShowCategoryEditorHandler = request =>
                CategoryEditorDialog.ShowEditor(request, this);

            // Parent to Revit so the window minimizes with it and never hides behind it.
            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this) { Owner = revitMainWindowHandle };
            }

            Loaded += (_, __) => viewModel.Refresh();
        }

        /// <summary>
        /// Opening Sync Ranges measures every range again, against the ranges
        /// as Step 1 shows them right now (saved or not), so the status is
        /// never older than the tab. SelectionChanged bubbles up from every
        /// ComboBox inside the tabs, hence the source check.
        /// </summary>
        private void OnStepChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != Steps || Steps.SelectedItem != SyncTab || _viewModel.IsBusy)
            {
                return;
            }
            _viewModel.Buildings.WriteInto(_viewModel.Model);
            _viewModel.AreaMapping.LoadFrom(_viewModel.Model);
        }
    }
}
