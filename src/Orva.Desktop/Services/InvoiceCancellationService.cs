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
    public sealed class InvoiceCancellationResult
    {
        public bool Succeeded { get; set; }
        public string InvoiceNumber { get; set; }
        public bool StockRestored { get; set; }
        public bool SnapshotRefreshed { get; set; }
        public string SnapshotWarning { get; set; }
        public string StockWarning { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Safe invoice cancellation (voiding) distinct from Sales Returns.
    /// Marks status='CANCELLED' without physically deleting records, restores deducted batch stock
    /// in the same atomic SQLite transaction (when restoreStock is true), bumps stock_snapshot_generation,
    /// refreshes StockData.json, and writes an immutable audit entry.
    /// Blocks cancellation if any completed sales return is already linked to the invoice.
    /// </summary>
    public sealed class InvoiceCancellationService
    {
        private readonly SqliteDatabase _database;
        private readonly StockSnapshotWriter _snapshotWriter;
        private readonly IBillingClock _clock;

        public InvoiceCancellationService(
            SqliteDatabase database,
            StockSnapshotWriter snapshotWriter = null,
            IBillingClock clock = null)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
            _snapshotWriter = snapshotWriter;
            _clock = clock ?? new SystemBillingClock();
        }

        public InvoiceCancellationResult CancelInvoice(string invoiceId, string reason, bool restoreStock = true)
        {
            if (string.IsNullOrWhiteSpace(invoiceId))
                return Fail("Select an invoice to cancel.");
            if (string.IsNullOrWhiteSpace(reason))
                return Fail("Enter a reason for cancelling this invoice.");

            string trimmedReason = reason.Trim();
            if (trimmedReason.Length > 240)
                return Fail("Cancellation reason is too long (maximum 240 characters).");

            BillingInstant now = BillingInstant.Capture(_clock);
            DateTime utcNow = now.UtcNow;
            DateTime localNow = now.LocalNow;
            int currentMonth = localNow.Year * 100 + localNow.Month;
            var batchWarnings = new List<string>();
            string utcText = utcNow.ToString("o", CultureInfo.InvariantCulture);
            string localText = localNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string invoiceNumber;
            bool restoredAny = false;

            try
            {
            using (var conn = _database.OpenConnection())
            using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
            {
                string status;
                long grandTotal;

                using (var find = new SQLiteCommand(
                    "SELECT invoice_number, status, grand_total_paise FROM invoice WHERE invoice_id = @id;",
                    conn,
                    tx))
                {
                    find.Parameters.AddWithValue("@id", invoiceId);
                    using (var r = find.ExecuteReader())
                    {
                        if (!r.Read()) return Fail("Invoice was not found.");
                        invoiceNumber = r.GetString(0);
                        status = r.GetString(1);
                        grandTotal = r.GetInt64(2);
                    }
                }

                if (string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    return Fail("Invoice " + invoiceNumber + " is already cancelled.");

                using (var retCheck = new SQLiteCommand(
                    "SELECT COUNT(*) FROM sales_return WHERE invoice_id = @id AND status = 'COMPLETED';",
                    conn,
                    tx))
                {
                    retCheck.Parameters.AddWithValue("@id", invoiceId);
                    long linkedReturns = Convert.ToInt64(retCheck.ExecuteScalar(), CultureInfo.InvariantCulture);
                    if (linkedReturns > 0)
                        return Fail("Invoice " + invoiceNumber + " already has " + linkedReturns + " completed sales return(s) and cannot be cancelled.");
                }

                using (var upd = new SQLiteCommand(
                    "UPDATE invoice SET status = 'CANCELLED', cancellation_reason = @reason WHERE invoice_id = @id;",
                    conn,
                    tx))
                {
                    upd.Parameters.AddWithValue("@reason", trimmedReason);
                    upd.Parameters.AddWithValue("@id", invoiceId);
                    upd.ExecuteNonQuery();
                }

                if (restoreStock)
                {
                    var linesToRestore = new List<Tuple<string, string, long>>();
                    using (var readLines = new SQLiteCommand(
                        "SELECT batch_id, medicine_id, quantity_milli FROM invoice_line WHERE invoice_id = @id;",
                        conn,
                        tx))
                    {
                        readLines.Parameters.AddWithValue("@id", invoiceId);
                        using (var lr = readLines.ExecuteReader())
                        {
                            while (lr.Read())
                            {
                                linesToRestore.Add(Tuple.Create(lr.GetString(0), lr.GetString(1), lr.GetInt64(2)));
                            }
                        }
                    }

                    foreach (var line in linesToRestore)
                    {
                        if (line.Item3 <= 0L) continue;
                        using (var chk = new SQLiteCommand("SELECT batch_number, batch_status, expiry_yyyymm FROM stock_batch WHERE batch_id = @batch;", conn, tx))
                        {
                            chk.Parameters.AddWithValue("@batch", line.Item1);
                            using (var cr = chk.ExecuteReader())
                            {
                                if (cr.Read())
                                {
                                    string bNum = cr.GetString(0);
                                    string bStatus = cr.GetString(1);
                                    int bExp = cr.GetInt32(2);
                                    if (!string.Equals(bStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase) || bExp < currentMonth)
                                        batchWarnings.Add("Batch " + bNum + " is " + bStatus + " / expired (" + bExp + ")");
                                }
                            }
                        }

                        using (var restock = new SQLiteCommand(@"
UPDATE stock_batch
SET quantity_milli = quantity_milli + @qty,
    updated_at_utc = @utc
WHERE batch_id = @batch;", conn, tx))
                        {
                            restock.Parameters.AddWithValue("@qty", line.Item3);
                            restock.Parameters.AddWithValue("@utc", utcText);
                            restock.Parameters.AddWithValue("@batch", line.Item1);
                            if (restock.ExecuteNonQuery() != 1)
                                batchWarnings.Add("Batch record " + line.Item1 + " was missing during restock");
                        }

                        using (var mov = new SQLiteCommand(@"
INSERT INTO stock_movement(movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli, reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(@id, @batch, @med, 'ADJUSTMENT', @qty, 'INVOICE_CANCEL', @inv, @reason, @utc, @local);", conn, tx))
                        {
                            mov.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                            mov.Parameters.AddWithValue("@batch", line.Item1);
                            mov.Parameters.AddWithValue("@med", line.Item2);
                            mov.Parameters.AddWithValue("@qty", line.Item3);
                            mov.Parameters.AddWithValue("@inv", invoiceId);
                            mov.Parameters.AddWithValue("@reason", "Invoice cancelled (" + invoiceNumber + "): " + trimmedReason);
                            mov.Parameters.AddWithValue("@utc", utcText);
                            mov.Parameters.AddWithValue("@local", localText);
                            mov.ExecuteNonQuery();
                        }

                        restoredAny = true;
                    }

                    if (restoredAny)
                    {
                        StockSnapshotState.MarkDirtyAndIncrementGeneration(conn, tx, utcText);
                    }
                }

                var serializer = new JavaScriptSerializer();
                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "invoiceId", invoiceId },
                    { "invoiceNumber", invoiceNumber },
                    { "previousStatus", status },
                    { "newStatus", "CANCELLED" },
                    { "grandTotalPaise", grandTotal },
                    { "stockRestored", restoredAny },
                    { "reason", trimmedReason }
                });

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'INVOICE_CANCELLED', 'INVOICE', @entity, @summary, @before_json, @after_json, @utc, @local);", conn, tx))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", invoiceId);
                    audit.Parameters.AddWithValue("@summary", "Cancelled invoice " + invoiceNumber + ": " + trimmedReason);
                    audit.Parameters.AddWithValue("@before_json", "{\"status\":\"" + status + "\"}");
                    audit.Parameters.AddWithValue("@after_json", afterJson);
                    audit.Parameters.AddWithValue("@utc", utcText);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                tx.Commit();
            }
            }
            catch (SQLiteException ex)
            {
                return Fail("Database error while cancelling invoice: " + ex.Message);
            }

            bool snapshotRefreshed = false;
            string snapshotWarning = null;
            if (restoredAny && _snapshotWriter != null)
            {
                try
                {
                    snapshotRefreshed = _snapshotWriter.WriteCurrentSnapshot();
                    if (!snapshotRefreshed)
                        snapshotWarning = "Invoice cancelled and stock restored, but StockData.json could not be refreshed immediately.";
                }
                catch (Exception ex)
                {
                    snapshotWarning = "Invoice cancelled and stock restored, but StockData.json refresh warned: " + ex.Message;
                }
            }

            return new InvoiceCancellationResult
            {
                Succeeded = true,
                InvoiceNumber = invoiceNumber,
                StockRestored = restoredAny,
                SnapshotRefreshed = snapshotRefreshed,
                SnapshotWarning = snapshotWarning,
                StockWarning = batchWarnings.Count > 0 ? ("Restored to non-active/expired batch: " + string.Join("; ", batchWarnings)) : null
            };
        }

        private static InvoiceCancellationResult Fail(string msg)
        {
            return new InvoiceCancellationResult { Succeeded = false, ErrorMessage = msg };
        }
    }
}
