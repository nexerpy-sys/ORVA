using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Web.Script.Serialization;
using Orva.Infrastructure.Database;
using Orva.Services.Billing;

namespace Orva.Services
{
    public sealed class ReturnableInvoiceSummary
    {
        public string InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string IssuedAtUtc { get; set; }
        public string CustomerName { get; set; }
        public long GrandTotalPaise { get; set; }
        public string Status { get; set; }
    }

    public sealed class ReturnableInvoiceLine
    {
        public string InvoiceLineId { get; set; }
        public string MedicineId { get; set; }
        public string BatchId { get; set; }
        public string MedicineName { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYyyyMm { get; set; }
        public string StockUnitName { get; set; }
        public long OriginalQuantityMilli { get; set; }
        public long AlreadyReturnedMilli { get; set; }
        public long RemainingReturnableMilli { get { return Math.Max(0L, OriginalQuantityMilli - AlreadyReturnedMilli); } }
        public long UnitPricePaise { get; set; }
        public long OriginalLineTotalPaise { get; set; }
        public long AlreadyRefundedPaise { get; set; }
        public long RemainingRefundablePaise { get { return Math.Max(0L, OriginalLineTotalPaise - AlreadyRefundedPaise); } }
        public string BatchStatus { get; set; }
        public int GstRateBasisPoints { get; set; }
        public string HsnCode { get; set; }
    }

    public sealed class SalesReturnLineRequest
    {
        public string InvoiceLineId { get; set; }
        public long ReturnQuantityMilli { get; set; }
        /// <summary>QUARANTINE (default), RESTOCK, DAMAGED, or EXPIRED.</summary>
        public string ResaleDisposition { get; set; }
    }

    public sealed class SalesReturnRequest
    {
        public SalesReturnRequest()
        {
            Lines = new List<SalesReturnLineRequest>();
        }

        public string InvoiceId { get; set; }
        public string Reason { get; set; }
        /// <summary>CASH, UPI, DEBIT_CARD, CREDIT_CARD, or ADJUSTMENT.</summary>
        public string RefundMethod { get; set; }
        public List<SalesReturnLineRequest> Lines { get; private set; }
    }

    public sealed class SalesReturnPreviewResult
    {
        public SalesReturnPreviewResult()
        {
            Errors = new List<string>();
        }

        public bool CanFinalize { get; set; }
        public long TotalRefundPaise { get; set; }
        public int QuarantinedLineCount { get; set; }
        public int RestockedLineCount { get; set; }
        public List<string> Errors { get; private set; }
    }

    public sealed class SalesReturnResult
    {
        public bool Succeeded { get; set; }
        public string ReturnId { get; set; }
        public string ReturnNumber { get; set; }
        public long RefundAmountPaise { get; set; }
        public DateTime? ReturnedAtUtc { get; set; }
        public bool SnapshotRefreshed { get; set; }
        public string SnapshotWarning { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Transactional Sales Return engine. Defaults returned items to QUARANTINE so damaged,
    /// expired, or unverified returns never silently re-enter sellable FEFO stock.
    /// </summary>
    public sealed class SalesReturnService
    {
        private static readonly string[] AllowedDispositions = new[] { "QUARANTINE", "RESTOCK", "DAMAGED", "EXPIRED" };
        private static readonly string[] AllowedRefundMethods = new[] { "CASH", "UPI", "DEBIT_CARD", "CREDIT_CARD", "ADJUSTMENT" };

        private readonly SqliteDatabase _database;
        private readonly StockSnapshotWriter _snapshotWriter;
        private readonly IBillingClock _clock;

        public SalesReturnService(SqliteDatabase database, StockSnapshotWriter snapshotWriter = null, IBillingClock clock = null)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
            _snapshotWriter = snapshotWriter;
            _clock = clock ?? new SystemBillingClock();
        }

        public IList<ReturnableInvoiceSummary> SearchReturnableInvoices(string query, int limit = 30)
        {
            if (limit <= 0) limit = 30;
            var list = new List<ReturnableInvoiceSummary>();
            string q = (query ?? string.Empty).Trim().ToUpperInvariant();

            const string sql = @"
SELECT invoice_id, invoice_number, issued_at_utc, customer_name, grand_total_paise, status
FROM invoice
WHERE status <> 'CANCELLED'
  AND (@q = '' OR UPPER(invoice_number) LIKE @like ESCAPE '\' OR UPPER(COALESCE(customer_name, '')) LIKE @like ESCAPE '\' OR issued_at_utc LIKE @like ESCAPE '\')
ORDER BY issued_at_utc DESC
LIMIT @limit;";

            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@q", q);
                cmd.Parameters.AddWithValue("@like", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
                cmd.Parameters.AddWithValue("@limit", limit);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new ReturnableInvoiceSummary
                        {
                            InvoiceId = r.GetString(0),
                            InvoiceNumber = r.GetString(1),
                            IssuedAtUtc = r.GetString(2),
                            CustomerName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                            GrandTotalPaise = r.GetInt64(4),
                            Status = r.GetString(5)
                        });
                    }
                }
            }
            return list;
        }

