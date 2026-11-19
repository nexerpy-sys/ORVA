using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text;
using Orva.Infrastructure.Database;

namespace Orva.Services
{
    public sealed class CatalogueCsvRow
    {
        public int LineNumber { get; set; }
        public string MedicineName { get; set; }
        public string GenericName { get; set; }
        public string BrandName { get; set; }
        public string Strength { get; set; }
        public string DosageForm { get; set; }
        public string StockUnit { get; set; }
        public string HsnCode { get; set; }
        public int? GstRateBasisPoints { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public string Barcode { get; set; }
        public string BatchNumber { get; set; }
        public int? ExpiryYyyyMm { get; set; }
        public long OpeningQuantityMilli { get; set; }
        public long PurchasePricePaise { get; set; }
        public long MrpPaise { get; set; }
    }

    public sealed class CatalogueImportPreview
    {
        public CatalogueImportPreview()
        {
            Rows = new List<CatalogueCsvRow>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public bool IsValid { get; set; }
        public string FilePath { get; set; }
        public int NewMedicineCount { get; set; }
        public int ExistingMedicineCount { get; set; }
        public int OpeningBatchRowCount { get; set; }
        public List<CatalogueCsvRow> Rows { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
    }

    public sealed class CatalogueImportResult
    {
        public bool Succeeded { get; set; }
        public int MedicinesCreated { get; set; }
        public int MedicinesUpdated { get; set; }
        public int BatchesUpdatedOrCreated { get; set; }
        public bool SnapshotRefreshed { get; set; }
        public string SnapshotWarning { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Bulk CSV medicine catalogue and opening-stock importer (supports Excel/Marg CSV exports).
    /// Additive for existing batches; never fabricates supplier invoices; preserves gst_rate_configured safety.
    /// </summary>
    public sealed class MedicineCatalogueImporter
    {
        public static readonly string[] TemplateHeaders = new[]
        {
            "MedicineName", "GenericName", "BrandName", "Strength", "DosageForm",
            "StockUnit", "HsnCode", "GstRatePercent", "LowStockThreshold",
            "Barcode", "BatchNumber", "ExpiryYyyyMm", "OpeningQuantity", "PurchasePrice", "Mrp"
        };

        private readonly SqliteDatabase _database;
        private readonly StockSnapshotWriter _snapshotWriter;

        public MedicineCatalogueImporter(SqliteDatabase database, StockSnapshotWriter snapshotWriter = null)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
            _snapshotWriter = snapshotWriter;
        }

        public static void WriteTemplateCsv(string filePath)
        {
            var sampleRows = new List<IList<string>>
            {
                new[] { "Paracetamol 500mg", "Paracetamol", "Calpol", "500mg", "Tablet", "strip", "30049099", "12", "20", "", "P101", "2028-12", "50", "18.50", "25.00" }
            };
            CsvExportHelper.WriteCsv(filePath, TemplateHeaders, sampleRows);
        }

        public CatalogueImportPreview PreviewCsv(string filePath)
        {
            var preview = new CatalogueImportPreview { FilePath = filePath };
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                preview.Errors.Add("Selected CSV file does not exist.");
                return preview;
            }

            IList<IList<string>> rawRows;
            try
            {
                rawRows = ReadTabularRows(filePath);
            }
            catch (Exception ex)
            {
                preview.Errors.Add("Could not read file: " + ex.Message);
                return preview;
            }

            if (rawRows.Count < 2)
            {
                preview.Errors.Add("Import file must include a header row and at least one medicine row.");
                return preview;
            }

            var headerMap = ParseHeaderColumns(rawRows[0]);
            if (!headerMap.ContainsKey("MEDICINENAME") || !headerMap.ContainsKey("STOCKUNIT"))
            {
                preview.Errors.Add("Header row must include at least 'MedicineName' and 'StockUnit' columns.");
                return preview;
            }

            for (int i = 1; i < rawRows.Count; i++)
            {
                var cols = rawRows[i];
                if (IsRowBlank(cols)) continue;
                var row = new CatalogueCsvRow { LineNumber = i + 1 };

                row.MedicineName = GetCol(cols, headerMap, "MEDICINENAME");
                row.GenericName = GetCol(cols, headerMap, "GENERICNAME");
                row.BrandName = GetCol(cols, headerMap, "BRANDNAME");
                row.Strength = GetCol(cols, headerMap, "STRENGTH");
                row.DosageForm = GetCol(cols, headerMap, "DOSAGEFORM");
                row.StockUnit = GetCol(cols, headerMap, "STOCKUNIT");
                row.HsnCode = GetCol(cols, headerMap, "HSNCODE");
                row.Barcode = GetCol(cols, headerMap, "BARCODE");
                row.BatchNumber = GetCol(cols, headerMap, "BATCHNUMBER");

                if (string.IsNullOrWhiteSpace(row.MedicineName))
                    preview.Errors.Add("Row " + row.LineNumber + ": MedicineName is required.");
                if (string.IsNullOrWhiteSpace(row.StockUnit))
                    preview.Errors.Add("Row " + row.LineNumber + ": StockUnit is required.");

                if (!string.IsNullOrWhiteSpace(row.HsnCode))
                {
                    string hsn = row.HsnCode.Trim();
                    if (hsn.Length < 4 || hsn.Length > 8 || !AllDigits(hsn))
                        preview.Errors.Add("Row " + row.LineNumber + ": Invalid HSN code '" + hsn + "' (must be 4 to 8 digits).");
                }

                string gstRaw = GetCol(cols, headerMap, "GSTRATEPERCENT");
                if (!string.IsNullOrWhiteSpace(gstRaw))
                {
                    decimal gstDec;
                    if (!decimal.TryParse(gstRaw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out gstDec) || gstDec < 0m || gstDec > 100m)
                        preview.Errors.Add("Row " + row.LineNumber + ": Invalid GstRatePercent '" + gstRaw + "'.");
                    else
                        row.GstRateBasisPoints = checked((int)Math.Round(gstDec * 100m, 0, MidpointRounding.AwayFromZero));
                }

                string lowRaw = GetCol(cols, headerMap, "LOWSTOCKTHRESHOLD");
                if (!string.IsNullOrWhiteSpace(lowRaw))
                {
                    decimal lowDec;
                    if (!decimal.TryParse(lowRaw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out lowDec) || lowDec < 0m)
                        preview.Errors.Add("Row " + row.LineNumber + ": Invalid LowStockThreshold '" + lowRaw + "'.");
                    else
                        row.LowStockThresholdMilli = checked((long)Math.Round(lowDec * 1000m, 0, MidpointRounding.AwayFromZero));
                }

                string qtyRaw = GetCol(cols, headerMap, "OPENINGQUANTITY");
                if (!string.IsNullOrWhiteSpace(qtyRaw))
                {
                    decimal qtyDec;
                    if (!decimal.TryParse(qtyRaw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out qtyDec) || qtyDec < 0m)
                        preview.Errors.Add("Row " + row.LineNumber + ": Invalid OpeningQuantity '" + qtyRaw + "'.");
                    else
                        row.OpeningQuantityMilli = checked((long)Math.Round(qtyDec * 1000m, 0, MidpointRounding.AwayFromZero));
                }

                if (row.OpeningQuantityMilli > 0)
                {
                    preview.OpeningBatchRowCount++;
                    if (string.IsNullOrWhiteSpace(row.BatchNumber))
                        preview.Errors.Add("Row " + row.LineNumber + ": BatchNumber is required when OpeningQuantity > 0.");

                    string expRaw = GetCol(cols, headerMap, "EXPIRYYYYYMM");
                    int expYm;
                    if (!TryParseFlexibleExpiryYyyyMm(expRaw, out expYm))
                        preview.Errors.Add("Row " + row.LineNumber + ": Valid Expiry (YYYYMM, YYYY-MM, or MM/YYYY) is required when OpeningQuantity > 0.");
                    else
                        row.ExpiryYyyyMm = expYm;

                    string purRaw = GetCol(cols, headerMap, "PURCHASEPRICE");
                    decimal purDec;
                    if (!string.IsNullOrWhiteSpace(purRaw))
                    {
                        if (!decimal.TryParse(purRaw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out purDec) || purDec < 0m)
                            preview.Errors.Add("Row " + row.LineNumber + ": Invalid PurchasePrice '" + purRaw + "'.");
                        else
                            row.PurchasePricePaise = checked((long)Math.Round(purDec * 100m, 0, MidpointRounding.AwayFromZero));
                    }

                    string mrpRaw = GetCol(cols, headerMap, "MRP");
                    decimal mrpDec;
                    if (!decimal.TryParse((mrpRaw ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out mrpDec) || mrpDec < 0m)
                        preview.Errors.Add("Row " + row.LineNumber + ": Valid Mrp is required when OpeningQuantity > 0.");
                    else
                        row.MrpPaise = checked((long)Math.Round(mrpDec * 100m, 0, MidpointRounding.AwayFromZero));
                }

                preview.Rows.Add(row);
            }

            // Cross-check against existing database medicines
            using (var conn = _database.OpenConnection())
            {
                var existing = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var cmd = new SQLiteCommand("SELECT normalized_name, stock_unit_name FROM medicine;", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) existing[r.GetString(0)] = r.GetString(1);
                }

                var seenExisting = new HashSet<string>(StringComparer.Ordinal);
                var seenNewUnits = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < preview.Rows.Count; i++)
                {
                    var row = preview.Rows[i];
                    if (string.IsNullOrWhiteSpace(row.MedicineName)) continue;
                    string norm = NormalizeName(row.MedicineName);
                    string rowUnit = (row.StockUnit ?? string.Empty).Trim();
                    string existingUnit;
                    if (existing.TryGetValue(norm, out existingUnit))
                    {
                        if (seenExisting.Add(norm)) preview.ExistingMedicineCount++;
                        if (!string.Equals(existingUnit, rowUnit, StringComparison.OrdinalIgnoreCase))
                        {
                            preview.Errors.Add("Row " + row.LineNumber + ": Unit mismatch for '" + row.MedicineName + "' (DB has '" + existingUnit + "', file has '" + row.StockUnit + "').");
                        }
                    }
                    else
                    {
                        string prevNewUnit;
                        if (seenNewUnits.TryGetValue(norm, out prevNewUnit))
                        {
                            if (!string.Equals(prevNewUnit, rowUnit, StringComparison.OrdinalIgnoreCase))
                            {
                                preview.Errors.Add("Row " + row.LineNumber + ": Conflicting StockUnit for new medicine '" + row.MedicineName + "' ('" + prevNewUnit + "' vs '" + rowUnit + "').");
                            }
                        }
                        else
                        {
                            seenNewUnits[norm] = rowUnit;
                            preview.NewMedicineCount++;
                        }
                    }
                }
            }

            preview.IsValid = preview.Errors.Count == 0 && preview.Rows.Count > 0;
            return preview;
        }

        public CatalogueImportResult ExecuteImport(string filePath, bool markProvidedGstRatesAsConfigured)
        {
            var preview = PreviewCsv(filePath);
            if (!preview.IsValid)
            {
                return new CatalogueImportResult
                {
                    Succeeded = false,
                    ErrorMessage = preview.Errors.Count > 0 ? preview.Errors[0] : "CSV catalogue file is invalid."
                };
            }

            var nowUtc = DateTime.UtcNow;
            string utcText = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            string localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string importId = Guid.NewGuid().ToString("N");
            int created = 0, updated = 0, batchesChanged = 0;

            using (var conn = _database.OpenConnection())
            using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
            {
                for (int i = 0; i < preview.Rows.Count; i++)
                {
                    var row = preview.Rows[i];
                    string norm = NormalizeName(row.MedicineName);
                    string medId = null;

                    using (var find = new SQLiteCommand("SELECT medicine_id, stock_unit_name, hsn_code, gst_rate_configured FROM medicine WHERE normalized_name = @norm;", conn, tx))
                    {
                        find.Parameters.AddWithValue("@norm", norm);
                        using (var r = find.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                medId = r.GetString(0);
                                string existingUnit = r.GetString(1);
                                string existingHsn = r.IsDBNull(2) ? null : r.GetString(2);
                                bool alreadyCfg = r.GetInt32(3) == 1;

                                if (!string.Equals(existingUnit, row.StockUnit.Trim(), StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidOperationException("Unit mismatch for " + row.MedicineName);

                                // Update blank HSN or explicitly verified GST rate without overwriting existing display name
                                string newHsn = string.IsNullOrWhiteSpace(existingHsn) && !string.IsNullOrWhiteSpace(row.HsnCode) ? row.HsnCode.Trim() : existingHsn;
                                if (!alreadyCfg && markProvidedGstRatesAsConfigured && row.GstRateBasisPoints.HasValue)
                                {
                                    using (var upd = new SQLiteCommand(
                                        "UPDATE medicine SET is_active = 1, hsn_code = @hsn, gst_rate_basis_points = @gst, gst_rate_configured = 1, updated_at_utc = @utc WHERE medicine_id = @id;",
                                        conn, tx))
                                    {
                                        upd.Parameters.AddWithValue("@hsn", Db(newHsn));
                                        upd.Parameters.AddWithValue("@gst", row.GstRateBasisPoints.Value);
                                        upd.Parameters.AddWithValue("@utc", utcText);
                                        upd.Parameters.AddWithValue("@id", medId);
                                        upd.ExecuteNonQuery();
                                    }
                                    updated++;
                                }
                                else if (!string.Equals(existingHsn, newHsn, StringComparison.Ordinal))
                                {
                                    using (var upd = new SQLiteCommand(
                                        "UPDATE medicine SET is_active = 1, hsn_code = @hsn, updated_at_utc = @utc WHERE medicine_id = @id;",
                                        conn, tx))
                                    {
                                        upd.Parameters.AddWithValue("@hsn", Db(newHsn));
                                        upd.Parameters.AddWithValue("@utc", utcText);
                                        upd.Parameters.AddWithValue("@id", medId);
                                        upd.ExecuteNonQuery();
                                    }
                                    updated++;
                                }
                            }
                        }
                    }

                    if (medId == null)
                    {
                        medId = Guid.NewGuid().ToString("N");
                        bool gstCfg = markProvidedGstRatesAsConfigured && row.GstRateBasisPoints.HasValue;
                        int gstBps = row.GstRateBasisPoints ?? 0;

                        using (var ins = new SQLiteCommand(@"
INSERT INTO medicine(
    medicine_id, display_name, normalized_name, generic_name, brand_name, strength, dosage_form,
    stock_unit_name, hsn_code, gst_rate_basis_points, gst_rate_configured, low_stock_threshold_milli,
    is_active, created_at_utc, updated_at_utc)
VALUES(
    @id, @name, @norm, @generic, @brand, @strength, @dosage,
    @unit, @hsn, @gst, @gst_cfg, @low,
    1, @utc, @utc);", conn, tx))
                        {
                            ins.Parameters.AddWithValue("@id", medId);
                            ins.Parameters.AddWithValue("@name", row.MedicineName.Trim());
                            ins.Parameters.AddWithValue("@norm", norm);
                            ins.Parameters.AddWithValue("@generic", Db(row.GenericName));
                            ins.Parameters.AddWithValue("@brand", Db(row.BrandName));
                            ins.Parameters.AddWithValue("@strength", Db(row.Strength));
                            ins.Parameters.AddWithValue("@dosage", Db(row.DosageForm));
                            ins.Parameters.AddWithValue("@unit", row.StockUnit.Trim());
                            ins.Parameters.AddWithValue("@hsn", Db(row.HsnCode));
                            ins.Parameters.AddWithValue("@gst", gstBps);
                            ins.Parameters.AddWithValue("@gst_cfg", gstCfg ? 1 : 0);
                            ins.Parameters.AddWithValue("@low", row.LowStockThresholdMilli);
                            ins.Parameters.AddWithValue("@utc", utcText);
                            ins.ExecuteNonQuery();
                        }
                        created++;
                    }

                    if (!string.IsNullOrWhiteSpace(row.Barcode))
                        {
                            using (var insBc = new SQLiteCommand(
                                "INSERT OR IGNORE INTO barcode_mapping(barcode, medicine_id, created_at_utc) VALUES(@bc, @med, @utc);",
                                conn,
                                tx))
                            {
                                insBc.Parameters.AddWithValue("@bc", row.Barcode.Trim());
                                insBc.Parameters.AddWithValue("@med", medId);
                                insBc.Parameters.AddWithValue("@utc", utcText);
                                insBc.ExecuteNonQuery();
                            }
                        }

                        if (row.OpeningQuantityMilli > 0 && row.ExpiryYyyyMm.HasValue)
                    {
                        string batchId = null;
                        using (var findB = new SQLiteCommand(
                            "SELECT batch_id FROM stock_batch WHERE medicine_id = @med AND UPPER(TRIM(batch_number)) = UPPER(TRIM(@num)) AND expiry_yyyymm = @exp;",
                            conn, tx))
                        {
                            findB.Parameters.AddWithValue("@med", medId);
                            findB.Parameters.AddWithValue("@num", row.BatchNumber.Trim());
                            findB.Parameters.AddWithValue("@exp", row.ExpiryYyyyMm.Value);
                            batchId = findB.ExecuteScalar() as string;
                        }

                        if (batchId == null)
                        {
                            batchId = Guid.NewGuid().ToString("N");
                            using (var insB = new SQLiteCommand(@"
INSERT INTO stock_batch(
    batch_id, medicine_id, batch_number, expiry_yyyymm, stock_unit_name,
    mrp_paise, latest_purchase_price_paise, quantity_milli, batch_status, created_at_utc, updated_at_utc)
VALUES(@id, @med, @num, @exp, @unit, @mrp, @pur, @qty, 'ACTIVE', @utc, @utc);", conn, tx))
                            {
                                insB.Parameters.AddWithValue("@id", batchId);
                                insB.Parameters.AddWithValue("@med", medId);
                                insB.Parameters.AddWithValue("@num", row.BatchNumber.Trim());
                                insB.Parameters.AddWithValue("@exp", row.ExpiryYyyyMm.Value);
                                insB.Parameters.AddWithValue("@unit", row.StockUnit.Trim());
                                insB.Parameters.AddWithValue("@mrp", row.MrpPaise);
                                insB.Parameters.AddWithValue("@pur", row.PurchasePricePaise);
                                insB.Parameters.AddWithValue("@qty", row.OpeningQuantityMilli);
                                insB.Parameters.AddWithValue("@utc", utcText);
                                insB.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // Additive for existing batch (never silently overwrites existing stock)
                            using (var updB = new SQLiteCommand(@"
UPDATE stock_batch
SET quantity_milli = quantity_milli + @qty,
    mrp_paise = CASE WHEN @mrp > 0 THEN @mrp ELSE mrp_paise END,
    latest_purchase_price_paise = CASE WHEN @pur > 0 THEN @pur ELSE latest_purchase_price_paise END,
    updated_at_utc = @utc
WHERE batch_id = @id;", conn, tx))
                            {
                                updB.Parameters.AddWithValue("@qty", row.OpeningQuantityMilli);
                                updB.Parameters.AddWithValue("@mrp", row.MrpPaise);
                                updB.Parameters.AddWithValue("@pur", row.PurchasePricePaise);
                                updB.Parameters.AddWithValue("@utc", utcText);
                                updB.Parameters.AddWithValue("@id", batchId);
                                updB.ExecuteNonQuery();
                            }
                        }

                        using (var mov = new SQLiteCommand(@"
INSERT INTO stock_movement(movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli, reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(@id, @batch, @med, 'OPENING', @qty, 'CSV_CATALOGUE_IMPORT', @ref_id, 'CSV opening stock import', @utc, @local);", conn, tx))
                        {
                            mov.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                            mov.Parameters.AddWithValue("@batch", batchId);
                            mov.Parameters.AddWithValue("@med", medId);
                            mov.Parameters.AddWithValue("@qty", row.OpeningQuantityMilli);
                            mov.Parameters.AddWithValue("@ref_id", importId);
                            mov.Parameters.AddWithValue("@utc", utcText);
                            mov.Parameters.AddWithValue("@local", localText);
                            mov.ExecuteNonQuery();
                        }

                        batchesChanged++;
                    }
                }

                using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'CATALOGUE_IMPORTED', 'INVENTORY', @entity, @summary, NULL, @after, @utc, @local);", conn, tx))
                {
                    audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    audit.Parameters.AddWithValue("@entity", importId);
                    audit.Parameters.AddWithValue("@summary", string.Format(
                        CultureInfo.InvariantCulture,
                        "Imported CSV catalogue: {0} new medicine(s), {1} updated, {2} opening batch row(s).",
                        created, updated, batchesChanged));
                    audit.Parameters.AddWithValue("@after", "{\"created\":" + created + ",\"updated\":" + updated + ",\"batches\":" + batchesChanged + "}");
                    audit.Parameters.AddWithValue("@utc", utcText);
                    audit.Parameters.AddWithValue("@local", localText);
                    audit.ExecuteNonQuery();
                }

                StockSnapshotState.MarkDirtyAndIncrementGeneration(conn, tx, utcText);
                tx.Commit();
            }

            var res = new CatalogueImportResult
            {
                Succeeded = true,
                MedicinesCreated = created,
                MedicinesUpdated = updated,
                BatchesUpdatedOrCreated = batchesChanged
            };

            if (_snapshotWriter != null)
            {
                try
                {
                    bool snapshotClean = _snapshotWriter.WriteCurrentSnapshot();
                    res.SnapshotRefreshed = snapshotClean;
                    if (!snapshotClean)
                        res.SnapshotWarning = "Catalogue imported safely, but newer stock changes occurred while refreshing StockData.json. It remains queued for refresh.";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("Snapshot refresh failed after CSV catalogue import: " + ex);
                    res.SnapshotWarning = "Catalogue imported safely, but StockData.json could not be refreshed yet.";
                }
            }

            return res;
        }

        public static bool TryParseFlexibleExpiryYyyyMm(string raw, out int yyyyMm)
        {
            yyyyMm = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            double oaSerial;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out oaSerial)
                && oaSerial >= 36526.0 && oaSerial <= 109574.0)
            {
                DateTime dt = DateTime.FromOADate(oaSerial);
                yyyyMm = dt.Year * 100 + dt.Month;
                return true;
            }
            int val;
            if (s.Length == 6 && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
            {
                int m = val % 100;
                if (val >= 200001 && val <= 219912 && m >= 1 && m <= 12) { yyyyMm = val; return true; }
                return false;
            }

            string[] parts = s.Split(new[] { '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                int a, b;
                if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                {
                    int year, month;
                    if (a >= 2000 && a <= 2199 && b >= 1 && b <= 12) { year = a; month = b; }
                    else if (b >= 2000 && b <= 2199 && a >= 1 && a <= 12) { year = b; month = a; }
                    else return false;
                    yyyyMm = year * 100 + month;
                    return true;
                }
            }
            return false;
        }

        private static bool IsRowBlank(IList<string> cols)
        {
            if (cols == null || cols.Count == 0) return true;
            for (int i = 0; i < cols.Count; i++)
                if (!string.IsNullOrWhiteSpace(cols[i])) return false;
            return true;
        }

        private static Dictionary<string, int> ParseHeaderColumns(IList<string> cols)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cols.Count; i++)
            {
                string key = (cols[i] ?? string.Empty).Trim().Trim('﻿').Replace(" ", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key)) map[key] = i;
            }
            return map;
        }

        private static IList<IList<string>> ReadTabularRows(string filePath)
        {
            if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return ReadXlsxSheet1Rows(filePath);

            var rows = new List<IList<string>>();
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
                rows.Add(ParseCsvLine(lines[i]));
            return rows;
        }

        private static IList<IList<string>> ReadXlsxSheet1Rows(string filePath)
        {
            var result = new List<IList<string>>();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var sharedStrings = new List<string>();
                var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (var s = ssEntry.Open())
                    {
                        var doc = XDocument.Load(s);
                        foreach (var si in doc.Descendants().Where(e => e.Name.LocalName == "si"))
                        {
                            var sb = new StringBuilder();
                            foreach (var t in si.Descendants().Where(e => e.Name.LocalName == "t"))
                                sb.Append(t.Value);
                            sharedStrings.Add(sb.ToString());
                        }
                    }
                }

                var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
                if (sheetEntry == null)
                    throw new InvalidOperationException("Worksheet sheet1.xml was not found in the Excel workbook.");

                using (var s = sheetEntry.Open())
                {
                    var doc = XDocument.Load(s);
                    foreach (var rowEl in doc.Descendants().Where(e => e.Name.LocalName == "row"))
                    {
                        var rowCells = new List<string>();
                        foreach (var c in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                        {
                            string cellRef = (string)c.Attribute("r") ?? string.Empty;
                            int colIdx = ParseCellColumnIndex(cellRef);
                            while (rowCells.Count < colIdx) rowCells.Add(string.Empty);

                            string cellType = (string)c.Attribute("t") ?? string.Empty;
                            string val = string.Empty;
                            if (cellType == "s")
                            {
                                var vEl = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                                int idx;
                                if (vEl != null && int.TryParse(vEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)
                                    && idx >= 0 && idx < sharedStrings.Count)
                                    val = sharedStrings[idx];
                            }
                            else if (cellType == "inlineStr")
                            {
                                var sb = new StringBuilder();
                                foreach (var t in c.Descendants().Where(e => e.Name.LocalName == "t"))
                                    sb.Append(t.Value);
                                val = sb.ToString();
                            }
                            else
                            {
                                var vEl = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                                if (vEl != null) val = vEl.Value;
                            }
                            rowCells.Add(val);
                        }
                        result.Add(rowCells);
                    }
                }
            }
            return result;
        }

        private static int ParseCellColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            int col = 0;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char ch = char.ToUpperInvariant(cellRef[i]);
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else break;
            }
            return Math.Max(0, col - 1);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result;
        }

        private static string GetCol(IList<string> cols, Dictionary<string, int> map, string key)
        {
            int idx;
            if (!map.TryGetValue(key, out idx) || idx < 0 || idx >= cols.Count) return string.Empty;
            return (cols[idx] ?? string.Empty).Trim();
        }

        private static bool AllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++) if (!char.IsDigit(s[i])) return false;
            return s.Length > 0;
        }

        private static string NormalizeName(string value)
        {
            return string.Join(" ", (value ?? string.Empty).Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim(); }
    }
}
