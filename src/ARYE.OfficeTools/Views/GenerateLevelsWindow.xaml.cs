using System.Windows;
using ARYE.OfficeTools.ViewModels;

namespace ARYE.OfficeTools.Views
{
    public partial class GenerateLevelsWindow : Window
    {
        internal GenerateLevelsWindow(GenerateLevelsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose = accepted =>
            {
                DialogResult = accepted;
                Close();
            };
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
