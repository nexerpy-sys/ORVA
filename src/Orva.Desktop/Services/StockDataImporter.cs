using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class StockImportPreview
    {
        public StockImportPreview()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public bool IsValid { get; set; }
        public string FilePath { get; set; }
        public int FormatVersion { get; set; }
        public string SourceShopId { get; set; }
        public string SourceShopName { get; set; }
        public string TargetShopId { get; set; }
        public string TargetShopName { get; set; }
        public bool IsDifferentShop { get; set; }
        public string GeneratedAtUtc { get; set; }
        public int MedicineCount { get; set; }
        public int BatchCount { get; set; }
        public long TotalImportedQuantityMilli { get; set; }
        public int NewMedicineCount { get; set; }
        public int MatchedMedicineCount { get; set; }
        public int SoftInactivatedMedicineCount { get; set; }
        public int NewBatchCount { get; set; }
        public int ChangedBatchCount { get; set; }
        public int ZeroedBatchCount { get; set; }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
    }

    public sealed class StockImportResult
    {
        public bool Succeeded { get; set; }
        public int MedicinesImported { get; set; }
        public int BatchesImported { get; set; }
        public int StockMovementsRecorded { get; set; }
        public int BatchesZeroed { get; set; }
        public bool SnapshotUpdated { get; set; }
        public string SnapshotWarning { get; set; }
        public string SafetyBackupPath { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// History-preserving importer for StockData.json (Version 2 and Version 1 compatible).
    /// Never deletes historical invoices, payments, purchases, returns, or stock movements.
    /// Reconciles batch quantities using IMPORT stock movements instead of fake purchase records.
    /// </summary>
    public sealed class StockDataImporter
    {
        private static readonly string[] AllowedBatchStatuses = new[] { "ACTIVE", "QUARANTINED", "EXPIRED", "BLOCKED" };

        private readonly SqliteDatabase _database;
        private readonly ShopRepository _shopRepository;
        private readonly StockSnapshotWriter _snapshotWriter;
        private readonly Func<string, string> _safetyBackupCreator;
        private readonly Action<string> _testCheckpoint;

        public StockDataImporter(
            SqliteDatabase database,
            ShopRepository shopRepository,
            StockSnapshotWriter snapshotWriter,
            Func<string, string> safetyBackupCreator = null)
            : this(database, shopRepository, snapshotWriter, safetyBackupCreator, null)
        {
        }

        internal StockDataImporter(
            SqliteDatabase database,
            ShopRepository shopRepository,
            StockSnapshotWriter snapshotWriter,
            Func<string, string> safetyBackupCreator,
            Action<string> testCheckpoint)
        {
            if (database == null) throw new ArgumentNullException("database");
            if (shopRepository == null) throw new ArgumentNullException("shopRepository");
            _database = database;
            _shopRepository = shopRepository;
            _snapshotWriter = snapshotWriter;
            _safetyBackupCreator = safetyBackupCreator;
            _testCheckpoint = testCheckpoint;
        }

        public StockImportPreview PreviewImport(string filePath)
        {
            var preview = new StockImportPreview { FilePath = filePath };
            StockSnapshot snapshot;
            if (!TryLoadAndValidateFile(filePath, preview, out snapshot))
            {
                preview.IsValid = false;
                return preview;
            }

            var targetShop = _shopRepository.GetProfile();
            preview.TargetShopId = targetShop != null ? targetShop.ShopId : null;
            preview.TargetShopName = targetShop != null ? targetShop.ShopName : null;
            preview.IsDifferentShop = targetShop != null
                && !string.Equals(targetShop.ShopId, snapshot.ShopId, StringComparison.Ordinal);

            if (preview.IsDifferentShop)
            {
                preview.Warnings.Add(
                    "This stock file belongs to a different shop (" + snapshot.ShopName + "). Importing will replace current inventory only; your shop profile, GSTIN, and historical invoices will remain unchanged.");
            }

            if (snapshot.FormatVersion == 1)
            {
                preview.Warnings.Add(
                    "This is a Version 1 stock file. Missing GST configuration flags will remain unconfigured (never assumed 0% exempt), and barcode mappings are not present.");
            }

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                AnalyzeAgainstDatabase(connection, transaction, snapshot, preview);
                transaction.Commit();
            }

            preview.IsValid = preview.Errors.Count == 0;
            return preview;
        }

        public StockImportResult ExecuteImport(string filePath, bool confirmDifferentShop)
        {
            var preview = PreviewImport(filePath);
            if (!preview.IsValid)
            {
                return new StockImportResult
                {
                    Succeeded = false,
                    ErrorMessage = preview.Errors.Count > 0 ? preview.Errors[0] : "The selected stock file is not valid."
                };
            }

            if (preview.IsDifferentShop && !confirmDifferentShop)
            {
                return new StockImportResult
                {
                    Succeeded = false,
                    ErrorMessage = "Explicit confirmation is required before importing inventory from a different shop."
                };
            }

            StockSnapshot snapshot;
            var reloadPreview = new StockImportPreview { FilePath = filePath };
            if (!TryLoadAndValidateFile(filePath, reloadPreview, out snapshot))
            {
                return new StockImportResult
                {
                    Succeeded = false,
                    ErrorMessage = reloadPreview.Errors.Count > 0 ? reloadPreview.Errors[0] : "Stock file could not be read."
                };
            }

            string safetyBackupPath = null;
            if (_safetyBackupCreator != null)
            {
                safetyBackupPath = _safetyBackupCreator("pre-stock-import");
            }

            var nowUtc = DateTime.UtcNow;
            string utcText = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            string localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string importId = Guid.NewGuid().ToString("N");
            int movementsRecorded = 0;
            int batchesZeroed = 0;
            int medicinesImported = snapshot.Medicines.Count;
            int batchesImported = 0;

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var existingMedsById = LoadExistingMedicines(connection, transaction);
                var existingBatchesById = LoadExistingBatches(connection, transaction);

                var matchedTargetMedicineIds = new HashSet<string>(StringComparer.Ordinal);
                var matchedTargetBatchIds = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < snapshot.Medicines.Count; i++)
                {
                    var srcMed = snapshot.Medicines[i];
                    var existingMed = MatchMedicine(srcMed, existingMedsById);

                    string targetMedicineId;
                    if (existingMed != null)
                    {
                        if (!string.Equals(existingMed.StockUnitName, srcMed.StockUnit.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                "Unit conflict for '" + srcMed.Name + "': existing unit is '" + existingMed.StockUnitName + "' but file has '" + srcMed.StockUnit + "'.");
                        }

                        targetMedicineId = existingMed.MedicineId;
                        // For V1 files (GstRateConfigured == null), never overwrite an already-configured medicine with false/guessed values.
                        bool effectiveGstConfigured = snapshot.FormatVersion >= 2 && srcMed.GstRateConfigured.HasValue
                            ? srcMed.GstRateConfigured.Value
                            : existingMed.GstRateConfigured;
                        int effectiveGstRate = snapshot.FormatVersion >= 2 && srcMed.GstRateConfigured.HasValue
                            ? srcMed.GstRateBasisPoints
                            : (existingMed.GstRateConfigured ? existingMed.GstRateBasisPoints : srcMed.GstRateBasisPoints);
                        bool effectiveActive = srcMed.IsActive.HasValue ? srcMed.IsActive.Value : true;

                        using (var upd = new SQLiteCommand(@"
UPDATE medicine
SET display_name = @name,
    normalized_name = @norm,
    generic_name = @generic,
    brand_name = @brand,
    strength = @strength,
    dosage_form = @dosage,
    hsn_code = @hsn,
    gst_rate_basis_points = @gst_bps,
    gst_rate_configured = @gst_cfg,
    low_stock_threshold_milli = @threshold,
    is_active = @active,
    updated_at_utc = @updated
WHERE medicine_id = @id;", connection, transaction))
                        {
                            upd.Parameters.AddWithValue("@name", srcMed.Name.Trim());
                            upd.Parameters.AddWithValue("@norm", NormalizeName(srcMed.Name));
                            upd.Parameters.AddWithValue("@generic", Db(srcMed.GenericName));
                            upd.Parameters.AddWithValue("@brand", Db(srcMed.BrandName));
                            upd.Parameters.AddWithValue("@strength", Db(srcMed.Strength));
                            upd.Parameters.AddWithValue("@dosage", Db(srcMed.DosageForm));
                            upd.Parameters.AddWithValue("@hsn", Db(srcMed.HsnCode));
                            upd.Parameters.AddWithValue("@gst_bps", effectiveGstRate);
                            upd.Parameters.AddWithValue("@gst_cfg", effectiveGstConfigured ? 1 : 0);
                            upd.Parameters.AddWithValue("@threshold", srcMed.LowStockThresholdMilli);
                            upd.Parameters.AddWithValue("@active", effectiveActive ? 1 : 0);
                            upd.Parameters.AddWithValue("@updated", utcText);
                            upd.Parameters.AddWithValue("@id", targetMedicineId);
                            upd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        targetMedicineId = !string.IsNullOrWhiteSpace(srcMed.MedicineId) && !existingMedsById.ContainsKey(srcMed.MedicineId)
                            ? srcMed.MedicineId.Trim()
                            : Guid.NewGuid().ToString("N");

                        // V1 files have no GstRateConfigured flag -> always treat as unconfigured (0), NEVER 0% exempt.
                        bool effectiveGstConfigured = snapshot.FormatVersion >= 2 && srcMed.GstRateConfigured.HasValue && srcMed.GstRateConfigured.Value;
                        bool effectiveActive = srcMed.IsActive.HasValue ? srcMed.IsActive.Value : true;

                        using (var ins = new SQLiteCommand(@"
INSERT INTO medicine(
    medicine_id, display_name, normalized_name, generic_name, brand_name, strength, dosage_form,
    stock_unit_name, hsn_code, gst_rate_basis_points, gst_rate_configured, low_stock_threshold_milli,
    is_active, created_at_utc, updated_at_utc)
VALUES(
    @id, @name, @norm, @generic, @brand, @strength, @dosage,
    @unit, @hsn, @gst_bps, @gst_cfg, @threshold,
    @active, @created, @updated);", connection, transaction))
                        {
                            ins.Parameters.AddWithValue("@id", targetMedicineId);
                            ins.Parameters.AddWithValue("@name", srcMed.Name.Trim());
                            ins.Parameters.AddWithValue("@norm", NormalizeName(srcMed.Name));
                            ins.Parameters.AddWithValue("@generic", Db(srcMed.GenericName));
                            ins.Parameters.AddWithValue("@brand", Db(srcMed.BrandName));
                            ins.Parameters.AddWithValue("@strength", Db(srcMed.Strength));
                            ins.Parameters.AddWithValue("@dosage", Db(srcMed.DosageForm));
                            ins.Parameters.AddWithValue("@unit", srcMed.StockUnit.Trim());
                            ins.Parameters.AddWithValue("@hsn", Db(srcMed.HsnCode));
                            ins.Parameters.AddWithValue("@gst_bps", srcMed.GstRateBasisPoints);
                            ins.Parameters.AddWithValue("@gst_cfg", effectiveGstConfigured ? 1 : 0);
                            ins.Parameters.AddWithValue("@threshold", srcMed.LowStockThresholdMilli);
                            ins.Parameters.AddWithValue("@active", effectiveActive ? 1 : 0);
                            ins.Parameters.AddWithValue("@created", utcText);
                            ins.Parameters.AddWithValue("@updated", utcText);
                            ins.ExecuteNonQuery();
                        }
                    }

                    matchedTargetMedicineIds.Add(targetMedicineId);

                    // Sync V2 barcode mappings safely without hijacking another medicine's barcode
                    if (snapshot.FormatVersion >= 2 && srcMed.Barcodes != null)
                    {
                        for (int b = 0; b < srcMed.Barcodes.Count; b++)
                        {
                            string bc = (srcMed.Barcodes[b] ?? string.Empty).Trim();
                            if (bc.Length == 0) continue;
                            using (var bcIns = new SQLiteCommand(
                                "INSERT OR IGNORE INTO barcode_mapping(barcode, medicine_id, created_at_utc) VALUES(@bc, @med, @utc);",
                                connection,
                                transaction))
                            {
                                bcIns.Parameters.AddWithValue("@bc", bc);
                                bcIns.Parameters.AddWithValue("@med", targetMedicineId);
                                bcIns.Parameters.AddWithValue("@utc", utcText);
                                bcIns.ExecuteNonQuery();
                            }
                        }
                    }

                    if (srcMed.Batches != null)
                    {
                        for (int j = 0; j < srcMed.Batches.Count; j++)
                        {
                            var srcBatch = srcMed.Batches[j];
                            batchesImported++;
                            var existingBatch = MatchBatch(srcBatch, targetMedicineId, existingBatchesById);
                            string status = NormalizeBatchStatus(srcBatch.Status);

                            if (existingBatch != null)
                            {
                                matchedTargetBatchIds.Add(existingBatch.BatchId);
                                long delta = checked(srcBatch.QuantityMilli - existingBatch.QuantityMilli);

                                using (var updBatch = new SQLiteCommand(@"
UPDATE stock_batch
SET mrp_paise = @mrp,
    latest_purchase_price_paise = @purchase,
    quantity_milli = @qty,
    batch_status = @status,
    updated_at_utc = @updated
WHERE batch_id = @id;", connection, transaction))
                                {
                                    updBatch.Parameters.AddWithValue("@mrp", srcBatch.MrpPaise);
                                    updBatch.Parameters.AddWithValue("@purchase", srcBatch.LatestPurchasePricePaise);
                                    updBatch.Parameters.AddWithValue("@qty", srcBatch.QuantityMilli);
                                    updBatch.Parameters.AddWithValue("@status", status);
                                    updBatch.Parameters.AddWithValue("@updated", utcText);
                                    updBatch.Parameters.AddWithValue("@id", existingBatch.BatchId);
                                    updBatch.ExecuteNonQuery();
                                }

                                if (delta != 0)
                                {
                                    InsertImportMovement(
                                        connection, transaction, existingBatch.BatchId, targetMedicineId,
                                        delta, importId, "Stock file import reconciliation", utcText, localText);
                                    movementsRecorded++;
                                }
                            }
                            else
                            {
                                string newBatchId = !string.IsNullOrWhiteSpace(srcBatch.BatchId) && !existingBatchesById.ContainsKey(srcBatch.BatchId)
                                    ? srcBatch.BatchId.Trim()
                                    : Guid.NewGuid().ToString("N");

                                matchedTargetBatchIds.Add(newBatchId);

                                using (var insBatch = new SQLiteCommand(@"
INSERT INTO stock_batch(
    batch_id, medicine_id, batch_number, expiry_yyyymm, stock_unit_name,
    mrp_paise, latest_purchase_price_paise, quantity_milli, batch_status,
    created_at_utc, updated_at_utc)
VALUES(
    @id, @med, @num, @exp, @unit,
    @mrp, @purchase, @qty, @status,
    @created, @updated);", connection, transaction))
                                {
                                    insBatch.Parameters.AddWithValue("@id", newBatchId);
                                    insBatch.Parameters.AddWithValue("@med", targetMedicineId);
                                    insBatch.Parameters.AddWithValue("@num", srcBatch.BatchNumber.Trim());
                                    insBatch.Parameters.AddWithValue("@exp", srcBatch.ExpiryYearMonth);
                                    insBatch.Parameters.AddWithValue("@unit", srcMed.StockUnit.Trim());
                                    insBatch.Parameters.AddWithValue("@mrp", srcBatch.MrpPaise);
                                    insBatch.Parameters.AddWithValue("@purchase", srcBatch.LatestPurchasePricePaise);
                                    insBatch.Parameters.AddWithValue("@qty", srcBatch.QuantityMilli);
                                    insBatch.Parameters.AddWithValue("@status", status);
                                    insBatch.Parameters.AddWithValue("@created", utcText);
                                    insBatch.Parameters.AddWithValue("@updated", utcText);
                                    insBatch.ExecuteNonQuery();
                                }

                                if (srcBatch.QuantityMilli > 0)
                                {
                                    InsertImportMovement(
                                        connection, transaction, newBatchId, targetMedicineId,
                                        srcBatch.QuantityMilli, importId, "Stock file import (new batch)", utcText, localText);
                                    movementsRecorded++;
                                }
                            }
                        }
                    }

                    if (_testCheckpoint != null) _testCheckpoint("after_medicine_" + i);
                }

                // Reconcile existing batches absent from source snapshot to 0 (NEVER delete historical batches)
                foreach (var kv in existingBatchesById)
                {
                    var existingBatch = kv.Value;
                    if (matchedTargetBatchIds.Contains(existingBatch.BatchId)) continue;
                    if (existingBatch.QuantityMilli <= 0) continue;

                    using (var zeroCmd = new SQLiteCommand(
                        "UPDATE stock_batch SET quantity_milli = 0, updated_at_utc = @updated WHERE batch_id = @id;",
                        connection,
                        transaction))
                    {
                        zeroCmd.Parameters.AddWithValue("@updated", utcText);
                        zeroCmd.Parameters.AddWithValue("@id", existingBatch.BatchId);
                        zeroCmd.ExecuteNonQuery();
                    }

                    InsertImportMovement(
                        connection, transaction, existingBatch.BatchId, existingBatch.MedicineId,
                        checked(-existingBatch.QuantityMilli), importId, "Zeroed during stock file replacement (absent in source)", utcText, localText);
                    movementsRecorded++;
                    batchesZeroed++;
                }

                // Soft-inactivate active medicines absent from source snapshot (NEVER delete)
                foreach (var kv in existingMedsById)
                {
                    var existingMed = kv.Value;
                    if (!existingMed.IsActive || matchedTargetMedicineIds.Contains(existingMed.MedicineId)) continue;

                    using (var inactCmd = new SQLiteCommand(
                        "UPDATE medicine SET is_active = 0, updated_at_utc = @updated WHERE medicine_id = @id;",
                        connection,
                        transaction))
                    {
                        inactCmd.Parameters.AddWithValue("@updated", utcText);
                        inactCmd.Parameters.AddWithValue("@id", existingMed.MedicineId);
                        inactCmd.ExecuteNonQuery();
                    }
                }

                if (_testCheckpoint != null) _testCheckpoint("before_audit_and_commit");

                var serializer = new JavaScriptSerializer();
                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "importId", importId },
                    { "sourceShopId", snapshot.ShopId },
                    { "sourceShopName", snapshot.ShopName },
                    { "formatVersion", snapshot.FormatVersion },
                    { "medicinesImported", medicinesImported },
                    { "batchesImported", batchesImported },
                    { "movementsRecorded", movementsRecorded },
                    { "batchesZeroed", batchesZeroed }
                });

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'STOCK_IMPORTED', 'INVENTORY', @entity, @summary, NULL, @after, @utc, @local);", connection, transaction))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", importId);
                    audit.Parameters.AddWithValue("@summary", string.Format(
                        CultureInfo.InvariantCulture,
                        "Imported StockData (v{0}) from {1}: {2} medicine(s), {3} batch(es), {4} movement(s).",
                        snapshot.FormatVersion,
                        snapshot.ShopName,
                        medicinesImported,
                        batchesImported,
                        movementsRecorded));
                    audit.Parameters.AddWithValue("@after", afterJson);
                    audit.Parameters.AddWithValue("@utc", utcText);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, utcText);
                transaction.Commit();
            }

            var result = new StockImportResult
            {
                Succeeded = true,
                MedicinesImported = medicinesImported,
                BatchesImported = batchesImported,
                StockMovementsRecorded = movementsRecorded,
                BatchesZeroed = batchesZeroed,
                SafetyBackupPath = safetyBackupPath
            };

            if (_snapshotWriter != null)
            {
                try
                {
                    bool snapshotClean = _snapshotWriter.WriteCurrentSnapshot();
                    result.SnapshotUpdated = snapshotClean;
                    if (!snapshotClean)
                        result.SnapshotWarning = "Inventory was imported safely, but newer stock changes occurred while refreshing StockData.json. It remains queued for refresh.";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("Stock snapshot refresh failed after committed stock import: " + ex);
                    result.SnapshotUpdated = false;
                    result.SnapshotWarning = "Inventory was imported safely, but the portable StockData.json file could not be refreshed yet.";
                }
            }

            return result;
        }

        private static bool TryLoadAndValidateFile(string filePath, StockImportPreview preview, out StockSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                preview.Errors.Add("The selected stock file does not exist.");
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                preview.Errors.Add("Could not read the selected stock file: " + ex.Message);
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
                snapshot = serializer.Deserialize<StockSnapshot>(json);
            }
            catch
            {
                preview.Errors.Add("The selected file is not a valid ORVA StockData.json file.");
                return false;
            }

            if (snapshot == null || !string.Equals(snapshot.Format, StockSnapshotWriter.ExpectedFormat, StringComparison.Ordinal))
            {
                preview.Errors.Add("This file is not an ORVA stock file (expected format ORVA_STOCK_DATA).");
                return false;
            }

            if (snapshot.FormatVersion < 1 || snapshot.FormatVersion > StockSnapshotWriter.CurrentFormatVersion)
            {
                preview.Errors.Add("Unsupported StockData format version (" + snapshot.FormatVersion + "). Update ORVA before importing this file.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(snapshot.ShopId) || string.IsNullOrWhiteSpace(snapshot.ShopName))
            {
                preview.Errors.Add("Stock file is missing required shop identity metadata.");
                return false;
            }

            if (snapshot.Medicines == null) snapshot.Medicines = new List<StockSnapshotMedicine>();

            preview.FormatVersion = snapshot.FormatVersion;
            preview.SourceShopId = snapshot.ShopId;
            preview.SourceShopName = snapshot.ShopName;
            preview.GeneratedAtUtc = snapshot.GeneratedAtUtc;

            var seenMedIds = new HashSet<string>(StringComparer.Ordinal);
            var seenBatchIds = new HashSet<string>(StringComparer.Ordinal);
            long totalQty = 0;
            int totalBatches = 0;

            for (int i = 0; i < snapshot.Medicines.Count; i++)
            {
                var med = snapshot.Medicines[i];
                if (med == null)
                {
                    preview.Errors.Add("Stock file contains an empty medicine entry.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(med.MedicineId))
                    preview.Errors.Add("A medicine entry is missing its MedicineId.");
                else if (!seenMedIds.Add(med.MedicineId.Trim()))
                    preview.Errors.Add("Duplicate MedicineId found in stock file: " + med.MedicineId);

                if (string.IsNullOrWhiteSpace(med.Name))
                    preview.Errors.Add("A medicine entry is missing its Name.");
                if (string.IsNullOrWhiteSpace(med.StockUnit))
                    preview.Errors.Add("Medicine '" + (med.Name ?? med.MedicineId) + "' is missing its StockUnit.");
                if (med.GstRateBasisPoints < 0 || med.GstRateBasisPoints > 10000)
                    preview.Errors.Add("Medicine '" + (med.Name ?? med.MedicineId) + "' has an invalid GST rate.");
                if (med.LowStockThresholdMilli < 0)
                    preview.Errors.Add("Medicine '" + (med.Name ?? med.MedicineId) + "' has a negative low-stock threshold.");

                if (med.Batches != null)
                {
                    for (int j = 0; j < med.Batches.Count; j++)
                    {
                        var batch = med.Batches[j];
                        totalBatches++;
                        if (batch == null)
                        {
                            preview.Errors.Add("Stock file contains an empty batch entry.");
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(batch.BatchId))
                            preview.Errors.Add("A batch entry under '" + med.Name + "' is missing its BatchId.");
                        else if (!seenBatchIds.Add(batch.BatchId.Trim()))
                            preview.Errors.Add("Duplicate BatchId found in stock file: " + batch.BatchId);

                        if (string.IsNullOrWhiteSpace(batch.BatchNumber))
                            preview.Errors.Add("A batch under '" + med.Name + "' is missing its BatchNumber.");

                        int month = batch.ExpiryYearMonth % 100;
                        if (batch.ExpiryYearMonth < 200001 || batch.ExpiryYearMonth > 219912 || month < 1 || month > 12)
                            preview.Errors.Add("Batch '" + batch.BatchNumber + "' under '" + med.Name + "' has an invalid expiry month.");

                        if (batch.QuantityMilli < 0)
                            preview.Errors.Add("Batch '" + batch.BatchNumber + "' under '" + med.Name + "' has a negative quantity.");
                        if (batch.MrpPaise < 0 || batch.LatestPurchasePricePaise < 0)
                            preview.Errors.Add("Batch '" + batch.BatchNumber + "' under '" + med.Name + "' has a negative price.");

                        try
                        {
                            totalQty = checked(totalQty + batch.QuantityMilli);
                        }
                        catch (OverflowException)
                        {
                            preview.Errors.Add("Total imported quantity exceeds supported numeric limits.");
                        }
                    }
                }
            }

            preview.MedicineCount = snapshot.Medicines.Count;
            preview.BatchCount = totalBatches;
            preview.TotalImportedQuantityMilli = totalQty;
            return preview.Errors.Count == 0;
        }

        private static void AnalyzeAgainstDatabase(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            StockSnapshot snapshot,
            StockImportPreview preview)
        {
            var existingMeds = LoadExistingMedicines(connection, transaction);
            var existingBatches = LoadExistingBatches(connection, transaction);
            var matchedMedIds = new HashSet<string>(StringComparer.Ordinal);
            var matchedBatchIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < snapshot.Medicines.Count; i++)
            {
                var srcMed = snapshot.Medicines[i];
                var existingMed = MatchMedicine(srcMed, existingMeds);
                string targetMedId = null;

                if (existingMed != null)
                {
                    preview.MatchedMedicineCount++;
                    targetMedId = existingMed.MedicineId;
                    matchedMedIds.Add(targetMedId);

                    if (!string.Equals(existingMed.StockUnitName, srcMed.StockUnit.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        preview.Errors.Add(
                            "Unit conflict for '" + srcMed.Name + "': current unit is '" + existingMed.StockUnitName + "' but file has '" + srcMed.StockUnit + "'.");
                    }
                }
                else
                {
                    preview.NewMedicineCount++;
                }

                if (srcMed.Batches != null)
                {
                    for (int j = 0; j < srcMed.Batches.Count; j++)
                    {
                        var srcBatch = srcMed.Batches[j];
                        var existingBatch = targetMedId == null ? null : MatchBatch(srcBatch, targetMedId, existingBatches);
                        if (existingBatch != null)
                        {
                            matchedBatchIds.Add(existingBatch.BatchId);
                            if (existingBatch.QuantityMilli != srcBatch.QuantityMilli)
                            {
                                preview.ChangedBatchCount++;
                            }
                        }
                        else
                        {
                            preview.NewBatchCount++;
                        }
                    }
                }
            }

            foreach (var kv in existingBatches)
            {
                if (!matchedBatchIds.Contains(kv.Key) && kv.Value.QuantityMilli > 0)
                    preview.ZeroedBatchCount++;
            }

            foreach (var kv in existingMeds)
            {
                if (kv.Value.IsActive && !matchedMedIds.Contains(kv.Key))
                    preview.SoftInactivatedMedicineCount++;
            }
        }

        private static ExistingMedicineRow MatchMedicine(StockSnapshotMedicine src, Dictionary<string, ExistingMedicineRow> existingById)
        {
            if (!string.IsNullOrWhiteSpace(src.MedicineId))
            {
                ExistingMedicineRow byId;
                if (existingById.TryGetValue(src.MedicineId.Trim(), out byId))
                    return byId;
            }

            string srcNorm = NormalizeName(src.Name);
            string srcStrength = (src.Strength ?? string.Empty).Trim();
            string srcDosage = (src.DosageForm ?? string.Empty).Trim();
            foreach (var kv in existingById)
            {
                var cand = kv.Value;
                if (string.Equals(cand.NormalizedName, srcNorm, StringComparison.Ordinal))
                {
                    return cand;
                }
            }

            return null;
        }

        private static ExistingBatchRow MatchBatch(StockSnapshotBatch src, string targetMedicineId, Dictionary<string, ExistingBatchRow> existingById)
        {
            if (!string.IsNullOrWhiteSpace(src.BatchId))
            {
                ExistingBatchRow byId;
                if (existingById.TryGetValue(src.BatchId.Trim(), out byId)
                    && string.Equals(byId.MedicineId, targetMedicineId, StringComparison.Ordinal))
                {
                    return byId;
                }
            }

            string srcNum = (src.BatchNumber ?? string.Empty).Trim().ToUpperInvariant();
            foreach (var kv in existingById)
            {
                var cand = kv.Value;
                if (string.Equals(cand.MedicineId, targetMedicineId, StringComparison.Ordinal)
                    && string.Equals((cand.BatchNumber ?? string.Empty).Trim().ToUpperInvariant(), srcNum, StringComparison.Ordinal)
                    && cand.ExpiryYearMonth == src.ExpiryYearMonth)
                {
                    return cand;
                }
            }

            return null;
        }

        private static Dictionary<string, ExistingMedicineRow> LoadExistingMedicines(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var dict = new Dictionary<string, ExistingMedicineRow>(StringComparer.Ordinal);
            const string sql = @"
SELECT medicine_id, normalized_name, strength, dosage_form, stock_unit_name,
       gst_rate_basis_points, gst_rate_configured, is_active
FROM medicine;";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var row = new ExistingMedicineRow
                    {
                        MedicineId = r.GetString(0),
                        NormalizedName = r.GetString(1),
                        Strength = r.IsDBNull(2) ? null : r.GetString(2),
                        DosageForm = r.IsDBNull(3) ? null : r.GetString(3),
                        StockUnitName = r.GetString(4),
                        GstRateBasisPoints = r.GetInt32(5),
                        GstRateConfigured = r.GetInt32(6) == 1,
                        IsActive = r.GetInt32(7) == 1
                    };
                    dict[row.MedicineId] = row;
                }
            }
            return dict;
        }

        private static Dictionary<string, ExistingBatchRow> LoadExistingBatches(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var dict = new Dictionary<string, ExistingBatchRow>(StringComparer.Ordinal);
            const string sql = "SELECT batch_id, medicine_id, batch_number, expiry_yyyymm, quantity_milli FROM stock_batch;";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var row = new ExistingBatchRow
                    {
                        BatchId = r.GetString(0),
                        MedicineId = r.GetString(1),
                        BatchNumber = r.GetString(2),
                        ExpiryYearMonth = r.GetInt32(3),
                        QuantityMilli = r.GetInt64(4)
                    };
                    dict[row.BatchId] = row;
                }
            }
            return dict;
        }

        private static void InsertImportMovement(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string batchId,
            string medicineId,
            long quantityDeltaMilli,
            string importId,
            string reason,
            string utcText,
            string localText)
        {
            const string sql = @"
INSERT INTO stock_movement(
    movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli,
    reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(
    @id, @batch, @med, 'IMPORT', @delta,
    'STOCK_IMPORT', @ref_id, @reason, @utc, @local);";

            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@batch", batchId);
                cmd.Parameters.AddWithValue("@med", medicineId);
                cmd.Parameters.AddWithValue("@delta", quantityDeltaMilli);
                cmd.Parameters.AddWithValue("@ref_id", importId);
                cmd.Parameters.AddWithValue("@reason", reason);
                cmd.Parameters.AddWithValue("@utc", utcText);
                cmd.Parameters.AddWithValue("@local", localText);
                cmd.ExecuteNonQuery();
            }
        }

        private static string NormalizeBatchStatus(string raw)
        {
            string s = (raw ?? "ACTIVE").Trim().ToUpperInvariant();
            return Array.IndexOf(AllowedBatchStatuses, s) >= 0 ? s : "ACTIVE";
        }

        private static string NormalizeName(string value)
        {
            return string.Join(" ", (value ?? string.Empty).Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim(); }

        private sealed class ExistingMedicineRow
        {
            public string MedicineId;
            public string NormalizedName;
            public string Strength;
            public string DosageForm;
            public string StockUnitName;
            public int GstRateBasisPoints;
            public bool GstRateConfigured;
            public bool IsActive;
        }

        private sealed class ExistingBatchRow
        {
            public string BatchId;
            public string MedicineId;
            public string BatchNumber;
            public int ExpiryYearMonth;
            public long QuantityMilli;
        }
    }
}
