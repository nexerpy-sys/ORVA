using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Web.Script.Serialization;
using Orva.Domain;
using Orva.Infrastructure.Database;
using Orva.Services;

namespace Orva.Infrastructure.Repositories
{
    public sealed class InventoryRepository
    {
        private readonly SqliteDatabase _database;
        public InventoryRepository(SqliteDatabase database) { _database = database; }

        public string SaveStockEntry(StockEntryInput input)
        {
            Validate(input);
            var nowUtc = DateTime.UtcNow;
            var now = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            var localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var medicineId = Guid.NewGuid().ToString("N");
            var batchId = Guid.NewGuid().ToString("N");
            var purchaseId = Guid.NewGuid().ToString("N");
            var movementId = Guid.NewGuid().ToString("N");
            var auditId = Guid.NewGuid().ToString("N");
            var normalizedName = NormalizeName(input.MedicineName);

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                medicineId = FindOrCreateMedicine(connection, transaction, input, normalizedName, now);
                batchId = FindOrCreateBatch(connection, transaction, medicineId, input, now);

                long lineTotal = MultiplyMoney(input.QuantityMilli, input.PurchasePricePaise);

                using (var purchase = new SQLiteCommand(@"
INSERT INTO purchase_receipt(purchase_id, received_at_utc, supplier_name_text, supplier_licence_text, document_type, document_reference, subtotal_paise, total_tax_paise, total_paise, created_at_utc)
VALUES(@id, @received, @supplier, @supplier_licence, @document_type, @document_reference, @subtotal, 0, @total, @created);", connection, transaction))
                {
                    purchase.Parameters.AddWithValue("@id", purchaseId);
                    purchase.Parameters.AddWithValue("@received", now);
                    purchase.Parameters.AddWithValue("@supplier", Db(input.SupplierName));
                    purchase.Parameters.AddWithValue("@supplier_licence", Db(input.SupplierLicence));
                    purchase.Parameters.AddWithValue("@document_type", input.DocumentType);
                    purchase.Parameters.AddWithValue("@document_reference", Db(input.DocumentReference));
                    purchase.Parameters.AddWithValue("@subtotal", lineTotal);
                    purchase.Parameters.AddWithValue("@total", lineTotal);
                    purchase.Parameters.AddWithValue("@created", now);
                    purchase.ExecuteNonQuery();
                }

                using (var line = new SQLiteCommand(@"
INSERT INTO purchase_line(purchase_line_id, purchase_id, batch_id, medicine_id, quantity_milli, unit_name_snapshot, unit_purchase_price_paise, line_total_paise, created_at_utc)
VALUES(@id, @purchase, @batch, @medicine, @quantity, @unit, @price, @total, @created);", connection, transaction))
                {
                    line.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    line.Parameters.AddWithValue("@purchase", purchaseId);
                    line.Parameters.AddWithValue("@batch", batchId);
                    line.Parameters.AddWithValue("@medicine", medicineId);
                    line.Parameters.AddWithValue("@quantity", input.QuantityMilli);
                    line.Parameters.AddWithValue("@unit", input.StockUnitName.Trim());
                    line.Parameters.AddWithValue("@price", input.PurchasePricePaise);
                    line.Parameters.AddWithValue("@total", lineTotal);
                    line.Parameters.AddWithValue("@created", now);
                    line.ExecuteNonQuery();
                }

                using (var movement = new SQLiteCommand(@"
INSERT INTO stock_movement(movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli, reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(@id, @batch, @medicine, 'PURCHASE', @quantity, 'PURCHASE_RECEIPT', @reference, 'Stock received', @utc, @local);", connection, transaction))
                {
                    movement.Parameters.AddWithValue("@id", movementId);
                    movement.Parameters.AddWithValue("@batch", batchId);
                    movement.Parameters.AddWithValue("@medicine", medicineId);
                    movement.Parameters.AddWithValue("@quantity", input.QuantityMilli);
                    movement.Parameters.AddWithValue("@reference", purchaseId);
                    movement.Parameters.AddWithValue("@utc", now);
                    movement.Parameters.AddWithValue("@local", localText);
                    movement.ExecuteNonQuery();
                }

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'STOCK_RECEIVED', 'STOCK_BATCH', @entity, @summary, NULL, @after, @utc, @local);", connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", auditId);
                    audit.Parameters.AddWithValue("@entity", batchId);
                    audit.Parameters.AddWithValue("@summary", "Received " + FormatQuantity(input.QuantityMilli) + " " + input.StockUnitName + " of " + input.MedicineName.Trim());
                    audit.Parameters.AddWithValue("@after", "{\"purchaseId\":\"" + purchaseId + "\",\"movementId\":\"" + movementId + "\",\"documentType\":\"" + input.DocumentType + "\"}");
                    audit.Parameters.AddWithValue("@utc", now);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, now);
                transaction.Commit();
            }
            return batchId;
        }

        public StockAdjustmentResult AdjustBatchStock(StockAdjustmentInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (string.IsNullOrWhiteSpace(input.BatchId)) throw new ArgumentException("Select a batch to adjust.");
            if (input.CountedQuantityMilli < 0) throw new ArgumentException("Counted quantity cannot be negative.");
            if (string.IsNullOrWhiteSpace(input.Reason)) throw new ArgumentException("Enter a brief reason for this stock adjustment.");

            string reason = input.Reason.Trim();
            if (reason.Length > 240) throw new ArgumentException("Reason is too long (maximum 240 characters).");

            var nowUtc = DateTime.UtcNow;
            var now = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            var localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var serializer = new JavaScriptSerializer();

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                string medicineId;
                string medicineName;
                string batchNumber;
                string unitName;
                long currentQuantity;

                using (var find = new SQLiteCommand(@"
SELECT b.medicine_id, m.display_name, b.batch_number, b.stock_unit_name, b.quantity_milli
FROM stock_batch b
JOIN medicine m ON m.medicine_id = b.medicine_id
WHERE b.batch_id = @id;", connection, transaction))
                {
                    find.Parameters.AddWithValue("@id", input.BatchId);
                    using (var reader = find.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException("Selected stock batch was not found.");

                        medicineId = reader.GetString(0);
                        medicineName = reader.GetString(1);
                        batchNumber = reader.GetString(2);
                        unitName = reader.GetString(3);
                        currentQuantity = reader.GetInt64(4);
                    }
                }

                long diff = checked(input.CountedQuantityMilli - currentQuantity);
                if (diff == 0)
                    throw new InvalidOperationException("The counted quantity matches the current recorded quantity. No adjustment is needed.");

                string discrepancyId = Guid.NewGuid().ToString("N");
                string movementId = Guid.NewGuid().ToString("N");

                using (var update = new SQLiteCommand(@"
UPDATE stock_batch
SET quantity_milli = @qty,
    updated_at_utc = @updated
WHERE batch_id = @id;", connection, transaction))
                {
                    update.Parameters.AddWithValue("@qty", input.CountedQuantityMilli);
                    update.Parameters.AddWithValue("@updated", now);
                    update.Parameters.AddWithValue("@id", input.BatchId);
                    update.ExecuteNonQuery();
                }

                using (var discrepancy = new SQLiteCommand(@"
INSERT INTO stock_discrepancy(discrepancy_id, batch_id, expected_quantity_milli, counted_quantity_milli, difference_milli, reason, reconciled_at_utc)
VALUES(@id, @batch, @expected, @counted, @diff, @reason, @utc);", connection, transaction))
                {
                    discrepancy.Parameters.AddWithValue("@id", discrepancyId);
                    discrepancy.Parameters.AddWithValue("@batch", input.BatchId);
                    discrepancy.Parameters.AddWithValue("@expected", currentQuantity);
                    discrepancy.Parameters.AddWithValue("@counted", input.CountedQuantityMilli);
                    discrepancy.Parameters.AddWithValue("@diff", diff);
                    discrepancy.Parameters.AddWithValue("@reason", reason);
                    discrepancy.Parameters.AddWithValue("@utc", now);
                    discrepancy.ExecuteNonQuery();
                }

                using (var movement = new SQLiteCommand(@"
INSERT INTO stock_movement(movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli, reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(@id, @batch, @medicine, 'ADJUSTMENT', @delta, 'STOCK_DISCREPANCY', @ref_id, @reason, @utc, @local);", connection, transaction))
                {
                    movement.Parameters.AddWithValue("@id", movementId);
                    movement.Parameters.AddWithValue("@batch", input.BatchId);
                    movement.Parameters.AddWithValue("@medicine", medicineId);
                    movement.Parameters.AddWithValue("@delta", diff);
                    movement.Parameters.AddWithValue("@ref_id", discrepancyId);
                    movement.Parameters.AddWithValue("@reason", reason);
                    movement.Parameters.AddWithValue("@utc", now);
                    movement.Parameters.AddWithValue("@local", localText);
                    movement.ExecuteNonQuery();
                }

                string beforeJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "batchId", input.BatchId },
                    { "batchNumber", batchNumber },
                    { "quantityMilli", currentQuantity }
                });
                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "batchId", input.BatchId },
                    { "batchNumber", batchNumber },
                    { "quantityMilli", input.CountedQuantityMilli },
                    { "differenceMilli", diff },
                    { "reason", reason }
                });

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'STOCK_ADJUSTED', 'STOCK_BATCH', @entity, @summary, @before_json, @after_json, @utc, @local);", connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", input.BatchId);
                    audit.Parameters.AddWithValue("@summary", string.Format(
                        CultureInfo.InvariantCulture,
                        "Adjusted {0} (Batch {1}) from {2} to {3} {4}: {5}",
                        medicineName,
                        batchNumber,
                        FormatQuantity(currentQuantity),
                        FormatQuantity(input.CountedQuantityMilli),
                        unitName,
                        reason));
                    audit.Parameters.AddWithValue("@before_json", beforeJson);
                    audit.Parameters.AddWithValue("@after_json", afterJson);
                    audit.Parameters.AddWithValue("@utc", now);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, now);
                transaction.Commit();

                return new StockAdjustmentResult
                {
                    MedicineName = medicineName,
                    BatchNumber = batchNumber,
                    PreviousQuantityMilli = currentQuantity,
                    NewQuantityMilli = input.CountedQuantityMilli,
                    DifferenceMilli = diff
                };
            }
        }

        public MedicineDetailsInput GetMedicineDetails(string medicineId)
        {
            if (string.IsNullOrWhiteSpace(medicineId)) return null;

            const string sql = @"
SELECT medicine_id, display_name, generic_name, brand_name, strength, dosage_form,
       stock_unit_name, low_stock_threshold_milli, is_active
FROM medicine
WHERE medicine_id = @id;";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@id", medicineId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new MedicineDetailsInput
                    {
                        MedicineId = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        GenericName = ReadNullable(reader, 2),
                        BrandName = ReadNullable(reader, 3),
                        Strength = ReadNullable(reader, 4),
                        DosageForm = ReadNullable(reader, 5),
                        StockUnitName = reader.GetString(6),
                        LowStockThresholdMilli = reader.GetInt64(7),
                        IsActive = reader.GetInt32(8) == 1
                    };
                }
            }
        }