        public IList<ReturnableInvoiceLine> GetReturnableLines(string invoiceId)
        {
            var lines = new List<ReturnableInvoiceLine>();
            if (string.IsNullOrWhiteSpace(invoiceId)) return lines;

            const string sql = @"
SELECT il.invoice_line_id, il.medicine_id, il.batch_id, il.medicine_name_snapshot,
       il.batch_number_snapshot, il.expiry_yyyymm_snapshot, il.unit_name_snapshot,
       il.quantity_milli, il.unit_price_paise, il.line_total_paise,
       il.gst_rate_basis_points, il.hsn_code_snapshot,
       COALESCE((SELECT SUM(srl.quantity_milli)
                 FROM sales_return_line srl
                 JOIN sales_return sr ON sr.return_id = srl.return_id
                 WHERE srl.invoice_line_id = il.invoice_line_id
                   AND sr.status = 'COMPLETED'), 0) AS returned_qty,
       COALESCE((SELECT SUM(srl.line_refund_paise)
                 FROM sales_return_line srl
                 JOIN sales_return sr ON sr.return_id = srl.return_id
                 WHERE srl.invoice_line_id = il.invoice_line_id
                   AND sr.status = 'COMPLETED'), 0) AS refunded_paise,
       COALESCE(sb.batch_status, 'INACTIVE') AS batch_status
FROM invoice_line il
LEFT JOIN stock_batch sb ON sb.batch_id = il.batch_id
WHERE il.invoice_id = @id
ORDER BY il.created_at_utc ASC;";

            using (var conn = _database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", invoiceId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        lines.Add(new ReturnableInvoiceLine
                        {
                            InvoiceLineId = r.GetString(0),
                            MedicineId = r.GetString(1),
                            BatchId = r.GetString(2),
                            MedicineName = r.GetString(3),
                            BatchNumber = r.GetString(4),
                            ExpiryYyyyMm = r.GetInt32(5),
                            StockUnitName = r.GetString(6),
                            OriginalQuantityMilli = r.GetInt64(7),
                            UnitPricePaise = r.GetInt64(8),
                            OriginalLineTotalPaise = r.GetInt64(9),
                            GstRateBasisPoints = r.GetInt32(10),
                            HsnCode = r.IsDBNull(11) ? null : r.GetString(11),
                            AlreadyReturnedMilli = r.GetInt64(12),
                            AlreadyRefundedPaise = r.GetInt64(13),
                            BatchStatus = r.GetString(14)
                        });
                    }
                }
            }
            return lines;
        }

        public SalesReturnPreviewResult PreviewReturn(SalesReturnRequest request)
        {
            var preview = new SalesReturnPreviewResult();
            if (request == null || string.IsNullOrWhiteSpace(request.InvoiceId))
            {
                preview.Errors.Add("Select an original invoice first.");
                return preview;
            }

            if (request.Lines == null || request.Lines.Count == 0)
            {
                preview.Errors.Add("Select at least one medicine line to return.");
                return preview;
            }

            string refundMethod = NormalizeRefundMethod(request.RefundMethod);
            if (refundMethod == null)
                preview.Errors.Add("Select a valid refund method (Cash, UPI, Debit Card, Credit Card, or Adjustment).");

            if (string.IsNullOrWhiteSpace(request.Reason))
                preview.Errors.Add("Enter a reason for the sales return.");

            using (var conn = _database.OpenConnection())
            using (var stCmd = new SQLiteCommand("SELECT status FROM invoice WHERE invoice_id = @id;", conn))
            {
                stCmd.Parameters.AddWithValue("@id", request.InvoiceId);
                object stObj = stCmd.ExecuteScalar();
                if (stObj == null || stObj == DBNull.Value)
                {
                    preview.Errors.Add("Original invoice was not found.");
                    return preview;
                }
                if (string.Equals(Convert.ToString(stObj), "CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    preview.Errors.Add("Cannot process a sales return against a cancelled invoice.");
                    return preview;
                }
            }

            var availableLines = GetReturnableLines(request.InvoiceId);
            var byId = new Dictionary<string, ReturnableInvoiceLine>(StringComparer.Ordinal);
            for (int i = 0; i < availableLines.Count; i++) byId[availableLines[i].InvoiceLineId] = availableLines[i];

            int currentMonth = _clock.CurrentExpiryYearMonth;
            long totalRefund = 0;
            var requestedByLine = new Dictionary<string, long>(StringComparer.Ordinal);

            for (int i = 0; i < request.Lines.Count; i++)
            {
                var reqLine = request.Lines[i];
                if (reqLine == null || reqLine.ReturnQuantityMilli <= 0) continue;

                ReturnableInvoiceLine orig;
                if (!byId.TryGetValue(reqLine.InvoiceLineId ?? string.Empty, out orig))
                {
                    preview.Errors.Add("One of the selected return items does not belong to this invoice.");
                    continue;
                }

                long prevQty;
                requestedByLine.TryGetValue(orig.InvoiceLineId, out prevQty);
                long cumulativeQty = checked(prevQty + reqLine.ReturnQuantityMilli);
                requestedByLine[orig.InvoiceLineId] = cumulativeQty;

                if (cumulativeQty > orig.RemainingReturnableMilli)
                {
                    preview.Errors.Add("Return quantity for " + orig.MedicineName + " exceeds remaining sold quantity.");
                    continue;
                }

                string disp = NormalizeDisposition(reqLine.ResaleDisposition);
                if (disp == null)
                {
                    preview.Errors.Add("Invalid resale disposition for " + orig.MedicineName + ".");
                    continue;
                }

                if (disp == "RESTOCK" && (orig.ExpiryYyyyMm < currentMonth || !string.Equals(orig.BatchStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
                {
                    preview.Errors.Add(orig.MedicineName + " (Batch " + orig.BatchNumber + ") is expired or not active and cannot be restocked. Keep it quarantined.");
                    continue;
                }

                long lineRefund = CalculateClampedLineRefundPaise(orig, reqLine.ReturnQuantityMilli);
                totalRefund = checked(totalRefund + lineRefund);

                if (disp == "RESTOCK") preview.RestockedLineCount++;
                else preview.QuarantinedLineCount++;
            }

            if (totalRefund <= 0 && preview.Errors.Count == 0)
                preview.Errors.Add("Enter a return quantity greater than zero for at least one medicine.");

            if (preview.Errors.Count == 0 && totalRefund > 0)
            {
                using (var conn = _database.OpenConnection())
                using (var capCmd = new SQLiteCommand(
                    "SELECT i.grand_total_paise, COALESCE((SELECT SUM(sr.refund_amount_paise) FROM sales_return sr WHERE sr.invoice_id = i.invoice_id AND sr.status = 'COMPLETED'), 0) FROM invoice i WHERE i.invoice_id = @id;",
                    conn))
                {
                    capCmd.Parameters.AddWithValue("@id", request.InvoiceId);
                    using (var cr = capCmd.ExecuteReader())
                    {
                        if (cr.Read())
                        {
                            long maxInvRefundable = Math.Max(0L, cr.GetInt64(0) - cr.GetInt64(1));
                            bool returningAllRemaining = true;
                            for (int a = 0; a < availableLines.Count; a++)
                            {
                                long reqSum;
                                requestedByLine.TryGetValue(availableLines[a].InvoiceLineId, out reqSum);
                                if (availableLines[a].RemainingReturnableMilli > reqSum) { returningAllRemaining = false; break; }
                            }
                            long sumLines = totalRefund;
                            if (returningAllRemaining) totalRefund = maxInvRefundable;
                            else if (totalRefund > maxInvRefundable) totalRefund = maxInvRefundable;

                            long delta = totalRefund - sumLines;
                            if (delta != 0L && preparedLines.Count > 0)
                            {
                                int lastIdx = preparedLines.Count - 1;
                                var lastItem = preparedLines[lastIdx];
                                long adjusted = Math.Max(0L, lastItem.Item4 + delta);
                                preparedLines[lastIdx] = Tuple.Create(lastItem.Item1, lastItem.Item2, lastItem.Item3, adjusted);
                            }
                        }
                    }
                }
            }

            preview.TotalRefundPaise = totalRefund;
            preview.CanFinalize = preview.Errors.Count == 0 && totalRefund > 0;
            return preview;
        }

        public SalesReturnResult FinalizeReturn(SalesReturnRequest request)
        {
            var preview = PreviewReturn(request);
            if (!preview.CanFinalize)
            {
                return new SalesReturnResult
                {
                    Succeeded = false,
                    ErrorMessage = preview.Errors.Count > 0 ? preview.Errors[0] : "Sales return could not be validated."
                };
            }

            BillingInstant now = BillingInstant.Capture(_clock);
            DateTime utcNow = now.UtcNow;
            DateTime localNow = now.LocalNow;
            string utcText = utcNow.ToString("o", CultureInfo.InvariantCulture);
            string localText = localNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int currentMonth = localNow.Year * 100 + localNow.Month;

            string returnId = Guid.NewGuid().ToString("N");
            string returnNumber;
            long totalRefund = 0;
            bool stockChanged = false;

            try
            {
            using (var conn = _database.OpenConnection())
            using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
            {
                string invStatus;
                string invNumber;
                int fyStartMonth = 4;
                using (var invCmd = new SQLiteCommand("SELECT status, invoice_number FROM invoice WHERE invoice_id = @id;", conn, tx))
                {
                    invCmd.Parameters.AddWithValue("@id", request.InvoiceId);
                    using (var r = invCmd.ExecuteReader())
                    {
                        if (!r.Read())
                            return Fail("Original invoice was not found.");
                        invStatus = r.GetString(0);
                        invNumber = r.GetString(1);
                    }
                }

                if (string.Equals(invStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    return Fail("Cannot process a sales return against a cancelled invoice.");

                using (var fyCmd = new SQLiteCommand("SELECT financial_year_start_month FROM shop_profile WHERE profile_id = 1;", conn, tx))
                {
                    var obj = fyCmd.ExecuteScalar();
                    if (obj != null && obj != DBNull.Value) fyStartMonth = Convert.ToInt32(obj, CultureInfo.InvariantCulture);
                }

                string fyLabel = InvoiceCalculator.GetFinancialYearLabel(localNow, fyStartMonth);
                long seq = NextReturnSequence(conn, tx, fyLabel, utcText);
                returnNumber = string.Format(CultureInfo.InvariantCulture, "RET/{0}/{1:D6}", fyLabel, seq);

                // Re-read returnable lines inside the write transaction
                var origLines = ReadLinesInTransaction(conn, tx, request.InvoiceId);
                var preparedLines = new List<Tuple<ReturnableInvoiceLine, long, string, long>>();
                var txRequestedByLine = new Dictionary<string, long>(StringComparer.Ordinal);

                for (int i = 0; i < request.Lines.Count; i++)
                {
                    var reqLine = request.Lines[i];
                    if (reqLine == null || reqLine.ReturnQuantityMilli <= 0) continue;

                    ReturnableInvoiceLine orig;
                    if (!origLines.TryGetValue(reqLine.InvoiceLineId, out orig))
                        throw new InvalidOperationException("Return line does not belong to invoice.");

                    long prevTxQty;
                    txRequestedByLine.TryGetValue(orig.InvoiceLineId, out prevTxQty);
                    long cumTxQty = checked(prevTxQty + reqLine.ReturnQuantityMilli);
                    txRequestedByLine[orig.InvoiceLineId] = cumTxQty;

                    if (cumTxQty > orig.RemainingReturnableMilli)
                        throw new InvalidOperationException("Return quantity exceeds remaining sold quantity for " + orig.MedicineName + ".");

                    string disp = NormalizeDisposition(reqLine.ResaleDisposition) ?? "QUARANTINE";
                    if (disp == "RESTOCK" && (orig.ExpiryYyyyMm < currentMonth || !string.Equals(orig.BatchStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Expired or non-active batch cannot be restocked.");

                    long lineRefund = CalculateClampedLineRefundPaise(orig, reqLine.ReturnQuantityMilli);
                    totalRefund = checked(totalRefund + lineRefund);
                    preparedLines.Add(Tuple.Create(orig, reqLine.ReturnQuantityMilli, disp, lineRefund));
                }

                if (preparedLines.Count == 0)
                    return Fail("No valid return quantities were provided.");

                // B2-02: Reconcile totalRefund against (invoice.grand_total_paise - prior completed returns)
                // so NEAREST_RUPEE round-off never causes an over-refund on full returns.
                using (var capCmd = new SQLiteCommand(
                    "SELECT i.grand_total_paise, COALESCE((SELECT SUM(sr.refund_amount_paise) FROM sales_return sr WHERE sr.invoice_id = i.invoice_id AND sr.status = 'COMPLETED'), 0) FROM invoice i WHERE i.invoice_id = @id;",
                    conn, tx))
                {
                    capCmd.Parameters.AddWithValue("@id", request.InvoiceId);
                    using (var cr = capCmd.ExecuteReader())
                    {
                        if (cr.Read())
                        {
                            long maxInvRefundable = Math.Max(0L, cr.GetInt64(0) - cr.GetInt64(1));
                            bool returningAllRemaining = true;
                            foreach (var kv in origLines)
                            {
                                long reqSum;
                                txRequestedByLine.TryGetValue(kv.Key, out reqSum);
                                if (kv.Value.RemainingReturnableMilli > reqSum) { returningAllRemaining = false; break; }
                            }
                            if (returningAllRemaining) totalRefund = maxInvRefundable;
                            else if (totalRefund > maxInvRefundable) totalRefund = maxInvRefundable;
                        }
                    }
                }

                string refundMethod = NormalizeRefundMethod(request.RefundMethod) ?? "CASH";
                string reason = request.Reason.Trim();

                using (var insRet = new SQLiteCommand(@"
INSERT INTO sales_return(return_id, return_number, invoice_id, returned_at_utc, reason, refund_method, refund_amount_paise, status, created_at_utc)
VALUES(@id, @num, @inv, @ret_utc, @reason, @method, @refund, 'COMPLETED', @created);", conn, tx))
                {
                    insRet.Parameters.AddWithValue("@id", returnId);
                    insRet.Parameters.AddWithValue("@num", returnNumber);
                    insRet.Parameters.AddWithValue("@inv", request.InvoiceId);
                    insRet.Parameters.AddWithValue("@ret_utc", utcText);
                    insRet.Parameters.AddWithValue("@reason", reason);
                    insRet.Parameters.AddWithValue("@method", refundMethod);
                    insRet.Parameters.AddWithValue("@refund", totalRefund);
                    insRet.Parameters.AddWithValue("@created", utcText);
                    insRet.ExecuteNonQuery();
                }

                for (int i = 0; i < preparedLines.Count; i++)
                {
                    var item = preparedLines[i];
                    var orig = item.Item1;
                    long retQty = item.Item2;
                    string disp = item.Item3;
                    long lineRefund = item.Item4;

                    using (var insLine = new SQLiteCommand(@"
INSERT INTO sales_return_line(return_line_id, return_id, invoice_line_id, batch_id, quantity_milli, resale_disposition, line_refund_paise)
VALUES(@id, @ret_id, @inv_line, @batch, @qty, @disp, @refund);", conn, tx))
                    {
                        insLine.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                        insLine.Parameters.AddWithValue("@ret_id", returnId);
                        insLine.Parameters.AddWithValue("@inv_line", orig.InvoiceLineId);
                        insLine.Parameters.AddWithValue("@batch", orig.BatchId);
                        insLine.Parameters.AddWithValue("@qty", retQty);
                        insLine.Parameters.AddWithValue("@disp", disp);
                        insLine.Parameters.AddWithValue("@refund", lineRefund);
                        insLine.ExecuteNonQuery();
                    }

                    // Only verified eligible RESTOCK items increment active sellable batch stock.
                    // QUARANTINE / DAMAGED / EXPIRED stay out of sellable FEFO stock.
                    if (disp == "RESTOCK")
                    {
                        using (var updBatch = new SQLiteCommand(@"
UPDATE stock_batch
SET quantity_milli = quantity_milli + @qty,
    updated_at_utc = @updated
WHERE batch_id = @batch
  AND batch_status = 'ACTIVE'
  AND expiry_yyyymm >= @current_month;", conn, tx))
                        {
                            updBatch.Parameters.AddWithValue("@qty", retQty);
                            updBatch.Parameters.AddWithValue("@updated", utcText);
                            updBatch.Parameters.AddWithValue("@batch", orig.BatchId);
                            updBatch.Parameters.AddWithValue("@current_month", currentMonth);
                            int affected = updBatch.ExecuteNonQuery();
                            if (affected != 1)
                                throw new InvalidOperationException("Batch " + orig.BatchNumber + " is not eligible for restocking. Choose Quarantine instead.");
                        }

                        using (var insMov = new SQLiteCommand(@"
INSERT INTO stock_movement(movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli, reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(@id, @batch, @med, 'SALE_RETURN_RESTOCK', @qty, 'SALES_RETURN', @ref_id, @reason, @utc, @local);", conn, tx))
                        {
                            insMov.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                            insMov.Parameters.AddWithValue("@batch", orig.BatchId);
                            insMov.Parameters.AddWithValue("@med", orig.MedicineId);
                            insMov.Parameters.AddWithValue("@qty", retQty);
                            insMov.Parameters.AddWithValue("@ref_id", returnId);
                            insMov.Parameters.AddWithValue("@reason", "Return " + returnNumber + " restocked: " + reason);
                            insMov.Parameters.AddWithValue("@utc", utcText);
                            insMov.Parameters.AddWithValue("@local", localText);
                            insMov.ExecuteNonQuery();
                        }

                        stockChanged = true;
                    }
                }

                // Mark original invoice as CREDIT_NOTE_ISSUED (never delete original invoice)
                using (var updInv = new SQLiteCommand("UPDATE invoice SET status = 'CREDIT_NOTE_ISSUED' WHERE invoice_id = @id;", conn, tx))
                {
                    updInv.Parameters.AddWithValue("@id", request.InvoiceId);
                    updInv.ExecuteNonQuery();
                }

                var serializer = new JavaScriptSerializer();
                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "returnId", returnId },
                    { "returnNumber", returnNumber },
                    { "invoiceId", request.InvoiceId },
                    { "invoiceNumber", invNumber },
                    { "refundMethod", refundMethod },
                    { "refundAmountPaise", totalRefund },
                    { "lineCount", preparedLines.Count },
                    { "stockRestocked", stockChanged }
                });

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'SALES_RETURN_CREATED', 'SALES_RETURN', @entity, @summary, NULL, @after, @utc, @local);", conn, tx))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", returnId);
                    audit.Parameters.AddWithValue("@summary", string.Format(
                        CultureInfo.InvariantCulture,
                        "Processed return {0} against invoice {1} for Rs. {2} ({3}): {4}",
                        returnNumber,
                        invNumber,
                        InvoiceCalculator.FormatRupees(totalRefund),
                        refundMethod,
                        reason));
                    audit.Parameters.AddWithValue("@after", afterJson);
                    audit.Parameters.AddWithValue("@utc", utcText);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                if (stockChanged)
                {
                    StockSnapshotState.MarkDirtyAndIncrementGeneration(conn, tx, utcText);
                }

                tx.Commit();
            }
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
            catch (OverflowException)
            {
                return Fail("Return quantity or amount exceeded supported numeric limits.");
            }
            catch (SQLiteException ex)
            {
                return Fail("Database error while saving return: " + ex.Message);
            }

            var result = new SalesReturnResult
            {
                Succeeded = true,
                ReturnId = returnId,
                ReturnNumber = returnNumber,
                RefundAmountPaise = totalRefund,
                ReturnedAtUtc = utcNow
            };

            if (stockChanged && _snapshotWriter != null)
            {
                try
                {
                    bool snapshotClean = _snapshotWriter.WriteCurrentSnapshot();
                    result.SnapshotRefreshed = snapshotClean;
                    if (!snapshotClean)
                        result.SnapshotWarning = "Return was saved, but newer stock changes occurred while refreshing StockData.json. It remains queued for refresh.";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("Snapshot refresh failed after return: " + ex);
                    result.SnapshotWarning = "Return was saved, but StockData.json could not be refreshed yet.";
                }
            }

            return result;
        }

        public static long CalculateClampedLineRefundPaise(ReturnableInvoiceLine orig, long returnQtyMilli)
        {
            if (orig == null || orig.OriginalQuantityMilli <= 0L || returnQtyMilli <= 0L) return 0L;
            long remainingRefundable = orig.RemainingRefundablePaise;
            if (remainingRefundable <= 0L) return 0L;
            if (returnQtyMilli >= orig.RemainingReturnableMilli) return remainingRefundable;
            long raw = CalculateProportionalRefundPaise(orig.OriginalLineTotalPaise, orig.OriginalQuantityMilli, returnQtyMilli);
            return Math.Min(raw, remainingRefundable);
        }

        public static long CalculateProportionalRefundPaise(long originalLineTotalPaise, long originalQtyMilli, long returnQtyMilli)
        {
            if (originalQtyMilli <= 0) return 0;
            if (returnQtyMilli == originalQtyMilli) return originalLineTotalPaise;
            decimal proportional = ((decimal)originalLineTotalPaise * (decimal)returnQtyMilli) / (decimal)originalQtyMilli;
            return checked((long)Math.Round(proportional, 0, MidpointRounding.AwayFromZero));
        }

        private static long NextReturnSequence(SQLiteConnection conn, SQLiteTransaction tx, string fyLabel, string utcText)
        {
            string key = "return_seq_" + fyLabel;
            long current = 0;
            using (var sel = new SQLiteCommand("SELECT setting_value FROM app_setting WHERE setting_key = @k;", conn, tx))
            {
                sel.Parameters.AddWithValue("@k", key);
                var obj = sel.ExecuteScalar();
                if (obj != null && obj != DBNull.Value)
                    long.TryParse(Convert.ToString(obj, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out current);
            }
            long next = checked(current + 1L);
            StockSnapshotState.UpsertSetting(conn, tx, key, next.ToString(CultureInfo.InvariantCulture), utcText);
            return next;
        }

        private static Dictionary<string, ReturnableInvoiceLine> ReadLinesInTransaction(SQLiteConnection conn, SQLiteTransaction tx, string invoiceId)
        {
            var dict = new Dictionary<string, ReturnableInvoiceLine>(StringComparer.Ordinal);
            const string sql = @"
SELECT il.invoice_line_id, il.medicine_id, il.batch_id, il.medicine_name_snapshot,
       il.batch_number_snapshot, il.expiry_yyyymm_snapshot, il.unit_name_snapshot,
       il.quantity_milli, il.unit_price_paise, il.line_total_paise,
       il.gst_rate_basis_points, il.hsn_code_snapshot,
       COALESCE((SELECT SUM(srl.quantity_milli)
                 FROM sales_return_line srl
                 JOIN sales_return sr ON sr.return_id = srl.return_id
                 WHERE srl.invoice_line_id = il.invoice_line_id
                   AND sr.status = 'COMPLETED'), 0) AS returned_qty,
       COALESCE((SELECT SUM(srl.line_refund_paise)
                 FROM sales_return_line srl
                 JOIN sales_return sr ON sr.return_id = srl.return_id
                 WHERE srl.invoice_line_id = il.invoice_line_id
                   AND sr.status = 'COMPLETED'), 0) AS refunded_paise,
       COALESCE(sb.batch_status, 'INACTIVE') AS batch_status
FROM invoice_line il
LEFT JOIN stock_batch sb ON sb.batch_id = il.batch_id
WHERE il.invoice_id = @id;";

            using (var cmd = new SQLiteCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", invoiceId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var line = new ReturnableInvoiceLine
                        {
                            InvoiceLineId = r.GetString(0),
                            MedicineId = r.GetString(1),
                            BatchId = r.GetString(2),
                            MedicineName = r.GetString(3),
                            BatchNumber = r.GetString(4),
                            ExpiryYyyyMm = r.GetInt32(5),
                            StockUnitName = r.GetString(6),
                            OriginalQuantityMilli = r.GetInt64(7),
                            UnitPricePaise = r.GetInt64(8),
                            OriginalLineTotalPaise = r.GetInt64(9),
                            GstRateBasisPoints = r.GetInt32(10),
                            HsnCode = r.IsDBNull(11) ? null : r.GetString(11),
                            AlreadyReturnedMilli = r.GetInt64(12),
                            AlreadyRefundedPaise = r.GetInt64(13),
                            BatchStatus = r.GetString(14)
                        };
                        dict[line.InvoiceLineId] = line;
                    }
                }
            }
            return dict;
        }

        private static string NormalizeDisposition(string raw)
        {
            string s = string.IsNullOrWhiteSpace(raw) ? "QUARANTINE" : raw.Trim().ToUpperInvariant();
            return Array.IndexOf(AllowedDispositions, s) >= 0 ? s : null;
        }

        private static string NormalizeRefundMethod(string raw)
        {
            string s = string.IsNullOrWhiteSpace(raw) ? "CASH" : raw.Trim().ToUpperInvariant();
            return Array.IndexOf(AllowedRefundMethods, s) >= 0 ? s : null;
        }

        private static SalesReturnResult Fail(string msg)
        {
            return new SalesReturnResult { Succeeded = false, ErrorMessage = msg };
        }
    }
}
