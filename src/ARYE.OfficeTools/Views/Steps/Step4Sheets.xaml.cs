using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ARYE.OfficeTools.ViewModels;

namespace ARYE.OfficeTools.Views.Steps
{
    public partial class Step4Sheets : UserControl
    {
        public Step4Sheets()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SheetLayoutViewModel previous) previous.FocusRequested -= OnFocusRequested;
            if (e.NewValue is SheetLayoutViewModel current) current.FocusRequested += OnFocusRequested;
        }

        /// <summary>Scrolls to the row of an empty setting and puts the cursor in its first control.</summary>
        private void OnFocusRequested(string key)
        {
            // The step was just opened: wait for layout so the row exists on screen.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var row = (FindName("Row_" + key) ?? FindName("Rows_" + key)) as FrameworkElement;
                if (row == null) return;
                row.BringIntoView();
                FindFocusable(row)?.Focus();
            }));
        }

        private static Control FindFocusable(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is Control control && control.Focusable && control.IsTabStop && (control is ComboBox || control is TextBox))
                {
                    return control;
                }
                Control nested = FindFocusable(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