        public void UpdateMedicineDetails(MedicineDetailsInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (string.IsNullOrWhiteSpace(input.MedicineId)) throw new ArgumentException("Medicine ID is required.");
            if (string.IsNullOrWhiteSpace(input.DisplayName)) throw new ArgumentException("Enter the medicine name.");
            if (input.LowStockThresholdMilli < 0) throw new ArgumentException("Low-stock threshold cannot be negative.");

            string trimmedName = input.DisplayName.Trim();
            string normalizedName = NormalizeName(trimmedName);

            var nowUtc = DateTime.UtcNow;
            var now = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            var localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var serializer = new JavaScriptSerializer();

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                string oldName;
                long oldThreshold;
                int oldActive;
                using (var find = new SQLiteCommand(
                    "SELECT display_name, low_stock_threshold_milli, is_active FROM medicine WHERE medicine_id = @id;",
                    connection,
                    transaction))
                {
                    find.Parameters.AddWithValue("@id", input.MedicineId);
                    using (var reader = find.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Medicine was not found.");
                        oldName = reader.GetString(0);
                        oldThreshold = reader.GetInt64(1);
                        oldActive = reader.GetInt32(2);
                    }
                }

                // Prevent duplicate normalized name collision with another medicine
                using (var dup = new SQLiteCommand(
                    "SELECT COUNT(*) FROM medicine WHERE normalized_name = @norm AND medicine_id <> @id;",
                    connection,
                    transaction))
                {
                    dup.Parameters.AddWithValue("@norm", normalizedName);
                    dup.Parameters.AddWithValue("@id", input.MedicineId);
                    if (Convert.ToInt64(dup.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                        throw new InvalidOperationException("Another medicine with the same name already exists.");
                }

                // Note: stock_unit_name is deliberately preserved to protect historical batch and invoice consistency.
                using (var update = new SQLiteCommand(@"
UPDATE medicine
SET display_name = @name,
    normalized_name = @norm,
    generic_name = @generic,
    brand_name = @brand,
    strength = @strength,
    dosage_form = @dosage,
    low_stock_threshold_milli = @threshold,
    is_active = @active,
    updated_at_utc = @updated
WHERE medicine_id = @id;", connection, transaction))
                {
                    update.Parameters.AddWithValue("@name", trimmedName);
                    update.Parameters.AddWithValue("@norm", normalizedName);
                    update.Parameters.AddWithValue("@generic", Db(input.GenericName));
                    update.Parameters.AddWithValue("@brand", Db(input.BrandName));
                    update.Parameters.AddWithValue("@strength", Db(input.Strength));
                    update.Parameters.AddWithValue("@dosage", Db(input.DosageForm));
                    update.Parameters.AddWithValue("@threshold", input.LowStockThresholdMilli);
                    update.Parameters.AddWithValue("@active", input.IsActive ? 1 : 0);
                    update.Parameters.AddWithValue("@updated", now);
                    update.Parameters.AddWithValue("@id", input.MedicineId);
                    update.ExecuteNonQuery();
                }

                string beforeJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "displayName", oldName },
                    { "lowStockThresholdMilli", oldThreshold },
                    { "isActive", oldActive == 1 }
                });
                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "displayName", trimmedName },
                    { "lowStockThresholdMilli", input.LowStockThresholdMilli },
                    { "isActive", input.IsActive }
                });

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'MEDICINE_UPDATED', 'MEDICINE', @entity, @summary, @before_json, @after_json, @utc, @local);", connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", input.MedicineId);
                    audit.Parameters.AddWithValue("@summary", "Updated medicine details for " + trimmedName);
                    audit.Parameters.AddWithValue("@before_json", beforeJson);
                    audit.Parameters.AddWithValue("@after_json", afterJson);
                    audit.Parameters.AddWithValue("@utc", now);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, now);
                transaction.Commit();
            }
        }

        public void UpdateBatchStatus(string batchId, string newStatus, string reason)
        {
            if (string.IsNullOrWhiteSpace(batchId)) throw new ArgumentException("Batch ID is required.");
            var allowed = new[] { "ACTIVE", "QUARANTINED", "EXPIRED", "BLOCKED" };
            string status = (newStatus ?? string.Empty).Trim().ToUpperInvariant();
            if (Array.IndexOf(allowed, status) < 0)
                throw new ArgumentException("Unsupported batch status: " + newStatus);

            var nowUtc = DateTime.UtcNow;
            var now = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            var localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                string oldStatus;
                string batchNumber;
                using (var find = new SQLiteCommand("SELECT batch_status, batch_number FROM stock_batch WHERE batch_id = @id;", connection, transaction))
                {
                    find.Parameters.AddWithValue("@id", batchId);
                    using (var reader = find.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Batch not found.");
                        oldStatus = reader.GetString(0);
                        batchNumber = reader.GetString(1);
                    }
                }

                if (string.Equals(oldStatus, status, StringComparison.Ordinal))
                {
                    transaction.Commit();
                    return;
                }

                using (var update = new SQLiteCommand(
                    "UPDATE stock_batch SET batch_status = @status, updated_at_utc = @updated WHERE batch_id = @id;",
                    connection,
                    transaction))
                {
                    update.Parameters.AddWithValue("@status", status);
                    update.Parameters.AddWithValue("@updated", now);
                    update.Parameters.AddWithValue("@id", batchId);
                    update.ExecuteNonQuery();
                }

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'BATCH_STATUS_UPDATED', 'STOCK_BATCH', @entity, @summary, @before_json, @after_json, @utc, @local);", connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", batchId);
                    audit.Parameters.AddWithValue("@summary", "Changed batch " + batchNumber + " status from " + oldStatus + " to " + status + (string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim()));
                    audit.Parameters.AddWithValue("@before_json", "{\"batchStatus\":\"" + oldStatus + "\"}");
                    audit.Parameters.AddWithValue("@after_json", "{\"batchStatus\":\"" + status + "\"}");
                    audit.Parameters.AddWithValue("@utc", now);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, now);
                transaction.Commit();
            }
        }

        public IList<InventoryRow> GetInventoryRows(string searchText)
        {
            return GetInventoryRows(searchText, "ALL", null);
        }

        public IList<InventoryRow> GetInventoryRows(string searchText, string filterMode, DateTime? localBusinessDate)
        {
            DateTime today = (localBusinessDate ?? DateTime.Now).Date;
            int currentMonth = today.Year * 100 + today.Month;
            string mode = string.IsNullOrWhiteSpace(filterMode) ? "ALL" : filterMode.Trim().ToUpperInvariant();

            var rows = new List<InventoryRow>();
            const string sql = @"
SELECT m.medicine_id, b.batch_id, m.display_name, b.batch_number, b.expiry_yyyymm,
       b.quantity_milli, b.stock_unit_name, b.mrp_paise, b.batch_status,
       m.low_stock_threshold_milli,
       COALESCE((SELECT SUM(b2.quantity_milli) FROM stock_batch b2
                 WHERE b2.medicine_id = m.medicine_id
                   AND b2.batch_status = 'ACTIVE'
                   AND b2.quantity_milli > 0
                   AND b2.expiry_yyyymm >= @current_month), 0) AS usable_total
FROM stock_batch b
JOIN medicine m ON m.medicine_id = b.medicine_id
WHERE m.is_active = 1
  AND (@search = '' OR m.normalized_name LIKE @like ESCAPE '\' OR UPPER(b.batch_number) LIKE @like ESCAPE '\' OR EXISTS (SELECT 1 FROM barcode_mapping bm WHERE bm.medicine_id = m.medicine_id AND UPPER(bm.barcode) = UPPER(@search)))
ORDER BY m.display_name COLLATE NOCASE, b.expiry_yyyymm, b.batch_number;";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
            {
                var search = (searchText ?? string.Empty).Trim().ToUpperInvariant();
                command.Parameters.AddWithValue("@search", search);
                command.Parameters.AddWithValue("@like", "%" + EscapeLike(NormalizeSearch(searchText)) + "%");
                command.Parameters.AddWithValue("@current_month", currentMonth);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int expiry = reader.GetInt32(4);
                        long quantity = reader.GetInt64(5);
                        string batchStatus = reader.GetString(8);
                        long threshold = reader.GetInt64(9);
                        long usableTotal = reader.GetInt64(10);

                        ExpiryAlertLevel expiryLevel = InventoryAlertCalculator.ClassifyExpiry(batchStatus, expiry, today);
                        bool isLowStock = InventoryAlertCalculator.IsLowStock(true, usableTotal, threshold);

                        if (mode == "LOW_STOCK" && !isLowStock) continue;
                        if (mode == "EXPIRING" && (expiryLevel == ExpiryAlertLevel.None || expiryLevel == ExpiryAlertLevel.Expired || quantity <= 0)) continue;
                        if (mode == "EXPIRED" && expiryLevel != ExpiryAlertLevel.Expired) continue;

                        string displayStatus;
                        string colour;
                        ResolveDisplayStatus(batchStatus, expiryLevel, quantity, isLowStock, out displayStatus, out colour);

                        rows.Add(new InventoryRow
                        {
                            MedicineId = reader.GetString(0),
                            BatchId = reader.GetString(1),
                            MedicineName = reader.GetString(2),
                            BatchNumber = reader.GetString(3),
                            Expiry = expiry.ToString("0000-00", CultureInfo.InvariantCulture),
                            ExpiryYearMonth = expiry,
                            Quantity = FormatQuantity(quantity),
                            QuantityMilli = quantity,
                            Unit = reader.GetString(6),
                            Mrp = FormatMoney(reader.GetInt64(7)),
                            Status = displayStatus,
                            StatusColour = colour,
                            BatchStatus = batchStatus,
                            ExpiryLevel = expiryLevel,
                            IsLowStock = isLowStock,
                            LowStockThresholdMilli = threshold,
                            UsableMedicineTotalMilli = usableTotal
                        });
                    }
                }
            }
            return rows;
        }

        public IList<LowStockAlertItem> GetLowStockAlerts(DateTime? localBusinessDate = null)
        {
            DateTime today = (localBusinessDate ?? DateTime.Now).Date;
            int currentMonth = today.Year * 100 + today.Month;
            var alerts = new List<LowStockAlertItem>();

            const string sql = @"
SELECT m.medicine_id, m.display_name, m.stock_unit_name, m.low_stock_threshold_milli,
       COALESCE((SELECT SUM(b.quantity_milli) FROM stock_batch b
                 WHERE b.medicine_id = m.medicine_id
                   AND b.batch_status = 'ACTIVE'
                   AND b.quantity_milli > 0
                   AND b.expiry_yyyymm >= @current_month), 0) AS usable_total
FROM medicine m
WHERE m.is_active = 1
  AND m.low_stock_threshold_milli > 0
ORDER BY m.display_name COLLATE NOCASE ASC;";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@current_month", currentMonth);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long threshold = reader.GetInt64(3);
                        long usable = reader.GetInt64(4);
                        if (InventoryAlertCalculator.IsLowStock(true, usable, threshold))
                        {
                            alerts.Add(new LowStockAlertItem
                            {
                                MedicineId = reader.GetString(0),
                                MedicineName = reader.GetString(1),
                                StockUnitName = reader.GetString(2),
                                ThresholdMilli = threshold,
                                UsableQuantityMilli = usable
                            });
                        }
                    }
                }
            }

            return alerts;
        }

        public IList<ExpiryAlertItem> GetExpiryAlerts(DateTime? localBusinessDate = null)
        {
            DateTime today = (localBusinessDate ?? DateTime.Now).Date;
            var alerts = new List<ExpiryAlertItem>();

            const string sql = @"
SELECT m.medicine_id, b.batch_id, m.display_name, b.batch_number,
       b.expiry_yyyymm, b.quantity_milli, b.stock_unit_name, b.batch_status
FROM stock_batch b
JOIN medicine m ON m.medicine_id = b.medicine_id
WHERE m.is_active = 1
  AND b.quantity_milli > 0
ORDER BY b.expiry_yyyymm ASC, m.display_name COLLATE NOCASE ASC, b.batch_number ASC;";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    int expiryYm = reader.GetInt32(4);
                    string batchStatus = reader.GetString(7);
                    ExpiryAlertLevel level = InventoryAlertCalculator.ClassifyExpiry(batchStatus, expiryYm, today);
                    if (level == ExpiryAlertLevel.None) continue;

                    DateTime expiryDate = InventoryAlertCalculator.GetMonthEndExpiryDate(expiryYm);
                    alerts.Add(new ExpiryAlertItem
                    {
                        MedicineId = reader.GetString(0),
                        BatchId = reader.GetString(1),
                        MedicineName = reader.GetString(2),
                        BatchNumber = reader.GetString(3),
                        ExpiryYearMonth = expiryYm,
                        ExpiryDate = expiryDate,
                        DaysRemaining = (expiryDate - today).Days,
                        QuantityMilli = reader.GetInt64(5),
                        StockUnitName = reader.GetString(6),
                        AlertLevel = level,
                        AlertLabel = InventoryAlertCalculator.GetExpiryLabel(level)
                    });
                }
            }

            return alerts;
        }

        public string GetStockUnitForMedicine(string medicineName)
        {
            if (string.IsNullOrWhiteSpace(medicineName)) return null;
            var normalized = NormalizeName(medicineName);
            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand("SELECT stock_unit_name FROM medicine WHERE normalized_name=@name AND is_active=1;", connection))
            {
                command.Parameters.AddWithValue("@name", normalized);
                return command.ExecuteScalar() as string;
            }
        }

        public int GetMedicineCount()
        {
            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM medicine WHERE is_active = 1;", connection))
                return Convert.ToInt32(command.ExecuteScalar());
        }

        public int GetBatchCount()
        {
            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM stock_batch;", connection))
                return Convert.ToInt32(command.ExecuteScalar());
        }

        private static string FindOrCreateMedicine(SQLiteConnection connection, SQLiteTransaction transaction, StockEntryInput input, string normalizedName, string now)
        {
            using (var find = new SQLiteCommand("SELECT medicine_id, stock_unit_name, is_active FROM medicine WHERE normalized_name = @name;", connection, transaction))
            {
                find.Parameters.AddWithValue("@name", normalizedName);
                using (var reader = find.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var existingId = reader.GetString(0);
                        var existingUnit = reader.GetString(1);
                        if (!string.Equals(existingUnit, input.StockUnitName.Trim(), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("This medicine is already recorded using the unit '" + existingUnit + "'. Use that unit for this stock entry.");
                        if (reader.GetInt32(2) == 0)
                    {
                        using (var reactivate = new SQLiteCommand("UPDATE medicine SET is_active = 1, updated_at_utc = @updated WHERE medicine_id = @id;", connection, transaction))
                        {
                            reactivate.Parameters.AddWithValue("@updated", now);
                            reactivate.Parameters.AddWithValue("@id", existingId);
                            reactivate.ExecuteNonQuery();
                        }
                    }
                    return existingId;
                    }
                }
            }

            var id = Guid.NewGuid().ToString("N");
            using (var insert = new SQLiteCommand(@"
INSERT INTO medicine(medicine_id, display_name, normalized_name, stock_unit_name, created_at_utc, updated_at_utc)
VALUES(@id, @name, @normalized, @unit, @created, @updated);", connection, transaction))
            {
                insert.Parameters.AddWithValue("@id", id);
                insert.Parameters.AddWithValue("@name", input.MedicineName.Trim());
                insert.Parameters.AddWithValue("@normalized", normalizedName);
                insert.Parameters.AddWithValue("@unit", input.StockUnitName.Trim());
                insert.Parameters.AddWithValue("@created", now);
                insert.Parameters.AddWithValue("@updated", now);
                insert.ExecuteNonQuery();
            }
            return id;
        }

        private static string FindOrCreateBatch(SQLiteConnection connection, SQLiteTransaction transaction, string medicineId, StockEntryInput input, string now)
        {
            string id = null;
            using (var find = new SQLiteCommand(@"SELECT batch_id, batch_status FROM stock_batch WHERE medicine_id=@medicine AND UPPER(TRIM(batch_number))=UPPER(TRIM(@batch)) AND expiry_yyyymm=@expiry;", connection, transaction))
            {
                find.Parameters.AddWithValue("@medicine", medicineId);
                find.Parameters.AddWithValue("@batch", input.BatchNumber.Trim());
                find.Parameters.AddWithValue("@expiry", input.ExpiryYearMonth);
                using (var reader = find.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        id = reader.GetString(0);
                        var status = reader.GetString(1);
                        if (!string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("This batch is marked as " + status.ToLowerInvariant() + ". It must be checked before more stock is added.");
                    }
                }
            }

            if (id == null)
            {
                id = Guid.NewGuid().ToString("N");
                using (var insert = new SQLiteCommand(@"
INSERT INTO stock_batch(batch_id, medicine_id, batch_number, expiry_yyyymm, stock_unit_name, mrp_paise, latest_purchase_price_paise, quantity_milli, batch_status, created_at_utc, updated_at_utc)
VALUES(@id, @medicine, @batch, @expiry, @unit, @mrp, @purchase, @quantity, 'ACTIVE', @created, @updated);", connection, transaction))
                {
                    insert.Parameters.AddWithValue("@id", id);
                    insert.Parameters.AddWithValue("@medicine", medicineId);
                    insert.Parameters.AddWithValue("@batch", input.BatchNumber.Trim());
                    insert.Parameters.AddWithValue("@expiry", input.ExpiryYearMonth);
                    insert.Parameters.AddWithValue("@unit", input.StockUnitName.Trim());
                    insert.Parameters.AddWithValue("@mrp", input.MrpPaise);
                    insert.Parameters.AddWithValue("@purchase", input.PurchasePricePaise);
                    insert.Parameters.AddWithValue("@quantity", input.QuantityMilli);
                    insert.Parameters.AddWithValue("@created", now);
                    insert.Parameters.AddWithValue("@updated", now);
                    insert.ExecuteNonQuery();
                }
            }
            else
            {
                using (var update = new SQLiteCommand(@"
UPDATE stock_batch SET quantity_milli = quantity_milli + @quantity,
mrp_paise = CASE WHEN @mrp > 0 THEN @mrp ELSE mrp_paise END, latest_purchase_price_paise=@purchase, updated_at_utc=@updated
WHERE batch_id=@id;", connection, transaction))
                {
                    update.Parameters.AddWithValue("@quantity", input.QuantityMilli);
                    update.Parameters.AddWithValue("@mrp", input.MrpPaise);
                    update.Parameters.AddWithValue("@purchase", input.PurchasePricePaise);
                    update.Parameters.AddWithValue("@updated", now);
                    update.Parameters.AddWithValue("@id", id);
                    update.ExecuteNonQuery();
                }
            }
            return id;
        }

        private static void Validate(StockEntryInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (string.IsNullOrWhiteSpace(input.MedicineName)) throw new ArgumentException("Enter the medicine name.");
            if (input.MedicineName.Trim().Length > 180) throw new ArgumentException("Medicine name is too long.");
            if (string.IsNullOrWhiteSpace(input.StockUnitName)) throw new ArgumentException("Choose the stock unit.");
            if (string.IsNullOrWhiteSpace(input.BatchNumber)) throw new ArgumentException("Enter the batch number.");
            if (input.BatchNumber.Trim().Length > 80) throw new ArgumentException("Batch number is too long.");
            if (input.ExpiryYearMonth < 200001 || input.ExpiryYearMonth > 219912 || (input.ExpiryYearMonth % 100) < 1 || (input.ExpiryYearMonth % 100) > 12)
                throw new ArgumentException("Choose a valid expiry month.");
            var currentMonth = DateTime.Now.Year * 100 + DateTime.Now.Month;
            if (input.ExpiryYearMonth < currentMonth) throw new ArgumentException("This batch has already passed its expiry month. Check the date before receiving it.");
            if (input.QuantityMilli <= 0) throw new ArgumentException("Enter a quantity greater than zero.");
            if (input.PurchasePricePaise < 0 || input.MrpPaise < 0) throw new ArgumentException("Prices cannot be negative.");
            if (string.IsNullOrWhiteSpace(input.DocumentType)) input.DocumentType = "NO_DOCUMENT";
            var allowed = new[] { "TAX_INVOICE", "CASH_CREDIT_MEMO", "NO_DOCUMENT", "OTHER" };
            if (Array.IndexOf(allowed, input.DocumentType) < 0) throw new ArgumentException("Choose a valid purchase document type.");
        }

        private static void ResolveDisplayStatus(
            string batchStatus,
            ExpiryAlertLevel expiryLevel,
            long quantityMilli,
            bool isLowStock,
            out string label,
            out string colour)
        {
            if (string.Equals(batchStatus, "QUARANTINED", StringComparison.Ordinal))
            {
                label = "Quarantined";
                colour = "#C9362B";
                return;
            }
            if (string.Equals(batchStatus, "BLOCKED", StringComparison.Ordinal))
            {
                label = "Blocked";
                colour = "#C9362B";
                return;
            }
            if (expiryLevel == ExpiryAlertLevel.Expired)
            {
                label = "Expired";
                colour = "#C9362B";
                return;
            }
            if (quantityMilli <= 0)
            {
                label = "Out of stock";
                colour = "#746F65";
                return;
            }
            if (expiryLevel != ExpiryAlertLevel.None)
            {
                label = InventoryAlertCalculator.GetExpiryLabel(expiryLevel);
                colour = InventoryAlertCalculator.GetExpiryColour(expiryLevel);
                return;
            }
            if (isLowStock)
            {
                label = "Low stock";
                colour = "#FF4B1F";
                return;
            }
            label = "Available";
            colour = "#16845B";
        }

        private static string NormalizeName(string value)
        {
            return string.Join(" ", value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim(); }
        private static string ReadNullable(SQLiteDataReader reader, int ordinal) { return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal); }
        private static long MultiplyMoney(long quantityMilli, long pricePaise)
        {
            return checked((long)Math.Round((decimal)quantityMilli * pricePaise / 1000m, 0, MidpointRounding.AwayFromZero));
        }
        private static string FormatMoney(long paise) { return (paise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN")); }
        private static string FormatQuantity(long quantityMilli) { return (quantityMilli / 1000m).ToString("0.###", CultureInfo.InvariantCulture); }

        private static string EscapeLike(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static string NormalizeSearch(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var parts = raw.Trim().ToUpperInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }
    }
}