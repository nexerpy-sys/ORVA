using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Orva.Domain;
using Orva.Infrastructure.Repositories;
using Orva.Domain.Billing;
using Orva.Services;
using Orva.Services.Billing;

namespace Orva.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly ShopRepository _shopRepository;
        private readonly TaxSettingsRepository _taxRepository;
        private ShopProfile _loadedProfile;
        private MedicineTaxConfigItem _selectedMedicine;
        private bool _suppressSelectionClear;

        public SettingsView(ShopRepository shopRepository, TaxSettingsRepository taxRepository)
        {
            if (shopRepository == null) throw new ArgumentNullException("shopRepository");
            if (taxRepository == null) throw new ArgumentNullException("taxRepository");

            _shopRepository = shopRepository;
            _taxRepository = taxRepository;

            InitializeComponent();
            StateComboBox.ItemsSource = IndianGstStateCatalog.AllStates;

            LoadShopProfile();
            LoadTaxSettings();
            RefreshMedicines();
            RefreshBackupStatus();
            LoadOperationalPrefs();
        }

        public event EventHandler DatabaseRestored;

        private void LoadShopProfile()
        {
            _loadedProfile = _shopRepository.GetProfile() ?? new ShopProfile { InvoicePrefix = "INV" };
            ShopNameBox.Text = _loadedProfile.ShopName ?? string.Empty;
            InvoicePrefixBox.Text = string.IsNullOrWhiteSpace(_loadedProfile.InvoicePrefix) ? "INV" : _loadedProfile.InvoicePrefix;
            ShopGstinBox.Text = _loadedProfile.Gstin ?? string.Empty;
            DrugLicenceBox.Text = _loadedProfile.DrugLicenceNumber ?? string.Empty;

            var matchedState = IndianGstStateCatalog.FindByCode(_loadedProfile.StateCode)
                ?? IndianGstStateCatalog.FindByName(_loadedProfile.StateName);

            StateComboBox.SelectedItem = matchedState;
            StateCodeBox.Text = matchedState != null ? matchedState.Code : (_loadedProfile.StateCode ?? string.Empty);
        }

        private void LoadTaxSettings()
        {
            var settings = _taxRepository.GetTaxSettings();

            SelectComboByTag(PricingModeBox, settings.PricingModeRaw);
            SelectComboByTag(SupplyTypeBox, settings.DefaultSupplyTypeRaw);
            SelectComboByTag(RoundOffBox, settings.RoundOffModeRaw);
            TaxConfiguredCheck.IsChecked = settings.IsConfigured;

            UpdateTaxStatusBadge(settings.IsConfigured);
        }

        private void UpdateTaxStatusBadge(bool configured)
        {
            if (configured)
            {
                TaxStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xDF, 0xF2, 0xE8));
                TaxStatusBadgeText.Foreground = (Brush)FindResource("Success");
                TaxStatusBadgeText.Text = LocalizationManager.Get("SettingsTaxConfigured");
            }
            else
            {
                TaxStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xE4, 0xDE));
                TaxStatusBadgeText.Foreground = (Brush)FindResource("Vermilion");
                TaxStatusBadgeText.Text = LocalizationManager.Get("SettingsTaxNotConfigured");
            }
        }

        private void StateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var state = StateComboBox.SelectedItem as IndianGstState;
            StateCodeBox.Text = state == null ? string.Empty : state.Code;
        }

        private void SaveShop_Click(object sender, RoutedEventArgs e)
        {
            ShopStatusText.Text = string.Empty;
            try
            {
                var selectedState = StateComboBox.SelectedItem as IndianGstState;
                if (selectedState == null)
                    throw new ArgumentException("Select the shop's State from the list so its 2-digit GST State Code is recorded.");

                var updated = new ShopProfile
                {
                    ShopId = _loadedProfile != null ? _loadedProfile.ShopId : null,
                    ShopName = ShopNameBox.Text,
                    LegalName = _loadedProfile != null ? _loadedProfile.LegalName : null,
                    Address = _loadedProfile != null ? _loadedProfile.Address : null,
                    Phone = _loadedProfile != null ? _loadedProfile.Phone : null,
                    Email = _loadedProfile != null ? _loadedProfile.Email : null,
                    StateName = selectedState.Name,
                    StateCode = selectedState.Code,
                    Gstin = ShopGstinBox.Text,
                    DrugLicenceNumber = DrugLicenceBox.Text,
                    InvoicePrefix = InvoicePrefixBox.Text
                };

                _shopRepository.SaveProfileWithAudit(updated);
                LoadShopProfile();
                ShopStatusText.Foreground = (Brush)FindResource("Success");
                ShopStatusText.Text = LocalizationManager.Get("SettingsSavedSuccess");
            }
            catch (Exception ex)
            {
                ShopStatusText.Foreground = (Brush)FindResource("Danger");
                ShopStatusText.Text = ex.Message;
            }
        }

        private void SaveTax_Click(object sender, RoutedEventArgs e)
        {
            TaxSaveMessageText.Text = string.Empty;
            try
            {
                string pricingMode = GetSelectedTag(PricingModeBox);
                string supplyType = GetSelectedTag(SupplyTypeBox);
                string roundOffMode = GetSelectedTag(RoundOffBox);

                if (string.IsNullOrEmpty(pricingMode))
                    throw new ArgumentException("Select a Tax Pricing Mode (Tax Inclusive or Tax Exclusive).");
                if (string.IsNullOrEmpty(supplyType))
                    throw new ArgumentException("Select a Default Supply Type (Intra-State or Inter-State).");
                if (string.IsNullOrEmpty(roundOffMode))
                    throw new ArgumentException("Select a Round-Off Mode.");

                bool configured = TaxConfiguredCheck.IsChecked == true;
                _taxRepository.SaveTaxSettings(configured, pricingMode, supplyType, roundOffMode);
                UpdateTaxStatusBadge(configured);

                TaxSaveMessageText.Foreground = (Brush)FindResource("Success");
                TaxSaveMessageText.Text = LocalizationManager.Get("SettingsSavedSuccess");
            }
            catch (Exception ex)
            {
                TaxSaveMessageText.Foreground = (Brush)FindResource("Danger");
                TaxSaveMessageText.Text = ex.Message;
            }
        }

        private void RefreshMedicines()
        {
            string query = MedicineSearchBox == null ? string.Empty : MedicineSearchBox.Text;
            MedicineTaxGrid.ItemsSource = _taxRepository.GetMedicinesForTaxConfig(query);
        }

        private void MedicineSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshMedicines();
        }

        private void MedicineTaxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedMedicine = MedicineTaxGrid.SelectedItem as MedicineTaxConfigItem;
            if (!_suppressSelectionClear) MedicineTaxMessageText.Text = string.Empty;

            if (_selectedMedicine == null)
            {
                SelectedMedicineLabel.Text = LocalizationManager.Get("SettingsSelectMedicinePrompt");
                MedicineHsnBox.Text = string.Empty;
                MedicineGstRateBox.Text = string.Empty;
                MedicineGstConfiguredCheck.IsChecked = false;
                SaveMedicineTaxButton.IsEnabled = false;
                return;
            }

            SelectedMedicineLabel.Text = _selectedMedicine.DisplayName + " (" + _selectedMedicine.StockUnitName + ")";
            MedicineHsnBox.Text = _selectedMedicine.HsnCode ?? string.Empty;
            MedicineGstRateBox.Text = (_selectedMedicine.GstRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            // Explicitly reflect stored configuration flag; never auto-enable merely because rate is 0.
            MedicineGstConfiguredCheck.IsChecked = _selectedMedicine.GstRateConfigured;
            SaveMedicineTaxButton.IsEnabled = true;
        }

        private void SaveMedicineTax_Click(object sender, RoutedEventArgs e)
        {
            MedicineTaxMessageText.Text = string.Empty;
            if (_selectedMedicine == null) return;

            try
            {
                decimal ratePercent;
                if (!decimal.TryParse((MedicineGstRateBox.Text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out ratePercent)
                    || ratePercent < 0m || ratePercent > 100m)
                {
                    throw new ArgumentException("Enter a valid GST percentage between 0 and 100.");
                }

                decimal scaled = ratePercent * 100m;
                if (scaled != decimal.Truncate(scaled))
                {
                    throw new ArgumentException("GST percentage can have at most two decimal places.");
                }

                int basisPoints = checked((int)scaled);
                bool configured = MedicineGstConfiguredCheck.IsChecked == true;

                _taxRepository.UpdateMedicineTaxConfig(
                    _selectedMedicine.MedicineId,
                    MedicineHsnBox.Text,
                    basisPoints,
                    configured);

                string savedId = _selectedMedicine.MedicineId;
                RefreshMedicines();

                MedicineTaxMessageText.Foreground = (Brush)FindResource("Success");
                MedicineTaxMessageText.Text = LocalizationManager.Get("SettingsSavedSuccess");

                // Re-select the updated medicine if still visible
                foreach (object item in MedicineTaxGrid.Items)
                {
                    var row = item as MedicineTaxConfigItem;
                    if (row != null && string.Equals(row.MedicineId, savedId, StringComparison.Ordinal))
                    {
                        _suppressSelectionClear = true;
                        try { MedicineTaxGrid.SelectedItem = row; }
                        finally { _suppressSelectionClear = false; }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MedicineTaxMessageText.Foreground = (Brush)FindResource("Danger");
                MedicineTaxMessageText.Text = ex.Message;
            }
        }


        private void RefreshBackupStatus()
        {
            if (App.Services == null || App.Services.AutomaticBackupCoordinator == null) return;
            var status = App.Services.AutomaticBackupCoordinator.GetStatus(DateTime.Now);
            LastBackupText.Text = string.IsNullOrWhiteSpace(status.LastSuccessLocalDate)
                ? "Not backed up yet"
                : status.LastSuccessLocalDate;

            if (!string.IsNullOrWhiteSpace(status.LastError))
            {
                BackupStatusText.Text = status.LastError;
                BackupStatusText.Foreground = (Brush)FindResource("Danger");
            }
            else if (status.IsDueToday)
            {
                BackupStatusText.Text = "Backup due today (keeps " + status.RetentionDays + " daily copies)";
                BackupStatusText.Foreground = (Brush)FindResource("Vermilion");
            }
            else
            {
                BackupStatusText.Text = "Up to date (keeps " + status.RetentionDays + " daily copies)";
                BackupStatusText.Foreground = (Brush)FindResource("Success");
            }

            BackupLocationText.Text = "Managed daily backup folder: " + status.DailyBackupDirectory;
        }

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            BackupActionMessageText.Text = string.Empty;
            var result = App.Services.AutomaticBackupCoordinator.RunBackupNow(DateTime.Now);
            RefreshBackupStatus();
            if (result.Succeeded)
            {
                BackupActionMessageText.Foreground = (Brush)FindResource("Success");
                BackupActionMessageText.Text = "Complete shop backup created and verified.";
            }
            else
            {
                BackupActionMessageText.Foreground = (Brush)FindResource("Danger");
                BackupActionMessageText.Text = result.ErrorMessage;
            }
        }

        private void ExportBackup_Click(object sender, RoutedEventArgs e)
        {
            BackupActionMessageText.Text = string.Empty;
            var dialog = new SaveFileDialog
            {
                Title = "Export Complete Shop Backup",
                Filter = "ORVA Shop Backup (*.shopbackup)|*.shopbackup",
                FileName = "ShopBackup.shopbackup",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            var result = App.Services.ShopBackupService.CreateBackupToPath(dialog.FileName, alsoUpdatePortableShopCopy: false);
            if (result.Succeeded)
            {
                BackupActionMessageText.Foreground = (Brush)FindResource("Success");
                BackupActionMessageText.Text = "Complete shop backup exported to: " + result.BackupFilePath;
            }
            else
            {
                BackupActionMessageText.Foreground = (Brush)FindResource("Danger");
                BackupActionMessageText.Text = result.ErrorMessage;
            }
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            BackupActionMessageText.Text = string.Empty;
            var dialog = new RestoreBackupWindow();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                var handler = DatabaseRestored;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private static void SelectComboByTag(ComboBox comboBox, string tag)
        {
            if (comboBox == null) return;
            comboBox.SelectedItem = null;
            if (string.IsNullOrWhiteSpace(tag)) return;

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i] as ComboBoxItem;
                if (item != null && string.Equals(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), tag.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private static string GetSelectedTag(ComboBox comboBox)
        {
            var item = comboBox != null ? comboBox.SelectedItem as ComboBoxItem : null;
            return item == null ? null : Convert.ToString(item.Tag, CultureInfo.InvariantCulture);
        }

        private void LoadOperationalPrefs()
        {
            if (App.Services == null) return;
            var format = App.Services.InvoicePrintService.GetDefaultPaperFormat();
            foreach (object item in DefaultPaperFormatCombo.Items)
            {
                var cbi = item as ComboBoxItem;
                if (cbi != null && string.Equals(Convert.ToString(cbi.Tag), format.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    DefaultPaperFormatCombo.SelectedItem = cbi;
                    break;
                }
            }
            BackupRetentionDaysBox.Text = App.Services.AutomaticBackupCoordinator.GetRetentionDays().ToString(CultureInfo.InvariantCulture);
            SupportPhonesBox.Text = App.Services.AppSettings.Get(SubscriptionService.SupportPhonesKey, string.Empty);
            SupportWhatsappBox.Text = App.Services.AppSettings.Get(SubscriptionService.SupportWhatsappKey, string.Empty);
            MerchantUpiIdBox.Text = App.Services.AppSettings.Get(SubscriptionService.UpiIdKey, string.Empty);
            MerchantUpiPayeeBox.Text = App.Services.AppSettings.Get(SubscriptionService.UpiPayeeKey, string.Empty);
        }

        private void SaveOperationalPrefs_Click(object sender, RoutedEventArgs e)
        {
            if (App.Services == null) return;
            try
            {
                var selected = DefaultPaperFormatCombo.SelectedItem as ComboBoxItem;
                InvoicePaperFormat fmt;
                if (selected != null && Enum.TryParse(Convert.ToString(selected.Tag), true, out fmt))
                {
                    App.Services.InvoicePrintService.SetDefaultPaperFormat(fmt);
                }

                int days;
                if (!int.TryParse((BackupRetentionDaysBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out days)
                    || days < 1 || days > 365)
                {
                    throw new ArgumentException("Backup retention must be between 1 and 365 days.");
                }
                App.Services.AutomaticBackupCoordinator.SetRetentionDays(days);
                App.Services.SubscriptionService.SaveSupportAndUpiSettings(
                    SupportPhonesBox.Text,
                    SupportWhatsappBox.Text,
                    MerchantUpiIdBox.Text,
                    MerchantUpiPayeeBox.Text);

                OperationalPrefsMessageText.Foreground = (Brush)FindResource("Success");
                OperationalPrefsMessageText.Text = LocalizationManager.Get("SettingsSavedSuccess");
            }
            catch (Exception ex)
            {
                OperationalPrefsMessageText.Foreground = (Brush)FindResource("Danger");
                OperationalPrefsMessageText.Text = ex.Message;
            }
        }
    }
}
