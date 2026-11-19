using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orva.Services;
using Orva.Services.Billing;

namespace Orva.Views
{
    public sealed class ReturnLineEditRow
    {
        public string InvoiceLineId { get; set; }
        public string MedicineName { get; set; }
        public string BatchNumber { get; set; }
        public long RemainingReturnableMilli { get; set; }
        public string StockUnitName { get; set; }
        public string RemainingText { get { return InvoiceCalculator.FormatQuantity(RemainingReturnableMilli) + " " + StockUnitName; } }
        public string ReturnQtyText { get; set; }
        public string Disposition { get; set; }
    }

    public partial class SalesReturnsView : UserControl
    {
        private readonly ObservableCollection<ReturnLineEditRow> _rows = new ObservableCollection<ReturnLineEditRow>();
        private ReturnableInvoiceSummary _selectedInvoice;
        private bool _isBusy;

        public SalesReturnsView()
        {
            InitializeComponent();
            ReturnLinesGrid.ItemsSource = _rows;
            RefreshInvoices();
        }

        private void RefreshInvoices()
        {
            string query = InvoiceSearchBox == null ? string.Empty : InvoiceSearchBox.Text;
            InvoiceListBox.ItemsSource = App.Services.SalesReturnService.SearchReturnableInvoices(query, 40);
        }

        private void InvoiceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isBusy) return;
            RefreshInvoices();
        }

        private void InvoiceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isBusy) return;
            _selectedInvoice = InvoiceListBox.SelectedItem as ReturnableInvoiceSummary;
            _rows.Clear();
            ReturnStatusText.Text = string.Empty;

            if (_selectedInvoice == null)
            {
                SelectedInvoiceHeader.Text = LocalizationManager.Get("RetSelectPrompt");
                return;
            }

            SelectedInvoiceHeader.Text = "Invoice " + _selectedInvoice.InvoiceNumber + " · Total ₹ " + InvoiceCalculator.FormatRupees(_selectedInvoice.GrandTotalPaise);
            var lines = App.Services.SalesReturnService.GetReturnableLines(_selectedInvoice.InvoiceId);
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.RemainingReturnableMilli <= 0) continue;
                _rows.Add(new ReturnLineEditRow
                {
                    InvoiceLineId = l.InvoiceLineId,
                    MedicineName = l.MedicineName,
                    BatchNumber = l.BatchNumber,
                    RemainingReturnableMilli = l.RemainingReturnableMilli,
                    StockUnitName = l.StockUnitName,
                    ReturnQtyText = "0",
                    Disposition = "QUARANTINE"
                });
            }
        }

        private SalesReturnRequest BuildRequest()
        {
            var item = RefundMethodBox.SelectedItem as ComboBoxItem;
            string method = item == null ? "CASH" : Convert.ToString(item.Tag, CultureInfo.InvariantCulture);

            var req = new SalesReturnRequest
            {
                InvoiceId = _selectedInvoice != null ? _selectedInvoice.InvoiceId : null,
                Reason = ReasonBox.Text,
                RefundMethod = method
            };

            for (int i = 0; i < _rows.Count; i++)
            {
                string rawQty = (_rows[i].ReturnQtyText ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(rawQty) || rawQty == "0") continue;
                decimal qtyDec;
                if (!decimal.TryParse(rawQty, NumberStyles.Number, CultureInfo.CurrentCulture, out qtyDec) || qtyDec < 0m || qtyDec > 999999999m)
                    throw new ArgumentException("Invalid return quantity '" + rawQty + "' for " + _rows[i].MedicineName + ".");
                if (qtyDec == 0m) continue;

                long qtyMilli = checked((long)Math.Round(qtyDec * 1000m, 0, MidpointRounding.AwayFromZero));
                req.Lines.Add(new SalesReturnLineRequest
                {
                    InvoiceLineId = _rows[i].InvoiceLineId,
                    ReturnQuantityMilli = qtyMilli,
                    ResaleDisposition = _rows[i].Disposition ?? "QUARANTINE"
                });
            }
            return req;
        }

        private void PreviewReturn_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            SalesReturnPreviewResult preview;
            try { preview = App.Services.SalesReturnService.PreviewReturn(BuildRequest()); }
            catch (Exception ex) { ReturnStatusText.Foreground = (Brush)FindResource("Danger"); ReturnStatusText.Text = ex.Message; return; }
            if (!preview.CanFinalize)
            {
                ReturnStatusText.Foreground = (Brush)FindResource("Danger");
                ReturnStatusText.Text = string.Join(" ", preview.Errors);
                return;
            }

            ReturnStatusText.Foreground = (Brush)FindResource("Teal");
            ReturnStatusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Preview ready · Refund: ₹ {0} · Quarantined lines: {1} · Restocked lines: {2}",
                InvoiceCalculator.FormatRupees(preview.TotalRefundPaise),
                preview.QuarantinedLineCount,
                preview.RestockedLineCount);
        }

        private void FinalizeReturn_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            SalesReturnRequest req; SalesReturnPreviewResult preview;
            try { req = BuildRequest(); preview = App.Services.SalesReturnService.PreviewReturn(req); }
            catch (Exception ex) { ReturnStatusText.Foreground = (Brush)FindResource("Danger"); ReturnStatusText.Text = ex.Message; return; }
            if (!preview.CanFinalize)
            {
                ReturnStatusText.Foreground = (Brush)FindResource("Danger");
                ReturnStatusText.Text = string.Join(" ", preview.Errors);
                return;
            }

            bool confirmed = OrvaConfirmDialog.Show(
                Window.GetWindow(this),
                "ORVA — Confirm Sales Return",
                "Confirm sales return for ₹ " + InvoiceCalculator.FormatRupees(preview.TotalRefundPaise) + "? Quarantined items will remain out of sellable stock.",
                false,
                "Confirm Return",
                "Cancel");
            if (!confirmed) return;

            _isBusy = true;
            FinalizeReturnButton.IsEnabled = false;
            PreviewReturnButton.IsEnabled = false;
            try
            {
                var res = App.Services.SalesReturnService.FinalizeReturn(req);
                if (!res.Succeeded)
                {
                    ReturnStatusText.Foreground = (Brush)FindResource("Danger");
                    ReturnStatusText.Text = res.ErrorMessage;
                    return;
                }

                ReturnStatusText.Foreground = (Brush)FindResource("Success");
                ReturnStatusText.Text = "Return " + res.ReturnNumber + " saved (Refund: ₹ " + InvoiceCalculator.FormatRupees(res.RefundAmountPaise) + ")." +
                    (string.IsNullOrWhiteSpace(res.SnapshotWarning) ? string.Empty : " " + res.SnapshotWarning);
                ReasonBox.Text = string.Empty;
                _selectedInvoice = null;
                SelectedInvoiceHeader.Text = "Select an invoice to process a return";
                _rows.Clear();
            }
            catch (Exception ex)
            {
                ReturnStatusText.Foreground = (Brush)FindResource("Danger");
                ReturnStatusText.Text = "Could not save return: " + ex.Message;
            }
            finally
            {
                _isBusy = false;
                FinalizeReturnButton.IsEnabled = true;
                PreviewReturnButton.IsEnabled = true;
            }
            RefreshInvoices();
        }
    }
}
