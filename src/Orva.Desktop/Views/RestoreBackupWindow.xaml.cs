using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Orva.Services;

namespace Orva.Views
{
    public partial class RestoreBackupWindow : Window
    {
        private ShopBackupPreview _preview;
        private bool _isBusy;

        public RestoreBackupWindow()
        {
            InitializeComponent();
        }

        private void ChooseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select ORVA ShopBackup.shopbackup file",
                Filter = "ORVA Shop Backup (*.shopbackup)|*.shopbackup|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true) return;

            FilePathBox.Text = dialog.FileName;
            LoadPreview(dialog.FileName);
        }

        private void LoadPreview(string filePath)
        {
            ErrorText.Text = string.Empty;
            PreviewCard.Visibility = Visibility.Collapsed;
            DifferentShopBanner.Visibility = Visibility.Collapsed;
            ConfirmReplaceCheck.IsChecked = false;
            RestoreButton.IsEnabled = false;

            _preview = App.Services.ShopBackupService.PreviewRestore(filePath);
            if (!_preview.IsValid || _preview.Manifest == null)
            {
                ErrorText.Text = string.Join("\n", _preview.Errors);
                return;
            }

            var m = _preview.Manifest;
            PreviewCard.Visibility = Visibility.Visible;
            SourceShopText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Backup shop: {0} (ID: {1})",
                m.ShopName,
                m.ShopId);

            BackupMetaText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Created: {0} · ORVA v{1} · Schema v{2}",
                m.BackupLocalText ?? m.BackupUtc,
                m.ApplicationVersion,
                m.DatabaseSchemaVersion);

            RecordCountsText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Medicines: {0:N0} · Batches: {1:N0} · Invoices: {2:N0} · Purchases: {3:N0} · Sales Returns: {4:N0}",
                m.MedicineCount,
                m.BatchCount,
                m.InvoiceCount,
                m.PurchaseCount,
                m.ReturnCount);

            if (_preview.IsDifferentShop)
            {
                DifferentShopBanner.Visibility = Visibility.Visible;
                DifferentShopText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "This backup belongs to a different shop ({0}) than the current installation ({1}). Restoring will completely replace the current local shop database after creating a safety backup.",
                    m.ShopName,
                    _preview.TargetShopName ?? "Current shop");
            }
        }

        private void ConfirmReplace_Changed(object sender, RoutedEventArgs e)
        {
            RestoreButton.IsEnabled = _preview != null && _preview.IsValid && ConfirmReplaceCheck.IsChecked == true;
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || _preview == null || !_preview.IsValid) return;

            bool confirmed = OrvaConfirmDialog.Show(
                this,
                "ORVA — Confirm complete restore",
                "Replace the entire local shop database with this backup? ORVA will create a safety backup of your current shop first and roll back automatically if anything goes wrong.",
                true,
                "Restore Backup",
                "Cancel");

            if (!confirmed) return;

            _isBusy = true;
            RestoreButton.IsEnabled = false;
            ErrorText.Text = "Restoring shop database…";
            string backupPath = _preview.BackupFilePath;
            try
            {
                var result = await Task.Run(() => App.Services.ShopBackupService.ExecuteRestore(backupPath));
                if (!IsLoaded) return;
                if (!result.Succeeded)
                {
                    ErrorText.Text = result.ErrorMessage;
                    return;
                }

                string msg = "Shop database restored successfully for " + (result.RestoredShopName ?? "your shop") + ".";
                if (!string.IsNullOrWhiteSpace(result.SnapshotWarning))
                    msg += "\n\n" + result.SnapshotWarning;

                OrvaConfirmDialog.Show(this, "ORVA — Restore complete", msg, false, "Done");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                if (IsLoaded) ErrorText.Text = "Restore failed: " + ex.Message;
            }
            finally
            {
                _isBusy = false;
                if (IsLoaded) RestoreButton.IsEnabled = _preview != null && _preview.IsValid && ConfirmReplaceCheck.IsChecked == true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
