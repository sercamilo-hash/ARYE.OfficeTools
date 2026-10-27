using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ARYE.OfficeTools.Core.Model;
using ARYE.OfficeTools.ViewModels;

namespace ARYE.OfficeTools.Views
{
    /// <summary>
    /// "Categories…" modal: edits the project's configurable ZFA category
    /// list (add / rename / delete / set role). Writes its result back into
    /// the request bag; the caller persists it. Deleting a category never
    /// touches areas — they become Unclassified on the next harvest.
    /// </summary>
    public partial class CategoryEditorDialog : Window
    {
        private readonly CategoryEditorRequest _request;
        private readonly ObservableCollection<CategoryRow> _rows =
            new ObservableCollection<CategoryRow>();

        internal CategoryEditorDialog(CategoryEditorRequest request, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _request = request;
            foreach (CategoryDefinition category in request.Categories)
            {
                _rows.Add(new CategoryRow(category.Name, category.Role));
            }
            RowsControl.ItemsSource = _rows;
        }

        /// <summary>Shows the modal; true when the user saved (the request bag then carries the new list).</summary>
        internal static bool ShowEditor(CategoryEditorRequest request, Window owner)
        {
            var dialog = new CategoryEditorDialog(request, owner);
            return dialog.ShowDialog() == true;
        }

        private void OnAddRowClick(object sender, RoutedEventArgs e)
        {
            _rows.Add(new CategoryRow("", CategoryRole.Deduction));
        }

        private void OnDeleteRowClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CategoryRow row)
            {
                _rows.Remove(row);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string error = Validate();
            if (error != null)
            {
                ValidationText.Text = error;
                ValidationText.Visibility = Visibility.Visible;
                return;
            }

            _request.Categories = _rows
                .Select(r => new CategoryDefinition(r.Name.Trim(), r.SelectedRole.Role))
                .ToList();
            DialogResult = true;
            Close();
        }

        private string Validate()
        {
            if (_rows.Count == 0)
            {
                return "The list needs at least one category — otherwise every area is Unclassified.";
            }
            if (_rows.Any(r => string.IsNullOrWhiteSpace(r.Name)))
            {
                return "Every category needs a name — it is the exact 'ZFA Category' value on the areas.";
            }
            List<string> duplicates = _rows
                .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                return "Duplicate name(s): " + string.Join(", ", duplicates)
                    + ". Matching is case-insensitive, so each name must be unique.";
            }
            return null;
        }

        /// <summary>One editable line of the list.</summary>
        private sealed class CategoryRow : ViewModelBase
        {
            private string _name;
            private RoleChoice _selectedRole;

            public CategoryRow(string name, CategoryRole role)
            {
                _name = name;
                _selectedRole = RoleOptions.First(o => o.Role == role);
            }

            public string Name
            {
                get => _name;
                set => Set(ref _name, value);
            }

            public IReadOnlyList<RoleChoice> RoleOptions { get; } = RoleChoice.All;

            public RoleChoice SelectedRole
            {
                get => _selectedRole;
                set => Set(ref _selectedRole, value);
            }
        }

        private sealed class RoleChoice
        {
            public static readonly IReadOnlyList<RoleChoice> All = new[]
            {
                new RoleChoice(CategoryRole.MainGross, "Main Gross"),
                new RoleChoice(CategoryRole.CommercialGross, "Commercial Gross"),
                new RoleChoice(CategoryRole.Deduction, "Deduction"),
                new RoleChoice(CategoryRole.Exempt, "Exempt")
            };

            private RoleChoice(CategoryRole role, string display)
            {
                Role = role;
                Display = display;
            }

            public CategoryRole Role { get; }

            public string Display { get; }

            public override string ToString() => Display;
        }
    }
}
