using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Orva.Services;
using Orva.Services.Billing;

namespace Orva.Views
{
    public partial class RecordsView : UserControl
    {
        private IList<PurchaseRegisterRow> _purchases = new List<PurchaseRegisterRow>();
        private IList<GstSummaryRow> _gstRows = new List<GstSummaryRow>();
        private IList<SalesReturnRegisterRow> _returnRows = new List<SalesReturnRegisterRow>();
        private IList<StockValuationRow> _valuationRows = new List<StockValuationRow>();
        private IList<ExpenseRow> _expenses = new List<ExpenseRow>();

        public RecordsView()
        {
            InitializeComponent();
            Loaded += (s, e) => RefreshAll();
        }

        private void RefreshAll()
        {
            if (App.Services == null) return;
            DateTime? from = FromDatePicker.SelectedDate;
            DateTime? to = ToDatePicker.SelectedDate;

            _purchases = App.Services.ReportingService.GetPurchaseRegister(from, to, null);
            PurchaseGrid.ItemsSource = _purchases;
            long purTotal = 0;
            int docCount = 0;
            for (int i = 0; i < _purchases.Count; i++)
            {
                purTotal += _purchases[i].TotalPaise;
                if (_purchases[i].IsDocumentedForReview) docCount++;
            }
            PurchaseSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} purchase receipt(s) ({1} Tax Invoices) · Total: ₹ {2}",
                _purchases.Count,
                docCount,
                InvoiceCalculator.FormatRupees(purTotal));

