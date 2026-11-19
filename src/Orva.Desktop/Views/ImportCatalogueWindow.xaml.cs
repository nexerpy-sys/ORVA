using System;
using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using Orva.Services;

namespace Orva.Views
{
    public partial class ImportCatalogueWindow : Window
    {
        private CatalogueImportPreview _preview;

        public ImportCatalogueWindow()
        {
            InitializeComponent();
        }

        private void Template_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save ORVA Medicine CSV Template",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = "ORVA_Medicine_Import_Template.csv"
            };
            if (dlg.ShowDialog(this) != true) return;
            try { MedicineCatalogueImporter.WriteTemplateCsv(dlg.FileName); } catch (Exception ex) { SummaryText.Text = "Could not save template: " + ex.Message; return; }
            MessageBox.Show(this, "CSV template saved to " + dlg.FileName, "ORVA", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ChooseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Medicine Catalogue CSV",
                Filter = "Excel & CSV files (*.csv;*.xlsx)|*.csv;*.xlsx|CSV File (*.csv)|*.csv|Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;

            FilePathBox.Text = dlg.FileName;
            ErrorText.Text = string.Empty;
            PreviewPanel.Visibility = Visibility.Collapsed;
            ImportButton.IsEnabled = false;

            _preview = App.Services.MedicineCatalogueImporter.PreviewCsv(dlg.FileName);
            if (!_preview.IsValid)
            {
                ErrorText.Text = string.Join("\n", _preview.Errors);
                return;
            }

            PreviewPanel.Visibility = Visibility.Visible;
            SummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Rows validated: {0} · New medicines: {1} · Existing medicines: {2} · Opening stock batches: {3}",
                _preview.Rows.Count,
                _preview.NewMedicineCount,
                _preview.ExistingMedicineCount,
                _preview.OpeningBatchRowCount);
            ImportButton.IsEnabled = true;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_preview == null || !_preview.IsValid) return;

            var res = App.Services.MedicineCatalogueImporter.ExecuteImport(
                _preview.FilePath,
                MarkGstConfiguredCheck.IsChecked == true);

            if (!res.Succeeded)
            {
                ErrorText.Text = res.ErrorMessage;
                return;
            }

            MessageBox.Show(
                this,
                string.Format(
                    CultureInfo.CurrentCulture,
                    "CSV import complete.\n\nNew medicines: {0}\nUpdated medicines: {1}\nOpening batches added/incremented: {2}",
                    res.MedicinesCreated,
                    res.MedicinesUpdated,
                    res.BatchesUpdatedOrCreated),
                "ORVA — Catalogue Imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
