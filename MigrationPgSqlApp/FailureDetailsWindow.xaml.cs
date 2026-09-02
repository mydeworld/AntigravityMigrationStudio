using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using MigrationPgSqlApp.Models;
using MigrationPgSqlApp.Services;

namespace MigrationPgSqlApp
{
    public partial class FailureDetailsWindow : MetroWindow
    {
        private readonly List<DbObjectBase> _allFailures;
        private List<DbObjectBase> _filteredFailures;

        public FailureDetailsWindow(List<DbObjectBase> failures)
        {
            InitializeComponent();
            DataContext = this;
            _allFailures = failures ?? new List<DbObjectBase>();
            _filteredFailures = _allFailures;

            RefreshGrid();
        }

        public LanguageManager Lang => LanguageManager.Instance;

        private void RefreshGrid()
        {
            FailuresGrid.ItemsSource = null;
            FailuresGrid.ItemsSource = _filteredFailures;
            CountText.Text = $" ({_filteredFailures.Count} items)";

            if (_filteredFailures.Count > 0)
            {
                FailuresGrid.SelectedIndex = 0;
            }
            else
            {
                DetailText.Text = string.Empty;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text?.Trim().ToLower() ?? string.Empty;

            if (string.IsNullOrEmpty(query))
            {
                _filteredFailures = _allFailures;
            }
            else
            {
                _filteredFailures = _allFailures
                    .Where(f => f.Name.ToLower().Contains(query) || 
                                f.DisplayType.ToLower().Contains(query) || 
                                f.ErrorMessage.ToLower().Contains(query))
                    .ToList();
            }

            RefreshGrid();
        }

        private void FailuresGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FailuresGrid.SelectedItem is DbObjectBase selectedObj)
            {
                DetailText.Text = $"Object Type: {selectedObj.DisplayType}\r\nName: {selectedObj.Name}\r\nError Reason:\r\n{selectedObj.ErrorMessage}";
            }
            else
            {
                DetailText.Text = string.Empty;
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (FailuresGrid.SelectedItem is DbObjectBase selectedObj)
            {
                try
                {
                    Clipboard.SetText($"[{selectedObj.DisplayType}] {selectedObj.Name}\r\nError: {selectedObj.ErrorMessage}");
                    MessageBox.Show("Error information copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select an item to copy.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
