using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Orva.Domain.Billing;
using Orva.Services;
using Orva.Services.Billing;
using Orva.ViewModels;

namespace Orva.Views
{
    public partial class InvoicesView : UserControl
    {
        private IList<SalesRegisterRow> _rows = new List<SalesRegisterRow>();

        public InvoicesView()
        {
            InitializeComponent();
            Loaded += (s, e) => RefreshList();
        }

        private void RefreshList()
        {
            if (App.Services == null) return;
            var selected = StatusFilterCombo.SelectedItem as ComboBoxItem;
            string status = selected != null ? Convert.ToString(selected.Content) : "ALL";
            _rows = App.Services.ReportingService.GetSalesRegister(
                FromDatePicker.SelectedDate,
                ToDatePicker.SelectedDate,
                status,
                SearchBox.Text);
            InvoicesGrid.ItemsSource = _rows;

            long activeTotal = 0;
            int activeCount = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!string.Equals(_rows[i].Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    activeTotal += _rows[i].GrandTotalPaise;
                    activeCount++;
                }
            }
            StatusText.Text = _rows.Count + " invoice(s) | " + activeCount + " active (Rs. " + InvoiceCalculator.FormatRupees(activeTotal) + ")";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) RefreshList();
        }

        private void StatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) RefreshList();
        }

        private void ReprintInvoice_Click(object sender, RoutedEventArgs e)
        {
            var row = InvoicesGrid.SelectedItem as SalesRegisterRow;
            if (row == null)
            {
                StatusText.Text = "Select an invoice to reprint.";
                return;
            }

            try
            {
                var record = App.Services.ReportingService.GetInvoiceForReprint(row.InvoiceId);
                if (record == null)
                {
                    StatusText.Text = "Invoice details could not be loaded.";
                    return;
                }

                var shop = App.Services.ShopRepository.GetProfile();
                var header = BillingShopHeader.FromProfile(shop);
                InvoicePaperFormat format = App.Services.InvoicePrintService.GetDefaultPaperFormat();

                var printResult = App.Services.InvoicePrintService.PrintInvoice(
                    record.CommittedInvoice,
                    record.Preview,
                    format,
                    header,
                    record.CustomerName,
                    record.CustomerPhone,
                    record.CustomerAddress,
                    record.CustomerGstin,
                    record.Tenders);

                if (printResult.Printed)
                    StatusText.Text = "Reprinted " + row.InvoiceNumber + " on " + printResult.PrinterName + ".";
                else if (printResult.CancelledByUser)
                    StatusText.Text = "Reprint cancelled.";
                else
                    StatusText.Text = printResult.ErrorMessage;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Reprint failed: " + ex.Message;
            }
        }

        private void CancelInvoice_Click(object sender, RoutedEventArgs e)
        {
            var row = InvoicesGrid.SelectedItem as SalesRegisterRow;
            if (row == null)
            {
                StatusText.Text = "Select an invoice to cancel.";
                return;
            }

            string reason = (CancelReasonBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                StatusText.Text = "Enter a cancellation reason before cancelling.";
                CancelReasonBox.Focus();
                return;
            }

            bool confirmed = OrvaConfirmDialog.Show(
                Window.GetWindow(this),
                "Confirm Invoice Cancellation",
                "Cancel invoice " + row.InvoiceNumber + " (Rs. " + row.GrandTotalText + ")?\n\nReason: " + reason + "\nThe invoice will remain as CANCELLED in your archive and deducted batch stock will be restored.",
                true,
                "Cancel Invoice",
                "Keep Invoice");
            if (!confirmed) return;

            try
            {
                var result = App.Services.InvoiceCancellationService.CancelInvoice(row.InvoiceId, reason, restoreStock: true);
                if (!result.Succeeded)
                {
                    StatusText.Text = result.ErrorMessage;
                    return;
                }

                CancelReasonBox.Text = string.Empty;
                RefreshList();
                StatusText.Text = "Invoice " + result.InvoiceNumber + " cancelled"
                    + (result.StockRestored ? " and batch stock restored." : ".")
                    + (string.IsNullOrWhiteSpace(result.StockWarning) ? string.Empty : " [" + result.StockWarning + "]") + (string.IsNullOrWhiteSpace(result.SnapshotWarning) ? string.Empty : " (" + result.SnapshotWarning + ")");
                if (!string.IsNullOrWhiteSpace(result.StockWarning)) OrvaConfirmDialog.Show(Window.GetWindow(this), "ORVA — Stock Warning", result.StockWarning, true, "OK");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Cancellation failed: " + ex.Message;
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Sales Register CSV",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "ORVA_SalesRegister_" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                App.Services.ReportingService.ExportSalesRegisterCsv(dlg.FileName, _rows);
                StatusText.Text = "Exported Sales Register CSV to " + dlg.FileName;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Export failed (close the file if open in Excel): " + ex.Message;
            }
        }

        private void ExportXlsx_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Sales Register Excel",
                Filter = "Excel files (*.xlsx)|*.xlsx",
                FileName = "ORVA_SalesRegister_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                App.Services.ReportingService.ExportSalesRegisterXlsx(dlg.FileName, _rows);
                StatusText.Text = "Exported Sales Register Excel to " + dlg.FileName;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Export failed (close the file if open in Excel): " + ex.Message;
            }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Sales Register PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = "ORVA_SalesRegister_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                App.Services.ReportingService.ExportSalesRegisterPdf(dlg.FileName, _rows);
                StatusText.Text = "Exported Sales Register PDF to " + dlg.FileName;
            }
            catch (Exception ex)
            {
                StatusText.Text = "PDF export failed: " + ex.Message;
            }
        }

        private void InvoicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = InvoicesGrid.SelectedItem as SalesRegisterRow;
            InvoiceDetailLines.Items.Clear();
            if (row == null)
            {
                DetailHeaderText.Text = "Select an invoice to inspect items";
                DetailFooterText.Text = string.Empty;
                return;
            }
            DetailHeaderText.Text = row.InvoiceNumber + " · " + row.IssuedDateText;
            try
            {
                var rec = App.Services.ReportingService.GetInvoiceForReprint(row.InvoiceId);
                if (rec == null || rec.Preview == null) return;
                foreach (var line in rec.Preview.Lines)
                {
                    var batchParts = new List<string>();
                    for (int b = 0; b < line.BatchAllocations.Count; b++)
                    {
                        var alloc = line.BatchAllocations[b];
                        batchParts.Add(alloc.BatchNumber + " (" + InvoiceCalculator.FormatQuantity(alloc.QuantityMilli) + ")");
                    }
                    string batchInfo = batchParts.Count > 0
                        ? " [" + string.Join(", ", batchParts) + "]"
                        : string.Empty;
                    InvoiceDetailLines.Items.Add(
                        line.MedicineName + batchInfo + " — " +
                        InvoiceCalculator.FormatQuantity(line.QuantityMilli) + " " + line.StockUnitName +
                        " × ₹" + InvoiceCalculator.FormatRupees((line.BatchAllocations.Count > 0 ? line.BatchAllocations[0].UnitPricePaise : 0L)) +
                        " = ₹" + InvoiceCalculator.FormatRupees(line.LineTotalPaise));
                }
                DetailFooterText.Text = "Taxable: ₹" + row.TaxableText + " · Tax: ₹" + row.TaxTotalText + " · Total: ₹" + row.GrandTotalText + " (" + row.Status + ")";
            }
            catch (Exception ex)
            {
                DetailFooterText.Text = "Could not load detail: " + ex.Message;
            }
        }
    }
}
