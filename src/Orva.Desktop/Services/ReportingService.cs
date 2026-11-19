using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Orva.Domain;
using Orva.Domain.Billing;
using Orva.Infrastructure.Database;
using Orva.Services.Billing;

namespace Orva.Services
{
    public sealed class SalesRegisterRow
    {
        public string InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string IssuedAtUtc { get; set; }
        public string IssuedDateText { get; set; }
        public string CustomerName { get; set; }
        public string CustomerGstin { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long RoundOffPaise { get; set; }
        public long GrandTotalPaise { get; set; }
        public long PaidTotalPaise { get; set; }
        public string Status { get; set; }
        public string CancellationReason { get; set; }

        public string TaxableText { get { return InvoiceCalculator.FormatRupees(TaxableValuePaise); } }
        public string TaxTotalText { get { return InvoiceCalculator.FormatRupees(CgstPaise + SgstPaise + IgstPaise); } }
        public string GrandTotalText { get { return InvoiceCalculator.FormatRupees(GrandTotalPaise); } }
        public string PaidText { get { return InvoiceCalculator.FormatRupees(PaidTotalPaise); } }
    }

    public sealed class PurchaseRegisterRow
    {
        public string PurchaseId { get; set; }
        public string ReceivedAtUtc { get; set; }
        public string ReceivedDateText { get; set; }
        public string SupplierName { get; set; }
        public string DocumentType { get; set; }
        public string DocumentReference { get; set; }
        public long TotalPaise { get; set; }
        public bool IsDocumentedForReview { get { return string.Equals(DocumentType, "TAX_INVOICE", StringComparison.Ordinal); } }
        public string TotalText { get { return InvoiceCalculator.FormatRupees(TotalPaise); } }
    }