            _gstRows = App.Services.ReportingService.GetGstSummary(from, to);
            GstGrid.ItemsSource = _gstRows;
            long gstTotal = 0;
            for (int i = 0; i < _gstRows.Count; i++)
                gstTotal += _gstRows[i].CgstPaise + _gstRows[i].SgstPaise + _gstRows[i].IgstPaise;
            GstSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} HSN/rate group(s) (net of returns, cancelled excluded) · Net GST: ₹ {1}",
                _gstRows.Count,
                InvoiceCalculator.FormatRupees(gstTotal));

            _returnRows = App.Services.ReportingService.GetSalesReturnsRegister(from, to, null);
            ReturnsGrid.ItemsSource = _returnRows;
            long retTotal = 0;
            for (int i = 0; i < _returnRows.Count; i++) retTotal += _returnRows[i].RefundAmountPaise;
            ReturnsSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} sales return(s) · Total Refunded: ₹ {1}",
                _returnRows.Count,
                InvoiceCalculator.FormatRupees(retTotal));

            _valuationRows = App.Services.ReportingService.GetStockValuation();
            ValuationGrid.ItemsSource = _valuationRows;
            long costTotal = 0;
            long mrpTotal = 0;
            for (int i = 0; i < _valuationRows.Count; i++)
            {
                costTotal += _valuationRows[i].PurchaseCostValuePaise;
                mrpTotal += _valuationRows[i].MrpValuePaise;
            }
            ValuationSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} medicines · Usable Cost Value: ₹ {1} · MRP Value: ₹ {2}",
                _valuationRows.Count,
                InvoiceCalculator.FormatRupees(costTotal),
                InvoiceCalculator.FormatRupees(mrpTotal));

            _expenses = App.Services.ReportingService.GetExpenses(from, to);
            ExpenseGrid.ItemsSource = _expenses;
            long expTotal = 0;
            for (int i = 0; i < _expenses.Count; i++) expTotal += _expenses[i].AmountPaise;
            ExpenseSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} expense record(s) · Total: ₹ {1}",
                _expenses.Count,
                InvoiceCalculator.FormatRupees(expTotal));

            FilterStatusText.Text = (from.HasValue || to.HasValue)
                ? "Filtered: " + (from.HasValue ? from.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "Start")
                  + " to " + (to.HasValue ? to.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "Today")
                : "Showing all dates";
        }

        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        private void CurrentFy_Click(object sender, RoutedEventArgs e)
        {
            DateTime today = DateTime.Today;
            var shop = App.Services != null ? App.Services.ShopRepository.GetProfile() : null;
            int startMonth = (shop != null && shop.FinancialYearStartMonth >= 1 && shop.FinancialYearStartMonth <= 12) ? shop.FinancialYearStartMonth : 4;
            int fyStartYear = today.Month >= startMonth ? today.Year : today.Year - 1;
            DateTime fyStart = new DateTime(fyStartYear, startMonth, 1);
            FromDatePicker.SelectedDate = fyStart;
            ToDatePicker.SelectedDate = fyStart.AddYears(1).AddDays(-1);
            RefreshAll();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;
            RefreshAll();
        }

        private void SafeExport(string title, string filter, string defaultName, Action<string> writer, TextBlock feedbackTarget)
        {
            var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
            if (dlg.ShowDialog() != true) return;
            try
            {
                writer(dlg.FileName);
                feedbackTarget.Text = "Exported to " + dlg.FileName;
            }
            catch (Exception ex)
            {
                feedbackTarget.Text = "Export failed (close the file if open in Excel): " + ex.Message;
            }
        }

        private void ExportPurchases_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Purchase Register CSV", "CSV files (*.csv)|*.csv",
                "ORVA_Purchases_" + DateTime.Now.ToString("yyyyMMdd") + ".csv",
                p => App.Services.ReportingService.ExportPurchaseRegisterCsv(p, _purchases), PurchaseSummaryText);
        }

        private void ExportPurchasesXlsx_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Purchase Register Excel", "Excel files (*.xlsx)|*.xlsx",
                "ORVA_Purchases_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx",
                p => App.Services.ReportingService.ExportPurchaseRegisterXlsx(p, _purchases), PurchaseSummaryText);
        }

        private void ExportGst_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export GST Summary CSV", "CSV files (*.csv)|*.csv",
                "ORVA_GST_Summary_" + DateTime.Now.ToString("yyyyMMdd") + ".csv",
                p => App.Services.ReportingService.ExportGstSummaryCsv(p, _gstRows), GstSummaryText);
        }

        private void ExportGstXlsx_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export GST Summary Excel", "Excel files (*.xlsx)|*.xlsx",
                "ORVA_GST_Summary_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx",
                p => App.Services.ReportingService.ExportGstSummaryXlsx(p, _gstRows), GstSummaryText);
        }

        private void ExportReturns_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Sales Returns CSV", "CSV files (*.csv)|*.csv",
                "ORVA_SalesReturns_" + DateTime.Now.ToString("yyyyMMdd") + ".csv",
                p => App.Services.ReportingService.ExportSalesReturnsCsv(p, _returnRows), ReturnsSummaryText);
        }

        private void ExportReturnsXlsx_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Sales Returns Excel", "Excel files (*.xlsx)|*.xlsx",
                "ORVA_SalesReturns_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx",
                p => App.Services.ReportingService.ExportSalesReturnsXlsx(p, _returnRows), ReturnsSummaryText);
        }

        private void ExportValuation_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Stock Valuation CSV", "CSV files (*.csv)|*.csv",
                "ORVA_StockValuation_" + DateTime.Now.ToString("yyyyMMdd") + ".csv",
                p => App.Services.ReportingService.ExportStockValuationCsv(p, _valuationRows), ValuationSummaryText);
        }

        private void ExportValuationXlsx_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Stock Valuation Excel", "Excel files (*.xlsx)|*.xlsx",
                "ORVA_StockValuation_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx",
                p => App.Services.ReportingService.ExportStockValuationXlsx(p, _valuationRows), ValuationSummaryText);
        }

        private void ExportExpenses_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Expenses CSV", "CSV files (*.csv)|*.csv",
                "ORVA_Expenses_" + DateTime.Now.ToString("yyyyMMdd") + ".csv",
                p => App.Services.ReportingService.ExportExpensesCsv(p, _expenses), ExpenseSummaryText);
        }

        private void ExportExpensesXlsx_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Expenses Excel", "Excel files (*.xlsx)|*.xlsx",
                "ORVA_Expenses_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx",
                p => App.Services.ReportingService.ExportExpensesXlsx(p, _expenses), ExpenseSummaryText);
        }

        private void AddExpense_Click(object sender, RoutedEventArgs e)
        {
            decimal amtDec;
            if (!decimal.TryParse((ExpenseAmountBox.Text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out amtDec) || amtDec <= 0m || amtDec > 999999999m)
            {
                ExpenseSummaryText.Text = "Enter a valid positive expense amount.";
                return;
            }

            long paise = checked((long)Math.Round(amtDec * 100m, 0, MidpointRounding.AwayFromZero));
            var item = ExpenseMethodBox.SelectedItem as ComboBoxItem;
            string method = item == null ? "CASH" : Convert.ToString(item.Tag, CultureInfo.InvariantCulture);

            try
            {
                App.Services.ReportingService.AddExpense(
                    ExpenseCategoryBox.Text,
                    ExpenseDescBox.Text,
                    paise,
                    method,
                    ExpenseRefBox.Text);
                ExpenseCategoryBox.Text = string.Empty;
                ExpenseDescBox.Text = string.Empty;
                ExpenseAmountBox.Text = string.Empty;
                ExpenseRefBox.Text = string.Empty;
                RefreshAll();
            }
            catch (Exception ex)
            {
                ExpenseSummaryText.Text = "Could not save expense: " + ex.Message;
            }
        }

        private void ExportPurchasesPdf_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Purchase Register PDF", "PDF files (*.pdf)|*.pdf",
                "ORVA_Purchases_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf",
                p => App.Services.ReportingService.ExportPurchaseRegisterPdf(p, _purchases, FilterStatusText.Text), PurchaseSummaryText);
        }

        private void ExportGstPdf_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export GST Summary PDF", "PDF files (*.pdf)|*.pdf",
                "ORVA_GST_Summary_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf",
                p => App.Services.ReportingService.ExportGstSummaryPdf(p, _gstRows, FilterStatusText.Text), GstSummaryText);
        }

        private void ExportReturnsPdf_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Sales Returns PDF", "PDF files (*.pdf)|*.pdf",
                "ORVA_SalesReturns_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf",
                p => App.Services.ReportingService.ExportSalesReturnsPdf(p, _returnRows, FilterStatusText.Text), ReturnsSummaryText);
        }

        private void ExportValuationPdf_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Stock Valuation PDF", "PDF files (*.pdf)|*.pdf",
                "ORVA_StockValuation_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf",
                p => App.Services.ReportingService.ExportStockValuationPdf(p, _valuationRows), ValuationSummaryText);
        }

        private void ExportExpensesPdf_Click(object sender, RoutedEventArgs e)
        {
            SafeExport("Export Expenses PDF", "PDF files (*.pdf)|*.pdf",
                "ORVA_Expenses_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf",
                p => App.Services.ReportingService.ExportExpensesPdf(p, _expenses, FilterStatusText.Text), ExpenseSummaryText);
        }
    }
}
