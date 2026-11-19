using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orva.Domain;

namespace Orva.Views
{
    public partial class StockAdjustmentWindow : Window
    {
        private readonly InventoryRow _row;

        public StockAdjustmentWindow(InventoryRow row)
        {
            if (row == null) throw new ArgumentNullException("row");
            _row = row;

            InitializeComponent();
            BatchSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} · Batch {1} · Exp {2}",
                row.MedicineName,
                row.BatchNumber,
                row.Expiry);

            CurrentQtyText.Text = row.Quantity + " " + row.Unit;
            CountedQtyBox.Text = row.Quantity;
            CountedQtyBox.Focus();
            CountedQtyBox.SelectAll();
        }

        private void CountedQtyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            long parsed;
            if (!TryParseNonNegativeQuantity(CountedQtyBox.Text, out parsed))
            {
                DifferenceText.Text = "—";
                DifferenceText.Foreground = (Brush)FindResource("Muted");
                return;
            }

            long diff = parsed - _row.QuantityMilli;
            string sign = diff > 0 ? "+" : string.Empty;
            DifferenceText.Text = sign + (diff / 1000m).ToString("0.###", CultureInfo.InvariantCulture) + " " + _row.Unit;
            DifferenceText.Foreground = diff == 0
                ? (Brush)FindResource("Muted")
                : (diff > 0 ? (Brush)FindResource("Success") : (Brush)FindResource("Vermilion"));
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            try
            {
                long counted;
                if (!TryParseNonNegativeQuantity(CountedQtyBox.Text, out counted))
                    throw new ArgumentException("Enter a valid quantity (0 or greater, up to 3 decimal places).");

                var result = App.Services.StockAdjustmentService.AdjustBatchQuantity(new StockAdjustmentInput
                {
                    BatchId = _row.BatchId,
                    CountedQuantityMilli = counted,
                    Reason = ReasonBox.Text
                });

                string msg = string.Format(
                    CultureInfo.CurrentCulture,
                    "Stock adjusted for {0} (Batch {1}).",
                    result.MedicineName,
                    result.BatchNumber);

                if (!string.IsNullOrWhiteSpace(result.SnapshotWarning))
                    msg += "\n\n" + result.SnapshotWarning;

                MessageBox.Show(
                    this,
                    msg,
                    "ORVA — Stock adjusted",
                    MessageBoxButton.OK,
                    result.SnapshotUpdated ? MessageBoxImage.Information : MessageBoxImage.Warning);

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

        private static bool TryParseNonNegativeQuantity(string text, out long quantityMilli)
        {
            quantityMilli = 0;
            decimal value;
            if (!decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                return false;
            if (value < 0m || value > 999999999m)
                return false;
            decimal scaled = value * 1000m;
            if (scaled != decimal.Truncate(scaled))
                return false;
            quantityMilli = checked((long)scaled);
            return true;
        }
    }
}
