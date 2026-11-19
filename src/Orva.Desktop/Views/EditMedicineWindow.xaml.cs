using System;
using System.Globalization;
using System.Windows;
using Orva.Domain;

namespace Orva.Views
{
    public partial class EditMedicineWindow : Window
    {
        private readonly MedicineDetailsInput _loaded;

        public EditMedicineWindow(string medicineId)
        {
            InitializeComponent();
            _loaded = App.Services.InventoryRepository.GetMedicineDetails(medicineId);
            if (_loaded == null)
            {
                throw new InvalidOperationException("Medicine details could not be loaded.");
            }

            DisplayNameBox.Text = _loaded.DisplayName ?? string.Empty;
            GenericNameBox.Text = _loaded.GenericName ?? string.Empty;
            BrandNameBox.Text = _loaded.BrandName ?? string.Empty;
            StrengthBox.Text = _loaded.Strength ?? string.Empty;
            DosageFormBox.Text = _loaded.DosageForm ?? string.Empty;
            UnitBox.Text = _loaded.StockUnitName ?? string.Empty;
            ThresholdBox.Text = (_loaded.LowStockThresholdMilli / 1000m).ToString("0.###", CultureInfo.CurrentCulture);
            IsActiveCheck.IsChecked = _loaded.IsActive;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            try
            {
                decimal thresholdDec;
                if (!decimal.TryParse((ThresholdBox.Text ?? "0").Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out thresholdDec) || thresholdDec < 0m)
                    throw new ArgumentException("Enter a valid minimum stock threshold (0 or greater).");

                long thresholdMilli = checked((long)Math.Round(thresholdDec * 1000m, 0, MidpointRounding.AwayFromZero));

                App.Services.InventoryRepository.UpdateMedicineDetails(new MedicineDetailsInput
                {
                    MedicineId = _loaded.MedicineId,
                    DisplayName = DisplayNameBox.Text,
                    GenericName = GenericNameBox.Text,
                    BrandName = BrandNameBox.Text,
                    Strength = StrengthBox.Text,
                    DosageForm = DosageFormBox.Text,
                    StockUnitName = _loaded.StockUnitName,
                    LowStockThresholdMilli = thresholdMilli,
                    IsActive = IsActiveCheck.IsChecked == true
                });

                try
                {
                    App.Services.SnapshotWriter.WriteCurrentSnapshot();
                }
                catch
                {
                    // Snapshot remains marked dirty and can be refreshed later.
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
