using System;
using System.Windows;
using System.Windows.Interop;
using ARYE.OfficeTools.ViewModels;

namespace ARYE.OfficeTools.Views
{
    public partial class SheetDiagrammerWindow : Window
    {
        internal SheetDiagrammerWindow(SheetDiagrammerShellViewModel viewModel, IntPtr revitMainWindowHandle)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.ShowResultHandler = (header, summary) => new ResultDialog(header, summary, this).ShowDialog();
            viewModel.SheetLayout.PickViewsHandler = (views, already) => ViewPickerDialog.Pick(this, views, already);

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this) { Owner = revitMainWindowHandle };
            }
            Loaded += (_, __) => viewModel.Refresh();
        }
    }
}
