using System.Windows;

namespace ARYE.OfficeTools.Views
{
    public partial class ResultDialog : Window
    {
        public ResultDialog(string header, string summary, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            HeaderText.Text = header;
            SummaryText.Text = summary;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