    public sealed class GstSummaryRow
    {
        public string HsnCode { get; set; }
        public int GstRateBasisPoints { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long TotalValuePaise { get; set; }

        public string GstRateText { get { return (GstRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%"; } }
        public string TaxableText { get { return InvoiceCalculator.FormatRupees(TaxableValuePaise); } }
        public string CgstText { get { return InvoiceCalculator.FormatRupees(CgstPaise); } }
        public string SgstText { get { return InvoiceCalculator.FormatRupees(SgstPaise); } }
        public string IgstText { get { return InvoiceCalculator.FormatRupees(IgstPaise); } }
        public string TotalText { get { return InvoiceCalculator.FormatRupees(TotalValuePaise); } }
    }

    public sealed class StockValuationRow
    {
        public string MedicineId { get; set; }
        public string MedicineName { get; set; }
        public string StockUnitName { get; set; }
        public long UsableQuantityMilli { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public bool IsLowStock { get; set; }
        public long PurchaseCostValuePaise { get; set; }
        public long MrpValuePaise { get; set; }

        public string QuantityText { get { return InvoiceCalculator.FormatQuantity(UsableQuantityMilli) + " " + StockUnitName; } }
        public string CostValueText { get { return InvoiceCalculator.FormatRupees(PurchaseCostValuePaise); } }
        public string MrpValueText { get { return InvoiceCalculator.FormatRupees(MrpValuePaise); } }
        public string AlertText { get { return IsLowStock ? "Low stock" : "OK"; } }
    }

    public sealed class ExpenseRow
    {
        public string ExpenseId { get; set; }
        public string ExpenseDateUtc { get; set; }
        public string ExpenseDateText { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public long AmountPaise { get; set; }
        public string PaymentMethod { get; set; }
        public string DocumentReference { get; set; }
        public string AmountText { get { return InvoiceCalculator.FormatRupees(AmountPaise); } }
    }

    public sealed class SalesReturnRegisterRow
    {
        public string ReturnId { get; set; }
        public string ReturnNumber { get; set; }
        public string InvoiceNumber { get; set; }
        public string ReturnedAtUtc { get; set; }
        public string ReturnedDateText { get; set; }
        public string Reason { get; set; }
        public string RefundMethod { get; set; }
        public long RefundAmountPaise { get; set; }
        public string Status { get; set; }
        public string RefundText { get { return InvoiceCalculator.FormatRupees(RefundAmountPaise); } }
    }

    public sealed class HistoricalInvoiceRecord
    {
        public InvoiceResult CommittedInvoice { get; set; }
        public InvoicePreviewResult Preview { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPhone { get; set; }
        public string CustomerAddress { get; set; }
        public string CustomerGstin { get; set; }
        public List<PaymentTenderViewModel> Tenders { get; set; }
    }

    public sealed class ReportingService
    {
        private readonly SqliteDatabase _database;

        public ReportingService(SqliteDatabase database)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
        }

        public IList<SalesRegisterRow> GetSalesRegister(DateTime? fromDate, DateTime? toDate, string statusFilter = "ALL", string search = null)
        {
            var rows = new List<SalesRegisterRow>();
            string fromIso = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string toIso = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string status = string.IsNullOrWhiteSpace(statusFilter) ? "ALL" : statusFilter.Trim().ToUpperInvariant();
            string q = (search ?? string.Empty).Trim().ToUpperInvariant();

            const string sql = @"
SELECT i.invoice_id, i.invoice_number, i.issued_at_utc, i.customer_name, i.customer_gstin,
       i.taxable_value_paise, i.cgst_paise, i.sgst_paise, i.igst_paise, i.round_off_paise,
       i.grand_total_paise, i.status, i.cancellation_reason,
       COALESCE((SELECT SUM(p.amount_paise) FROM payment p WHERE p.invoice_id = i.invoice_id), 0) AS paid_paise
FROM invoice i
WHERE (@from = '' OR i.issued_at_utc >= @from)
  AND (@to = '' OR i.issued_at_utc <= @to)
  AND (@status = 'ALL' OR i.status = @status)
  AND (@q = '' OR UPPER(i.invoice_number) LIKE @like ESCAPE '\' OR UPPER(COALESCE(i.customer_name, '')) LIKE @like ESCAPE '\')
ORDER BY i.issued_at_utc DESC;";

            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@from", fromIso);
                cmd.Parameters.AddWithValue("@to", toIso);
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@q", q);
                cmd.Parameters.AddWithValue("@like", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string utc = r.GetString(2);
                        rows.Add(new SalesRegisterRow
                        {
                            InvoiceId = r.GetString(0),
                            InvoiceNumber = r.GetString(1),
                            IssuedAtUtc = utc,
                            IssuedDateText = FormatUtcToLocalDate(utc),
                            CustomerName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                            CustomerGstin = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                            TaxableValuePaise = r.GetInt64(5),
                            CgstPaise = r.GetInt64(6),
                            SgstPaise = r.GetInt64(7),
                            IgstPaise = r.GetInt64(8),
                            RoundOffPaise = r.GetInt64(9),
                            GrandTotalPaise = r.GetInt64(10),
                            Status = r.GetString(11),
                            CancellationReason = r.IsDBNull(12) ? string.Empty : r.GetString(12),
                            PaidTotalPaise = r.GetInt64(13)
                        });
                    }
                }
            }
            return rows;
        }

        public IList<PurchaseRegisterRow> GetPurchaseRegister(DateTime? fromDate, DateTime? toDate, string supplierSearch = null)
        {
            var rows = new List<PurchaseRegisterRow>();
            string fromIso = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string toIso = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string q = (supplierSearch ?? string.Empty).Trim().ToUpperInvariant();

            const string sql = @"
SELECT purchase_id, received_at_utc, supplier_name_text, document_type, document_reference, total_paise
FROM purchase_receipt
WHERE (@from = '' OR received_at_utc >= @from)
  AND (@to = '' OR received_at_utc <= @to)
  AND (@q = '' OR UPPER(COALESCE(supplier_name_text, '')) LIKE @like ESCAPE '\' OR UPPER(COALESCE(document_reference, '')) LIKE @like ESCAPE '\')
ORDER BY received_at_utc DESC;";

            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@from", fromIso);
                cmd.Parameters.AddWithValue("@to", toIso);
                cmd.Parameters.AddWithValue("@q", q);
                cmd.Parameters.AddWithValue("@like", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string utc = r.GetString(1);
                        rows.Add(new PurchaseRegisterRow
                        {
                            PurchaseId = r.GetString(0),
                            ReceivedAtUtc = utc,
                            ReceivedDateText = FormatUtcToLocalDate(utc),
                            SupplierName = r.IsDBNull(2) ? "Unspecified" : r.GetString(2),
                            DocumentType = r.GetString(3),
                            DocumentReference = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                            TotalPaise = r.GetInt64(5)
                        });
                    }
                }
            }
            return rows;
        }

        /// <summary>
        /// Computes GST summary by HSN and GST rate across active (non-cancelled) invoices.
        /// Cancelled invoices are strictly excluded.
        /// </summary>
        public IList<GstSummaryRow> GetGstSummary(DateTime? fromDate, DateTime? toDate)
        {
            var rows = new List<GstSummaryRow>();
            string fromIso = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string toIso = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;

            const string sql = @"
SELECT COALESCE(il.hsn_code_snapshot, 'UNSPECIFIED') AS hsn,
       il.gst_rate_basis_points,
       il.quantity_milli,
       il.taxable_value_paise,
       il.cgst_paise,
       il.sgst_paise,
       il.igst_paise,
       il.line_total_paise,
       COALESCE((SELECT SUM(srl.quantity_milli)
                 FROM sales_return_line srl
                 JOIN sales_return sr ON sr.return_id = srl.return_id
                 WHERE srl.invoice_line_id = il.invoice_line_id
                   AND sr.status = 'COMPLETED'
                   AND (@from = '' OR sr.returned_at_utc >= @from)
                   AND (@to = '' OR sr.returned_at_utc <= @to)), 0) AS returned_qty_milli
FROM invoice_line il
JOIN invoice i ON i.invoice_id = il.invoice_id
WHERE i.status <> 'CANCELLED'
  AND (@from = '' OR i.issued_at_utc >= @from)
  AND (@to = '' OR i.issued_at_utc <= @to)
ORDER BY hsn ASC, il.gst_rate_basis_points ASC;";

            var groups = new Dictionary<string, GstSummaryRow>(StringComparer.Ordinal);
            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@from", fromIso);
                cmd.Parameters.AddWithValue("@to", toIso);

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string hsn = r.GetString(0);
                        int rateBps = r.GetInt32(1);
                        long origQty = r.GetInt64(2);
                        long taxable = r.GetInt64(3);
                        long cgst = r.GetInt64(4);
                        long sgst = r.GetInt64(5);
                        long igst = r.GetInt64(6);
                        long total = r.GetInt64(7);
                        long returnedQty = r.GetInt64(8);

                        long netQty = Math.Max(0L, origQty - returnedQty);
                        if (netQty <= 0L) continue;

                        if (returnedQty > 0L && origQty > 0L)
                        {
                            taxable = ScaleProportional(taxable, netQty, origQty);
                            cgst = ScaleProportional(cgst, netQty, origQty);
                            sgst = ScaleProportional(sgst, netQty, origQty);
                            igst = ScaleProportional(igst, netQty, origQty);
                            total = checked(taxable + cgst + sgst + igst);
                        }

                        string key = hsn + "|" + rateBps.ToString(CultureInfo.InvariantCulture);
                        GstSummaryRow agg;
                        if (!groups.TryGetValue(key, out agg))
                        {
                            agg = new GstSummaryRow { HsnCode = hsn, GstRateBasisPoints = rateBps };
                            groups[key] = agg;
                            rows.Add(agg);
                        }

                        agg.TaxableValuePaise = checked(agg.TaxableValuePaise + taxable);
                        agg.CgstPaise = checked(agg.CgstPaise + cgst);
                        agg.SgstPaise = checked(agg.SgstPaise + sgst);
                        agg.IgstPaise = checked(agg.IgstPaise + igst);
                        agg.TotalValuePaise = checked(agg.TotalValuePaise + total);
                    }
                }
            }
            return rows;
        }

        private static long ScaleProportional(long amountPaise, long netQtyMilli, long origQtyMilli)
        {
            if (amountPaise == 0L || netQtyMilli <= 0L || origQtyMilli <= 0L) return 0L;
            decimal scaled = ((decimal)amountPaise * (decimal)netQtyMilli) / (decimal)origQtyMilli;
            return checked((long)Math.Round(scaled, 0, MidpointRounding.AwayFromZero));
        }

        public IList<StockValuationRow> GetStockValuation(DateTime? localBusinessDate = null)
        {
            DateTime today = (localBusinessDate ?? DateTime.Now).Date;
            int currentMonth = today.Year * 100 + today.Month;
            var rows = new List<StockValuationRow>();

            const string sql = @"
SELECT m.medicine_id, m.display_name, m.stock_unit_name, m.low_stock_threshold_milli,
       b.quantity_milli, b.latest_purchase_price_paise, b.mrp_paise
FROM medicine m
LEFT JOIN stock_batch b ON b.medicine_id = m.medicine_id
    AND b.batch_status = 'ACTIVE'
    AND b.quantity_milli > 0
    AND b.expiry_yyyymm >= @current_month
WHERE m.is_active = 1
ORDER BY m.display_name COLLATE NOCASE ASC;";

            var map = new Dictionary<string, StockValuationRow>(StringComparer.Ordinal);
            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@current_month", currentMonth);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string medId = r.GetString(0);
                        StockValuationRow row;
                        if (!map.TryGetValue(medId, out row))
                        {
                            row = new StockValuationRow
                            {
                                MedicineId = medId,
                                MedicineName = r.GetString(1),
                                StockUnitName = r.GetString(2),
                                LowStockThresholdMilli = r.GetInt64(3)
                            };
                            map[medId] = row;
                            rows.Add(row);
                        }

                        if (!r.IsDBNull(4))
                        {
                            long qty = r.GetInt64(4);
                            long purPrice = r.GetInt64(5);
                            long mrp = r.GetInt64(6);
                            row.UsableQuantityMilli = checked(row.UsableQuantityMilli + qty);
                            row.PurchaseCostValuePaise = checked(row.PurchaseCostValuePaise + InvoiceCalculator.CalculateGrossPaise(qty, purPrice));
                            row.MrpValuePaise = checked(row.MrpValuePaise + InvoiceCalculator.CalculateGrossPaise(qty, mrp));
                        }
                    }
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].IsLowStock = InventoryAlertCalculator.IsLowStock(true, rows[i].UsableQuantityMilli, rows[i].LowStockThresholdMilli);
            }

