using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Orva.Domain;

namespace Orva.Views
{
    public partial class AddStockWindow : Window
    {
        public AddStockWindow(string prefillMedicineName = null)
        {
            InitializeComponent();
            bool hasPrefill = !string.IsNullOrWhiteSpace(prefillMedicineName);
            if (hasPrefill)
            {
                MedicineNameBox.Text = prefillMedicineName.Trim();
            }
            Height = Math.Min(760, SystemParameters.WorkArea.Height - 32);
            Width = Math.Min(760, SystemParameters.WorkArea.Width - 48);
            var months = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
            for (var i = 0; i < 12; i++) ExpiryMonthBox.Items.Add(new ComboBoxItem { Content = months[i], Tag = i + 1 });
            var firstYear = DateTime.Now.Year;
            for (var year = firstYear; year <= firstYear + 12; year++) ExpiryYearBox.Items.Add(year.ToString(CultureInfo.InvariantCulture));
            Loaded += (s, e) => { if (hasPrefill) { MedicineName_LostFocus(this, new RoutedEventArgs()); BatchNumberBox.Focus(); } else MedicineNameBox.Focus(); };
        }

        private void MedicineName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UnitBox.IsEnabled = true;
        }

        private void MedicineName_LostFocus(object sender, RoutedEventArgs e)
        {
            var existingUnit = App.Services.InventoryRepository.GetStockUnitForMedicine(MedicineNameBox.Text);
            if (!string.IsNullOrWhiteSpace(existingUnit))
            {
                UnitBox.Text = existingUnit;
                UnitBox.IsEnabled = false;
            }
            else
            {
                UnitBox.IsEnabled = true;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(MedicineNameBox.Text)) throw new ArgumentException("Enter the medicine name.");
                if (string.IsNullOrWhiteSpace(BatchNumberBox.Text)) throw new ArgumentException("Enter the batch number.");
                var monthItem = ExpiryMonthBox.SelectedItem as ComboBoxItem;
                if (monthItem == null || ExpiryYearBox.SelectedItem == null) throw new ArgumentException("Choose the expiry month and year.");

                var quantity = ParseQuantity(QuantityBox.Text, "quantity");
                var purchasePrice = ParseMoney(PurchasePriceBox.Text, "purchase price");
                var mrp = ParseMoney(MrpBox.Text, "MRP");
                var unit = (UnitBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Choose the stock unit.");

                var documentItem = DocumentTypeBox.SelectedItem as ComboBoxItem;
                var documentType = documentItem == null ? "NO_DOCUMENT" : Convert.ToString(documentItem.Tag, CultureInfo.InvariantCulture);
                var expiryMonth = Convert.ToInt32(monthItem.Tag, CultureInfo.InvariantCulture);
                var expiryYear = Convert.ToInt32(ExpiryYearBox.SelectedItem, CultureInfo.InvariantCulture);

                var result = App.Services.StockEntryService.ReceiveStock(new StockEntryInput
                {
                    MedicineName = MedicineNameBox.Text,
                    StockUnitName = unit,
                    BatchNumber = BatchNumberBox.Text,
                    ExpiryYearMonth = expiryYear * 100 + expiryMonth,
                    QuantityMilli = quantity,
                    PurchasePricePaise = purchasePrice,
                    MrpPaise = mrp,
                    SupplierName = SupplierBox.Text,
                    SupplierLicence = SupplierLicenceBox.Text,
                    DocumentType = documentType,
                    DocumentReference = DocumentReferenceBox.Text
                });

                var message = "Stock saved successfully.\n\n" + result.MedicineName + " · Batch " + result.BatchNumber;
                if (result.SnapshotUpdated)
                    message += "\n\nThe portable StockData.json file has also been updated.";
                else
                    message += "\n\n" + result.SnapshotWarning;

                string doneText = TryFindResource("StockSavedDone") as string;
            OrvaConfirmDialog.Show(this, "ORVA — Stock saved", message,
                !result.SnapshotUpdated, string.IsNullOrEmpty(doneText) ? "Done" : doneText);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }

        private static long ParseQuantity(string text, string label)
        {
            decimal value;
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) || value <= 0)
                throw new ArgumentException("Enter a valid " + label + " greater than zero.");
            if (value > 999999999m) throw new ArgumentException("The " + label + " is too large.");
            return checked((long)Math.Round(value * 1000m, 0, MidpointRounding.AwayFromZero));
        }

        private static long ParseMoney(string text, string label)
        {
            decimal value;
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) || value < 0)
                throw new ArgumentException("Enter a valid " + label + ".");
            if (value > 999999999m) throw new ArgumentException("The " + label + " is too large.");
            return checked((long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}