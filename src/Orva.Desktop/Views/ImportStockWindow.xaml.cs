using System;
using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using Orva.Services;

namespace Orva.Views
{
    public partial class ImportStockWindow : Window
    {
        private StockImportPreview _currentPreview;

        public ImportStockWindow()
        {
            InitializeComponent();
        }

        private void ChooseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select ORVA StockData.json file",
                Filter = "ORVA Stock File (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true) return;

            FilePathBox.Text = dialog.FileName;
            LoadPreview(dialog.FileName);
        }

        private void LoadPreview(string path)
        {
            ErrorText.Text = string.Empty;
            PreviewCard.Visibility = Visibility.Collapsed;
            WarningBanner.Visibility = Visibility.Collapsed;
            ConfirmDifferentShopCheck.Visibility = Visibility.Collapsed;
            ConfirmDifferentShopCheck.IsChecked = false;
            ImportButton.IsEnabled = false;

            _currentPreview = App.Services.StockDataImporter.PreviewImport(path);
            if (!_currentPreview.IsValid)
            {
                ErrorText.Text = string.Join("\n", _currentPreview.Errors);
                return;
            }

            PreviewCard.Visibility = Visibility.Visible;
            SourceShopText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Source shop: {0} (ID: {1})",
                _currentPreview.SourceShopName,
                _currentPreview.SourceShopId);

            FileMetaText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Format v{0} · Generated: {1}",
                _currentPreview.FormatVersion,
                _currentPreview.GeneratedAtUtc ?? "Unknown");

            MedCountText.Text = _currentPreview.MedicineCount.ToString("N0");
            BatchCountText.Text = _currentPreview.BatchCount.ToString("N0");
            ZeroedCountText.Text = _currentPreview.ZeroedBatchCount.ToString("N0");

            ChangeSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "New medicines: {0} · Matched medicines: {1} · New batches: {2} · Updated batch quantities: {3} · Batches zeroed: {4}. Historical invoices, purchases, and payments will not be deleted.",
                _currentPreview.NewMedicineCount,
                _currentPreview.MatchedMedicineCount,
                _currentPreview.NewBatchCount,
                _currentPreview.ChangedBatchCount,
                _currentPreview.ZeroedBatchCount);

            if (_currentPreview.Warnings.Count > 0)
            {
                WarningBanner.Visibility = Visibility.Visible;
                WarningText.Text = string.Join("\n\n", _currentPreview.Warnings);
            }

            if (_currentPreview.IsDifferentShop)
            {
                ConfirmDifferentShopCheck.Visibility = Visibility.Visible;
                ImportButton.IsEnabled = false;
            }
            else
            {
                ImportButton.IsEnabled = true;
            }
        }

        private void ConfirmDifferentShop_Changed(object sender, RoutedEventArgs e)
        {
            ImportButton.IsEnabled = _currentPreview != null
                && _currentPreview.IsValid
                && (!_currentPreview.IsDifferentShop || ConfirmDifferentShopCheck.IsChecked == true);
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreview == null || !_currentPreview.IsValid) return;

            var answer = MessageBox.Show(
                this,
                "Replace current inventory with the previewed stock file? A safety backup of your current shop database will be created first. Historical invoices and purchases will be preserved.",
                "ORVA — Confirm stock import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes) return;

            try
            {
                var result = App.Services.StockDataImporter.ExecuteImport(
                    _currentPreview.FilePath,
                    ConfirmDifferentShopCheck.IsChecked == true);

                if (!result.Succeeded)
                {
                    ErrorText.Text = result.ErrorMessage;
                    return;
                }

                string msg = string.Format(
                    CultureInfo.CurrentCulture,
                    "Inventory imported successfully.\n\nMedicines: {0}\nBatches: {1}\nStock movements recorded: {2}",
                    result.MedicinesImported,
                    result.BatchesImported,
                    result.StockMovementsRecorded);

                if (!string.IsNullOrWhiteSpace(result.SnapshotWarning))
                    msg += "\n\n" + result.SnapshotWarning;

                MessageBox.Show(this, msg, "ORVA — Stock imported", MessageBoxButton.OK,
                    result.SnapshotUpdated ? MessageBoxImage.Information : MessageBoxImage.Warning);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Stock import failed and was rolled back safely. " + ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