            return rows;
        }

        public IList<ExpenseRow> GetExpenses(DateTime? fromDate, DateTime? toDate)
        {
            var rows = new List<ExpenseRow>();
            string fromIso = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string toIso = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;

            const string sql = @"
SELECT expense_id, expense_date_utc, category, description, amount_paise, payment_method, document_reference
FROM expense_record
WHERE (@from = '' OR expense_date_utc >= @from)
  AND (@to = '' OR expense_date_utc <= @to)
ORDER BY expense_date_utc DESC;";

            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@from", fromIso);
                cmd.Parameters.AddWithValue("@to", toIso);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string utc = r.GetString(1);
                        rows.Add(new ExpenseRow
                        {
                            ExpenseId = r.GetString(0),
                            ExpenseDateUtc = utc,
                            ExpenseDateText = FormatUtcToLocalDate(utc),
                            Category = r.GetString(2),
                            Description = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                            AmountPaise = r.GetInt64(4),
                            PaymentMethod = r.IsDBNull(5) ? string.Empty : r.GetString(5),
                            DocumentReference = r.IsDBNull(6) ? string.Empty : r.GetString(6)
                        });
                    }
                }
            }
            return rows;
        }

        public void AddExpense(string category, string description, long amountPaise, string paymentMethod, string documentReference)
        {
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Enter an expense category.");
            if (amountPaise <= 0) throw new ArgumentException("Expense amount must be greater than zero.");
            string method = string.IsNullOrWhiteSpace(paymentMethod) ? "CASH" : paymentMethod.Trim().ToUpperInvariant();
            var allowed = new[] { "CASH", "UPI", "DEBIT_CARD", "CREDIT_CARD" };
            if (Array.IndexOf(allowed, method) < 0) throw new ArgumentException("Choose a valid payment method.");

            string nowUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using (var conn = _database.OpenConnection())
            using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
            {
                using (var cmd = new SQLiteCommand(@"
INSERT INTO expense_record(expense_id, expense_date_utc, category, description, amount_paise, payment_method, document_reference, created_at_utc)
VALUES(@id, @date, @cat, @desc, @amt, @method, @ref, @created);", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    cmd.Parameters.AddWithValue("@date", nowUtc);
                    cmd.Parameters.AddWithValue("@cat", category.Trim());
                    cmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description.Trim());
                    cmd.Parameters.AddWithValue("@amt", amountPaise);
                    cmd.Parameters.AddWithValue("@method", method);
                    cmd.Parameters.AddWithValue("@ref", string.IsNullOrWhiteSpace(documentReference) ? (object)DBNull.Value : documentReference.Trim());
                    cmd.Parameters.AddWithValue("@created", nowUtc);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        public void ExportSalesRegisterCsv(string filePath, IList<SalesRegisterRow> rows)
        {
            var headers = new[] { "Date", "Invoice Number", "Customer", "Customer GSTIN", "Taxable (Rs)", "CGST (Rs)", "SGST (Rs)", "IGST (Rs)", "Grand Total (Rs)", "Paid (Rs)", "Status" };
            var data = new List<IList<string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[]
                {
                    r.IssuedDateText, r.InvoiceNumber, r.CustomerName, r.CustomerGstin,
                    InvoiceCalculator.FormatRupees(r.TaxableValuePaise),
                    InvoiceCalculator.FormatRupees(r.CgstPaise),
                    InvoiceCalculator.FormatRupees(r.SgstPaise),
                    InvoiceCalculator.FormatRupees(r.IgstPaise),
                    InvoiceCalculator.FormatRupees(r.GrandTotalPaise),
                    InvoiceCalculator.FormatRupees(r.PaidTotalPaise),
                    r.Status
                });
            }
            CsvExportHelper.WriteCsv(filePath, headers, data);
        }

        public void ExportPurchaseRegisterCsv(string filePath, IList<PurchaseRegisterRow> rows)
        {
            var headers = new[] { "Date", "Supplier", "Document Type", "Document Reference", "Total (Rs)" };
            var data = new List<IList<string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ReceivedDateText, r.SupplierName, r.DocumentType, r.DocumentReference, InvoiceCalculator.FormatRupees(r.TotalPaise) });
            }
            CsvExportHelper.WriteCsv(filePath, headers, data);
        }

        public void ExportGstSummaryCsv(string filePath, IList<GstSummaryRow> rows)
        {
            var headers = new[] { "HSN Code", "GST Rate", "Taxable Value (Rs)", "CGST (Rs)", "SGST (Rs)", "IGST (Rs)", "Total Value (Rs)" };
            var data = new List<IList<string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[]
                {
                    r.HsnCode, r.GstRateText,
                    InvoiceCalculator.FormatRupees(r.TaxableValuePaise),
                    InvoiceCalculator.FormatRupees(r.CgstPaise),
                    InvoiceCalculator.FormatRupees(r.SgstPaise),
                    InvoiceCalculator.FormatRupees(r.IgstPaise),
                    InvoiceCalculator.FormatRupees(r.TotalValuePaise)
                });
            }
            CsvExportHelper.WriteCsv(filePath, headers, data);
        }

        public void ExportStockValuationCsv(string filePath, IList<StockValuationRow> rows)
        {
            var headers = new[] { "Medicine", "Usable Quantity", "Unit", "Purchase Cost Value (Rs)", "MRP Value (Rs)", "Status" };
            var data = new List<IList<string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[]
                {
                    r.MedicineName,
                    InvoiceCalculator.FormatQuantity(r.UsableQuantityMilli),
                    r.StockUnitName,
                    InvoiceCalculator.FormatRupees(r.PurchaseCostValuePaise),
                    InvoiceCalculator.FormatRupees(r.MrpValuePaise),
                    r.AlertText
                });
            }
            CsvExportHelper.WriteCsv(filePath, headers, data);
        }

        public IList<SalesReturnRegisterRow> GetSalesReturnsRegister(DateTime? fromDate, DateTime? toDate, string search = null)
        {
            var rows = new List<SalesReturnRegisterRow>();
            string fromIso = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string toIso = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : string.Empty;
            string q = (search ?? string.Empty).Trim().ToUpperInvariant();

            const string sql = @"
SELECT sr.return_id, sr.return_number, i.invoice_number, sr.returned_at_utc,
       sr.reason, sr.refund_method, sr.refund_amount_paise, sr.status
FROM sales_return sr
JOIN invoice i ON i.invoice_id = sr.invoice_id
WHERE (@from = '' OR sr.returned_at_utc >= @from)
  AND (@to = '' OR sr.returned_at_utc <= @to)
  AND (@q = '' OR UPPER(sr.return_number) LIKE @like ESCAPE '\' OR UPPER(i.invoice_number) LIKE @like ESCAPE '\' OR UPPER(COALESCE(sr.reason, '')) LIKE @like ESCAPE '\')
ORDER BY sr.returned_at_utc DESC;";

            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@from", fromIso);
                cmd.Parameters.AddWithValue("@to", toIso);
                cmd.Parameters.AddWithValue("@q", q);
                cmd.Parameters.AddWithValue("@like", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string utc = r.GetString(3);
                        rows.Add(new SalesReturnRegisterRow
                        {
                            ReturnId = r.GetString(0),
                            ReturnNumber = r.GetString(1),
                            InvoiceNumber = r.GetString(2),
                            ReturnedAtUtc = utc,
                            ReturnedDateText = FormatUtcToLocalDate(utc),
                            Reason = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                            RefundMethod = r.IsDBNull(5) ? string.Empty : r.GetString(5),
                            RefundAmountPaise = r.GetInt64(6),
                            Status = r.GetString(7)
                        });
                    }
                }
            }
            return rows;
        }

        public HistoricalInvoiceRecord GetInvoiceForReprint(string invoiceId)
        {
            if (string.IsNullOrWhiteSpace(invoiceId)) return null;
            using (var conn = _database.OpenConnection())
            {
                var record = new HistoricalInvoiceRecord { Tenders = new List<PaymentTenderViewModel>() };
                var preview = new InvoicePreviewResult { CanFinalize = true };
                var result = new InvoiceResult { Succeeded = true, InvoiceId = invoiceId };

                const string invSql = @"
SELECT invoice_number, issued_at_utc, customer_name, customer_phone, customer_address, customer_gstin,
       subtotal_paise, taxable_value_paise, cgst_paise, sgst_paise, igst_paise, round_off_paise, grand_total_paise
FROM invoice
WHERE invoice_id = @id;";

                using (var cmd = new SQLiteCommand(invSql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", invoiceId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        result.InvoiceNumber = r.GetString(0);
                        DateTime issuedUtc;
                        if (DateTime.TryParse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out issuedUtc))
                            result.IssuedAtUtc = issuedUtc.ToUniversalTime();
                        record.CustomerName = r.IsDBNull(2) ? string.Empty : r.GetString(2);
                        record.CustomerPhone = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                        record.CustomerAddress = r.IsDBNull(4) ? string.Empty : r.GetString(4);
                        record.CustomerGstin = r.IsDBNull(5) ? string.Empty : r.GetString(5);
                        preview.SubtotalPaise = r.GetInt64(6);
                        preview.TaxableValuePaise = r.GetInt64(7);
                        preview.CgstPaise = r.GetInt64(8);
                        preview.SgstPaise = r.GetInt64(9);
                        preview.IgstPaise = r.GetInt64(10);
                        preview.RoundOffPaise = r.GetInt64(11);
                        preview.GrandTotalPaise = r.GetInt64(12);
                        result.GrandTotalPaise = preview.GrandTotalPaise;
                    }
                }

                const string lineSql = @"
SELECT medicine_id, medicine_name_snapshot, unit_name_snapshot, hsn_code_snapshot, gst_rate_basis_points,
       batch_id, batch_number_snapshot, expiry_yyyymm_snapshot, quantity_milli, unit_price_paise,
       taxable_value_paise, cgst_paise, sgst_paise, igst_paise, line_total_paise
FROM invoice_line
WHERE invoice_id = @id
ORDER BY rowid ASC;";

                using (var cmd = new SQLiteCommand(lineSql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", invoiceId);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            long qty = r.GetInt64(8);
                            long unitPrice = r.GetInt64(9);
                            long taxVal = r.GetInt64(10);
                            long cgst = r.GetInt64(11);
                            long sgst = r.GetInt64(12);
                            long igst = r.GetInt64(13);
                            long lineTotal = r.GetInt64(14);
                            long subtotal = InvoiceCalculator.CalculateGrossPaise(qty, unitPrice);
                            var line = new InvoiceLinePreview
                            {
                                MedicineId = r.GetString(0),
                                MedicineName = r.GetString(1),
                                StockUnitName = r.GetString(2),
                                QuantityMilli = qty,
                                SubtotalPaise = subtotal,
                                TaxableValuePaise = taxVal,
                                CgstPaise = cgst,
                                SgstPaise = sgst,
                                IgstPaise = igst,
                                LineTotalPaise = lineTotal
                            };
                            line.BatchAllocations.Add(new BatchAllocationPreview
                            {
                                BatchNumber = r.GetString(6),
                                ExpiryYyyyMm = r.GetInt32(7),
                                QuantityMilli = qty,
                                UnitPricePaise = unitPrice,
                                SubtotalPaise = subtotal,
                                TaxableValuePaise = taxVal,
                                CgstPaise = cgst,
                                SgstPaise = sgst,
                                IgstPaise = igst,
                                LineTotalPaise = lineTotal
                            });
                            preview.Lines.Add(line);
                        }
                    }
                }

                const string paySql = @"
SELECT method, amount_paise, reference_text
FROM payment
WHERE invoice_id = @id
ORDER BY rowid ASC;";

                long paidSum = 0;
                using (var cmd = new SQLiteCommand(paySql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", invoiceId);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string m = r.GetString(0);
                            long amt = r.GetInt64(1);
                            string rf = r.IsDBNull(2) ? string.Empty : r.GetString(2);
                            paidSum = checked(paidSum + amt);
                            InvoicePaymentMethod pm = InvoicePaymentMethod.Cash;
                            if (string.Equals(m, "UPI", StringComparison.OrdinalIgnoreCase)) pm = InvoicePaymentMethod.Upi;
                            else if (string.Equals(m, "DEBIT_CARD", StringComparison.OrdinalIgnoreCase)) pm = InvoicePaymentMethod.DebitCard;
                            else if (string.Equals(m, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase)) pm = InvoicePaymentMethod.CreditCard;
                            record.Tenders.Add(new PaymentTenderViewModel
                            {
                                Method = pm,
                                AmountText = InvoiceCalculator.FormatRupees(amt),
                                ReferenceText = rf
                            });
                        }
                    }
                }

                preview.TenderTotalPaise = paidSum;
                preview.AmountRemainingPaise = Math.Max(0L, preview.GrandTotalPaise - paidSum);
                record.CommittedInvoice = result;
                record.Preview = preview;
                return record;
            }
        }

        public void ExportSalesReturnsCsv(string filePath, IList<SalesReturnRegisterRow> rows)
        {
            var headers = new[] { "Return Number", "Invoice Number", "Date", "Refund Method", "Refund (Rs)", "Status", "Reason" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ReturnNumber, r.InvoiceNumber, r.ReturnedDateText, r.RefundMethod, r.RefundText, r.Status, r.Reason });
            }
            CsvExportHelper.WriteCsv(filePath, headers, data);
        }

        public void ExportSalesReturnsXlsx(string filePath, IList<SalesReturnRegisterRow> rows)
        {
            var headers = new[] { "Return Number", "Invoice Number", "Date", "Refund Method", "Refund (Rs)", "Status", "Reason" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ReturnNumber, r.InvoiceNumber, r.ReturnedDateText, r.RefundMethod, r.RefundText, r.Status, r.Reason });
            }
            SpreadsheetExportHelper.WriteXlsx(filePath, "Sales Returns", headers, data, new[] { false, false, false, false, true, false, false });
        }

        public void ExportExpensesCsv(string filePath, IList<ExpenseRow> rows)
        {
            var headers = new[] { "Date", "Category", "Description", "Payment Method", "Document Ref", "Amount (Rs)" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ExpenseDateText, r.Category, r.Description, r.PaymentMethod, r.DocumentReference, r.AmountText });
            }
            CsvExportHelper.WriteCsv(filePath, headers, data);
        }

        public void ExportExpensesXlsx(string filePath, IList<ExpenseRow> rows)
        {
            var headers = new[] { "Date", "Category", "Description", "Payment Method", "Document Ref", "Amount (Rs)" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ExpenseDateText, r.Category, r.Description, r.PaymentMethod, r.DocumentReference, r.AmountText });
            }
            SpreadsheetExportHelper.WriteXlsx(filePath, "Expenses", headers, data, new[] { false, false, false, false, false, true });
        }

        public void ExportSalesRegisterXlsx(string filePath, IList<SalesRegisterRow> rows)
        {
            var headers = new[] { "Invoice Number", "Date", "Customer", "Customer GSTIN", "Taxable (Rs)", "CGST (Rs)", "SGST (Rs)", "IGST (Rs)", "Round Off (Rs)", "Grand Total (Rs)", "Paid (Rs)", "Status", "Cancellation Reason" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.InvoiceNumber, r.IssuedDateText, r.CustomerName, r.CustomerGstin, r.TaxableText, InvoiceCalculator.FormatRupees(r.CgstPaise), InvoiceCalculator.FormatRupees(r.SgstPaise), InvoiceCalculator.FormatRupees(r.IgstPaise), InvoiceCalculator.FormatRupees(r.RoundOffPaise), r.GrandTotalText, r.PaidText, r.Status, r.CancellationReason });
            }
            SpreadsheetExportHelper.WriteXlsx(filePath, "Sales Register", headers, data, new[] { false, false, false, false, true, true, true, true, true, true, true, false, false });
        }

        public void ExportPurchaseRegisterXlsx(string filePath, IList<PurchaseRegisterRow> rows)
        {
            var headers = new[] { "Date", "Supplier", "Document Type", "Document Reference", "Total (Rs)", "Tax Invoice" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ReceivedDateText, r.SupplierName, r.DocumentType, r.DocumentReference, r.TotalText, r.IsDocumentedForReview ? "Yes" : "No" });
            }
            SpreadsheetExportHelper.WriteXlsx(filePath, "Purchase Register", headers, data, new[] { false, false, false, false, true, false });
        }

        public void ExportGstSummaryXlsx(string filePath, IList<GstSummaryRow> rows)
        {
            var headers = new[] { "HSN Code", "GST Rate", "Net Taxable (Rs)", "Net CGST (Rs)", "Net SGST (Rs)", "Net IGST (Rs)", "Net Total (Rs)" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.HsnCode, r.GstRateText, r.TaxableText, r.CgstText, r.SgstText, r.IgstText, r.TotalText });
            }
            SpreadsheetExportHelper.WriteXlsx(filePath, "GST Summary", headers, data, new[] { false, false, true, true, true, true, true });
        }

        public void ExportStockValuationXlsx(string filePath, IList<StockValuationRow> rows)
        {
            var headers = new[] { "Medicine", "Usable Stock", "Purchase Cost Value (Rs)", "MRP Value (Rs)", "Status" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.MedicineName, r.QuantityText, r.CostValueText, r.MrpValueText, r.AlertText });
            }
            SpreadsheetExportHelper.WriteXlsx(filePath, "Stock Valuation", headers, data, new[] { false, false, true, true, false });
        }

        public void ExportSalesRegisterPdf(string filePath, IList<SalesRegisterRow> rows, string subtitle = null)
        {
            var headers = new[] { "Invoice No", "Date", "Customer", "Taxable", "Tax", "Total", "Paid", "Status" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.InvoiceNumber, r.IssuedDateText, r.CustomerName, r.TaxableText, r.TaxTotalText, r.GrandTotalText, r.PaidText, r.Status });
            }
            PdfExportHelper.WriteTablePdf(filePath, "ORVA - Sales Register", subtitle ?? ("Records: " + rows.Count), headers, data);
        }

        public void ExportPurchaseRegisterPdf(string filePath, IList<PurchaseRegisterRow> rows, string subtitle = null)
        {
            var headers = new[] { "Date", "Supplier", "Document Type", "Reference", "Total (Rs)", "Tax Inv" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ReceivedDateText, r.SupplierName, r.DocumentType, r.DocumentReference, r.TotalText, r.IsDocumentedForReview ? "Yes" : "No" });
            }
            PdfExportHelper.WriteTablePdf(filePath, "ORVA - Purchase Register", subtitle ?? ("Records: " + rows.Count), headers, data);
        }

        public void ExportGstSummaryPdf(string filePath, IList<GstSummaryRow> rows, string subtitle = null)
        {
            var headers = new[] { "HSN Code", "GST Rate", "Net Taxable", "Net CGST", "Net SGST", "Net IGST", "Net Total" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.HsnCode, r.GstRateText, r.TaxableText, r.CgstText, r.SgstText, r.IgstText, r.TotalText });
            }
            PdfExportHelper.WriteTablePdf(filePath, "ORVA - GST / HSN Summary (Net of Returns)", subtitle ?? ("Groups: " + rows.Count), headers, data);
        }

        public void ExportSalesReturnsPdf(string filePath, IList<SalesReturnRegisterRow> rows, string subtitle = null)
        {
            var headers = new[] { "Return No", "Invoice No", "Date", "Method", "Refund (Rs)", "Status", "Reason" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ReturnNumber, r.InvoiceNumber, r.ReturnedDateText, r.RefundMethod, r.RefundText, r.Status, r.Reason });
            }
            PdfExportHelper.WriteTablePdf(filePath, "ORVA - Sales Returns Register", subtitle ?? ("Records: " + rows.Count), headers, data);
        }

        public void ExportStockValuationPdf(string filePath, IList<StockValuationRow> rows, string subtitle = null)
        {
            var headers = new[] { "Medicine", "Usable Stock", "Cost Value (Rs)", "MRP Value (Rs)", "Status" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.MedicineName, r.QuantityText, r.CostValueText, r.MrpValueText, r.AlertText });
            }
            PdfExportHelper.WriteTablePdf(filePath, "ORVA - Current Stock Valuation", subtitle ?? ("Medicines: " + rows.Count), headers, data);
        }

        public void ExportExpensesPdf(string filePath, IList<ExpenseRow> rows, string subtitle = null)
        {
            var headers = new[] { "Date", "Category", "Description", "Method", "Ref", "Amount (Rs)" };
            var data = new List<IList<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                data.Add(new[] { r.ExpenseDateText, r.Category, r.Description, r.PaymentMethod, r.DocumentReference, r.AmountText });
            }
            PdfExportHelper.WriteTablePdf(filePath, "ORVA - Shop Expense Register", subtitle ?? ("Records: " + rows.Count), headers, data);
        }

        private static string FormatUtcToLocalDate(string utcIso)
        {
            DateTime dt;
            if (DateTime.TryParse(utcIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                return dt.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt", CultureInfo.CurrentCulture);
            return utcIso ?? string.Empty;
        }
    }
}
