using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Web.Script.Serialization;
using Orva.Domain.Billing;
using Orva.Infrastructure.Database;
using Orva.Services;
using Orva.Services.Billing;

namespace Orva.Infrastructure.Repositories
{
    public sealed class BillingRepository
    {
        private readonly SqliteDatabase _database;

        public BillingRepository(SqliteDatabase database)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
        }

        public IList<MedicineLookupResult> SearchMedicines(string query, int maximumResults, int currentExpiryYearMonth)
        {
            var results = new List<MedicineLookupResult>();
            if (maximumResults <= 0) return results;

            string trimmed = (query ?? string.Empty).Trim();
            string normalized = NormalizeSearchText(trimmed);

            const string sql = @"
SELECT m.medicine_id,
       m.display_name,
       m.generic_name,
       m.brand_name,
       m.strength,
       m.dosage_form,
       m.stock_unit_name,
       m.low_stock_threshold_milli,
       COALESCE((
           SELECT SUM(b.quantity_milli)
           FROM stock_batch b
           WHERE b.medicine_id = m.medicine_id
             AND b.batch_status = 'ACTIVE'
             AND b.quantity_milli > 0
             AND b.expiry_yyyymm >= @current_month
       ), 0) AS available_qty_milli,
       CASE
           WHEN @raw <> '' AND EXISTS (
               SELECT 1 FROM barcode_mapping bm
               WHERE bm.medicine_id = m.medicine_id AND bm.barcode = @raw
           ) THEN 0
           WHEN @norm <> '' AND m.normalized_name LIKE @prefix ESCAPE '\' THEN 1
           ELSE 2
       END AS match_rank
FROM medicine m
WHERE m.is_active = 1
  AND (
      @norm = ''
      OR m.normalized_name LIKE @like ESCAPE '\'
      OR UPPER(COALESCE(m.generic_name, '')) LIKE @like ESCAPE '\'
      OR UPPER(COALESCE(m.brand_name, '')) LIKE @like ESCAPE '\'
      OR EXISTS (
          SELECT 1 FROM barcode_mapping bm
          WHERE bm.medicine_id = m.medicine_id
            AND bm.barcode = @raw
      )
  )
ORDER BY match_rank ASC, m.display_name COLLATE NOCASE ASC
LIMIT @limit;";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@current_month", currentExpiryYearMonth);
                command.Parameters.AddWithValue("@raw", trimmed);
                command.Parameters.AddWithValue("@norm", normalized);
                command.Parameters.AddWithValue("@prefix", EscapeLike(normalized) + "%");
                command.Parameters.AddWithValue("@like", "%" + EscapeLike(normalized) + "%");
                command.Parameters.AddWithValue("@limit", maximumResults);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long available = reader.GetInt64(8);
                        long threshold = reader.GetInt64(7);
                        string unit = reader.GetString(6);
                        bool canSell = available > 0;
                        bool isLowStock = canSell && threshold > 0 && available <= threshold;

                        results.Add(new MedicineLookupResult
                        {
                            MedicineId = reader.GetString(0),
                            DisplayName = reader.GetString(1),
                            GenericName = ReadNullableString(reader, 2),
                            BrandName = ReadNullableString(reader, 3),
                            Strength = ReadNullableString(reader, 4),
                            DosageForm = ReadNullableString(reader, 5),
                            StockUnitName = unit,
                            AvailableQuantityMilli = available,
                            LowStockThresholdMilli = threshold,
                            IsLowStock = isLowStock,
                            CanSell = canSell,
                            AvailabilityMessage = BuildAvailabilityMessage(available, unit, isLowStock)
                        });
                    }
                }
            }

            return results;
        }

        public BillingDataSnapshot LoadReadOnlySnapshot(IList<string> medicineIds, int currentExpiryYearMonth)
        {
            using (var connection = _database.OpenConnection())
            {
                return LoadSnapshot(connection, null, medicineIds, currentExpiryYearMonth);
            }
        }

        public BillingDataSnapshot LoadSnapshot(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            IList<string> medicineIds,
            int currentExpiryYearMonth)
        {
            var snapshot = new BillingDataSnapshot
            {
                Shop = ReadShopContext(connection, transaction),
                TaxSettings = ReadTaxSettings(connection, transaction)
            };

            if (medicineIds == null || medicineIds.Count == 0)
            {
                return snapshot;
            }

            for (int i = 0; i < medicineIds.Count; i++)
            {
                string medicineId = medicineIds[i];
                if (string.IsNullOrWhiteSpace(medicineId) || snapshot.Medicines.ContainsKey(medicineId))
                {
                    continue;
                }

                var medicine = ReadMedicine(connection, transaction, medicineId);
                if (medicine != null)
                {
                    snapshot.Medicines.Add(medicineId, medicine);
                    snapshot.EligibleBatchesByMedicine.Add(
                        medicineId,
                        ReadEligibleBatches(connection, transaction, medicineId, currentExpiryYearMonth));
                }
            }

            return snapshot;
        }

        public SQLiteConnection OpenWriteConnection()
        {
            return _database.OpenConnection();
        }

        public CommittedInvoiceRecord PersistInvoiceInTransaction(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            BillingShopContext shop,
            ResolvedTaxPolicy taxPolicy,
            CalculatedInvoice calculated,
            InvoiceCustomerDraft customer,
            IList<ValidatedTender> tenders,
            DateTime issuedAtUtc,
            int localOffsetMinutes,
            DateTime localNow,
            int currentExpiryYearMonth,
            BillingWriteCheckpoint checkpoint)
        {
            string utcText = issuedAtUtc.ToString("o", CultureInfo.InvariantCulture);
            string localText = localNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string financialYear = InvoiceCalculator.GetFinancialYearLabel(localNow, shop.FinancialYearStartMonth);

            long nextSequence = IncrementInvoiceSequence(connection, transaction, financialYear);
            InvokeCheckpoint(checkpoint, "after_sequence_increment");

            string invoiceId = Guid.NewGuid().ToString("N");
            string invoiceNumber = InvoiceCalculator.FormatInvoiceNumber(shop.InvoicePrefix, financialYear, nextSequence);

            InsertInvoiceHeader(
                connection,
                transaction,
                invoiceId,
                invoiceNumber,
                financialYear,
                utcText,
                localOffsetMinutes,
                customer,
                taxPolicy.PlaceOfSupplyStateCode,
                calculated);
            InvokeCheckpoint(checkpoint, "after_invoice_insert");

            var beforeBatches = new List<Dictionary<string, object>>();
            var afterBatches = new List<Dictionary<string, object>>();

            for (int i = 0; i < calculated.Lines.Count; i++)
            {
                var line = calculated.Lines[i];
                for (int j = 0; j < line.BatchLines.Count; j++)
                {
                    var batchLine = line.BatchLines[j];

                    InsertInvoiceLine(
                        connection,
                        transaction,
                        invoiceId,
                        line.LineGroupId,
                        line.MedicineId,
                        line.MedicineName,
                        batchLine,
                        utcText);
                    InvokeCheckpoint(checkpoint, "after_invoice_line_insert");

                    DeductBatchStock(
                        connection,
                        transaction,
                        batchLine.BatchId,
                        batchLine.QuantityMilli,
                        currentExpiryYearMonth,
                        utcText);
                    InvokeCheckpoint(checkpoint, "after_batch_deduction");

                    InsertSaleStockMovement(
                        connection,
                        transaction,
                        batchLine.BatchId,
                        line.MedicineId,
                        batchLine.QuantityMilli,
                        invoiceId,
                        invoiceNumber,
                        utcText,
                        localText);
                    InvokeCheckpoint(checkpoint, "after_stock_movement_insert");

                    long postQty = checked(batchLine.PreviousQuantityMilli - batchLine.QuantityMilli);
                    beforeBatches.Add(new Dictionary<string, object>
                    {
                        { "batchId", batchLine.BatchId },
                        { "batchNumber", batchLine.BatchNumber },
                        { "quantityMilli", batchLine.PreviousQuantityMilli }
                    });
                    afterBatches.Add(new Dictionary<string, object>
                    {
                        { "batchId", batchLine.BatchId },
                        { "batchNumber", batchLine.BatchNumber },
                        { "quantityMilli", postQty }
                    });
                }
            }

            var paymentSummaries = new List<Dictionary<string, object>>();
            for (int i = 0; i < tenders.Count; i++)
            {
                var tender = tenders[i];
                InsertPayment(connection, transaction, invoiceId, tender, utcText);
                paymentSummaries.Add(new Dictionary<string, object>
                {
                    { "method", tender.DatabaseMethod },
                    { "amountPaise", tender.AmountPaise }
                });
            }
            InvokeCheckpoint(checkpoint, "after_payment_insert");

            InsertAuditLog(
                connection,
                transaction,
                invoiceId,
                invoiceNumber,
                calculated,
                beforeBatches,
                afterBatches,
                paymentSummaries,
                utcText,
                localText);
            InvokeCheckpoint(checkpoint, "after_audit_log_insert");

            MarkStockSnapshotDirty(connection, transaction, utcText);
            InvokeCheckpoint(checkpoint, "after_snapshot_dirty_update");

            return new CommittedInvoiceRecord
            {
                InvoiceId = invoiceId,
                InvoiceNumber = invoiceNumber,
                FinancialYear = financialYear,
                IssuedAtUtc = issuedAtUtc,
                IssuedLocalOffsetMinutes = localOffsetMinutes,
                GrandTotalPaise = calculated.GrandTotalPaise
            };
        }

        private static BillingShopContext ReadShopContext(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            const string sql = @"
SELECT shop_id, shop_name, state_code, gstin, invoice_prefix, financial_year_start_month
FROM shop_profile
WHERE profile_id = 1;";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) return null;
                return new BillingShopContext
                {
                    ShopId = reader.GetString(0),
                    ShopName = reader.GetString(1),
                    StateCode = ReadNullableString(reader, 2),
                    Gstin = ReadNullableString(reader, 3),
                    InvoicePrefix = reader.GetString(4),
                    FinancialYearStartMonth = reader.GetInt32(5)
                };
            }
        }

        private static BillingTaxSettings ReadTaxSettings(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var settings = new BillingTaxSettings();
            const string sql = @"
SELECT setting_key, setting_value
FROM app_setting
WHERE setting_key IN (@k1, @k2, @k3, @k4);";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@k1", BillingSettingKeys.TaxConfigured);
                command.Parameters.AddWithValue("@k2", BillingSettingKeys.TaxPricingMode);
                command.Parameters.AddWithValue("@k3", BillingSettingKeys.DefaultSupplyType);
                command.Parameters.AddWithValue("@k4", BillingSettingKeys.RoundOffMode);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = reader.GetString(0);
                        string value = reader.GetString(1);

                        if (string.Equals(key, BillingSettingKeys.TaxConfigured, StringComparison.Ordinal))
                            settings.IsConfigured = string.Equals(value, "1", StringComparison.Ordinal);
                        else if (string.Equals(key, BillingSettingKeys.TaxPricingMode, StringComparison.Ordinal))
                            settings.PricingModeRaw = value;
                        else if (string.Equals(key, BillingSettingKeys.DefaultSupplyType, StringComparison.Ordinal))
                            settings.DefaultSupplyTypeRaw = value;
                        else if (string.Equals(key, BillingSettingKeys.RoundOffMode, StringComparison.Ordinal))
                            settings.RoundOffModeRaw = value;
                    }
                }
            }

            return settings;
        }

        private static BillingMedicineRecord ReadMedicine(SQLiteConnection connection, SQLiteTransaction transaction, string medicineId)
        {
            const string sql = @"
SELECT medicine_id, display_name, generic_name, brand_name, strength, dosage_form,
       stock_unit_name, hsn_code, gst_rate_basis_points, gst_rate_configured,
       low_stock_threshold_milli, is_active
FROM medicine
WHERE medicine_id = @id;";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", medicineId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new BillingMedicineRecord
                    {
                        MedicineId = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        GenericName = ReadNullableString(reader, 2),
                        BrandName = ReadNullableString(reader, 3),
                        Strength = ReadNullableString(reader, 4),
                        DosageForm = ReadNullableString(reader, 5),
                        StockUnitName = reader.GetString(6),
                        HsnCode = ReadNullableString(reader, 7),
                        GstRateBasisPoints = reader.GetInt32(8),
                        GstRateConfigured = reader.GetInt32(9) == 1,
                        LowStockThresholdMilli = reader.GetInt64(10),
                        IsActive = reader.GetInt32(11) == 1
                    };
                }
            }
        }

        private static List<BillingBatchRecord> ReadEligibleBatches(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string medicineId,
            int currentExpiryYearMonth)
        {
            var batches = new List<BillingBatchRecord>();
            const string sql = @"
SELECT batch_id, medicine_id, batch_number, expiry_yyyymm, stock_unit_name,
       mrp_paise, quantity_milli, batch_status
FROM stock_batch
WHERE medicine_id = @medicine_id
  AND batch_status = 'ACTIVE'
  AND quantity_milli > 0
  AND expiry_yyyymm >= @current_month
ORDER BY expiry_yyyymm ASC, UPPER(TRIM(batch_number)) ASC, batch_id ASC;";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@medicine_id", medicineId);
                command.Parameters.AddWithValue("@current_month", currentExpiryYearMonth);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        batches.Add(new BillingBatchRecord
                        {
                            BatchId = reader.GetString(0),
                            MedicineId = reader.GetString(1),
                            BatchNumber = reader.GetString(2),
                            ExpiryYyyyMm = reader.GetInt32(3),
                            StockUnitName = reader.GetString(4),
                            MrpPaise = reader.GetInt64(5),
                            QuantityMilli = reader.GetInt64(6),
                            BatchStatus = reader.GetString(7)
                        });
                    }
                }
            }

            return batches;
        }

        private static long IncrementInvoiceSequence(SQLiteConnection connection, SQLiteTransaction transaction, string financialYear)
        {
            const string upsertSql = @"
INSERT INTO invoice_sequence(financial_year, last_number)
VALUES(@fy, 1)
ON CONFLICT(financial_year) DO UPDATE SET last_number = last_number + 1;";

            using (var upsert = new SQLiteCommand(upsertSql, connection, transaction))
            {
                upsert.Parameters.AddWithValue("@fy", financialYear);
                upsert.ExecuteNonQuery();
            }

            using (var select = new SQLiteCommand("SELECT last_number FROM invoice_sequence WHERE financial_year = @fy;", connection, transaction))
            {
                select.Parameters.AddWithValue("@fy", financialYear);
                return Convert.ToInt64(select.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static void InsertInvoiceHeader(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string invoiceId,
            string invoiceNumber,
            string financialYear,
            string utcText,
            int localOffsetMinutes,
            InvoiceCustomerDraft customer,
            string placeOfSupplyStateCode,
            CalculatedInvoice calculated)
        {
            const string sql = @"
INSERT INTO invoice(
    invoice_id, invoice_number, financial_year, issued_at_utc, issued_local_offset_minutes,
    customer_name, customer_phone, customer_address, customer_gstin, place_of_supply_state_code,
    subtotal_paise, discount_paise, taxable_value_paise, cgst_paise, sgst_paise, igst_paise,
    round_off_paise, grand_total_paise, status, cancellation_reason, created_at_utc)
VALUES(
    @id, @number, @fy, @issued_utc, @offset_min,
    @cust_name, @cust_phone, @cust_addr, @cust_gstin, @pos_state,
    @subtotal, @discount, @taxable, @cgst, @sgst, @igst,
    @round_off, @grand_total, 'ISSUED', NULL, @created_utc);";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", invoiceId);
                command.Parameters.AddWithValue("@number", invoiceNumber);
                command.Parameters.AddWithValue("@fy", financialYear);
                command.Parameters.AddWithValue("@issued_utc", utcText);
                command.Parameters.AddWithValue("@offset_min", localOffsetMinutes);
                command.Parameters.AddWithValue("@cust_name", DbNullable(customer != null ? customer.Name : null));
                command.Parameters.AddWithValue("@cust_phone", DbNullable(customer != null ? customer.Phone : null));
                command.Parameters.AddWithValue("@cust_addr", DbNullable(customer != null ? customer.Address : null));
                command.Parameters.AddWithValue("@cust_gstin", DbNullable(customer != null ? NormalizeGstin(customer.Gstin) : null));
                command.Parameters.AddWithValue("@pos_state", DbNullable(placeOfSupplyStateCode));
                command.Parameters.AddWithValue("@subtotal", calculated.SubtotalPaise);
                command.Parameters.AddWithValue("@discount", calculated.DiscountPaise);
                command.Parameters.AddWithValue("@taxable", calculated.TaxableValuePaise);
                command.Parameters.AddWithValue("@cgst", calculated.CgstPaise);
                command.Parameters.AddWithValue("@sgst", calculated.SgstPaise);
                command.Parameters.AddWithValue("@igst", calculated.IgstPaise);
                command.Parameters.AddWithValue("@round_off", calculated.RoundOffPaise);
                command.Parameters.AddWithValue("@grand_total", calculated.GrandTotalPaise);
                command.Parameters.AddWithValue("@created_utc", utcText);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertInvoiceLine(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string invoiceId,
            string lineGroupId,
            string medicineId,
            string medicineNameSnapshot,
            CalculatedBatchLine batchLine,
            string utcText)
        {
            const string sql = @"
INSERT INTO invoice_line(
    invoice_line_id, invoice_id, line_group_id, medicine_id, batch_id,
    medicine_name_snapshot, batch_number_snapshot, expiry_yyyymm_snapshot,
    quantity_milli, unit_name_snapshot, unit_price_paise, discount_paise,
    taxable_value_paise, gst_rate_basis_points, hsn_code_snapshot,
    cgst_paise, sgst_paise, igst_paise, line_total_paise, created_at_utc)
VALUES(
    @id, @invoice_id, @group_id, @medicine_id, @batch_id,
    @med_name, @batch_num, @expiry,
    @qty, @unit_name, @unit_price, @discount,
    @taxable, @gst_bps, @hsn,
    @cgst, @sgst, @igst, @line_total, @created_utc);";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("@invoice_id", invoiceId);
                command.Parameters.AddWithValue("@group_id", lineGroupId);
                command.Parameters.AddWithValue("@medicine_id", medicineId);
                command.Parameters.AddWithValue("@batch_id", batchLine.BatchId);
                command.Parameters.AddWithValue("@med_name", medicineNameSnapshot);
                command.Parameters.AddWithValue("@batch_num", batchLine.BatchNumber);
                command.Parameters.AddWithValue("@expiry", batchLine.ExpiryYyyyMm);
                command.Parameters.AddWithValue("@qty", batchLine.QuantityMilli);
                command.Parameters.AddWithValue("@unit_name", batchLine.StockUnitName);
                command.Parameters.AddWithValue("@unit_price", batchLine.UnitPricePaise);
                command.Parameters.AddWithValue("@discount", batchLine.DiscountPaise);
                command.Parameters.AddWithValue("@taxable", batchLine.TaxableValuePaise);
                command.Parameters.AddWithValue("@gst_bps", batchLine.GstRateBasisPoints);
                command.Parameters.AddWithValue("@hsn", DbNullable(batchLine.HsnCode));
                command.Parameters.AddWithValue("@cgst", batchLine.CgstPaise);
                command.Parameters.AddWithValue("@sgst", batchLine.SgstPaise);
                command.Parameters.AddWithValue("@igst", batchLine.IgstPaise);
                command.Parameters.AddWithValue("@line_total", batchLine.LineTotalPaise);
                command.Parameters.AddWithValue("@created_utc", utcText);
                command.ExecuteNonQuery();
            }
        }

        private static void DeductBatchStock(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string batchId,
            long deductQuantityMilli,
            int currentExpiryYearMonth,
            string utcText)
        {
            const string sql = @"
UPDATE stock_batch
SET quantity_milli = quantity_milli - @deduct,
    updated_at_utc = @updated
WHERE batch_id = @batch_id
  AND batch_status = 'ACTIVE'
  AND expiry_yyyymm >= @current_month
  AND quantity_milli >= @deduct;";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@deduct", deductQuantityMilli);
                command.Parameters.AddWithValue("@updated", utcText);
                command.Parameters.AddWithValue("@batch_id", batchId);
                command.Parameters.AddWithValue("@current_month", currentExpiryYearMonth);

                int affected = command.ExecuteNonQuery();
                if (affected != 1)
                {
                    throw new InvalidOperationException("Batch stock changed or became ineligible during invoice finalisation.");
                }
            }
        }

        private static void InsertSaleStockMovement(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string batchId,
            string medicineId,
            long deductedQuantityMilli,
            string invoiceId,
            string invoiceNumber,
            string utcText,
            string localText)
        {
            const string sql = @"
INSERT INTO stock_movement(
    movement_id, batch_id, medicine_id, movement_type, quantity_delta_milli,
    reference_type, reference_id, reason, created_at_utc, created_local_text)
VALUES(
    @id, @batch_id, @medicine_id, 'SALE', @delta,
    'INVOICE', @ref_id, @reason, @utc, @local);";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("@batch_id", batchId);
                command.Parameters.AddWithValue("@medicine_id", medicineId);
                command.Parameters.AddWithValue("@delta", checked(-deductedQuantityMilli));
                command.Parameters.AddWithValue("@ref_id", invoiceId);
                command.Parameters.AddWithValue("@reason", "Sale invoice " + invoiceNumber);
                command.Parameters.AddWithValue("@utc", utcText);
                command.Parameters.AddWithValue("@local", localText);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertPayment(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string invoiceId,
            ValidatedTender tender,
            string utcText)
        {
            const string sql = @"
INSERT INTO payment(payment_id, invoice_id, method, amount_paise, reference_text, paid_at_utc)
VALUES(@id, @invoice_id, @method, @amount, @reference, @paid_utc);";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("@invoice_id", invoiceId);
                command.Parameters.AddWithValue("@method", tender.DatabaseMethod);
                command.Parameters.AddWithValue("@amount", tender.AmountPaise);
                command.Parameters.AddWithValue("@reference", DbNullable(tender.ReferenceText));
                command.Parameters.AddWithValue("@paid_utc", utcText);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertAuditLog(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string invoiceId,
            string invoiceNumber,
            CalculatedInvoice calculated,
            List<Dictionary<string, object>> beforeBatches,
            List<Dictionary<string, object>> afterBatches,
            List<Dictionary<string, object>> payments,
            string utcText,
            string localText)
        {
            var serializer = new JavaScriptSerializer();
            string beforeJson = serializer.Serialize(new Dictionary<string, object>
            {
                { "batches", beforeBatches }
            });
            string afterJson = serializer.Serialize(new Dictionary<string, object>
            {
                { "invoiceId", invoiceId },
                { "invoiceNumber", invoiceNumber },
                { "grandTotalPaise", calculated.GrandTotalPaise },
                { "taxableValuePaise", calculated.TaxableValuePaise },
                { "cgstPaise", calculated.CgstPaise },
                { "sgstPaise", calculated.SgstPaise },
                { "igstPaise", calculated.IgstPaise },
                { "roundOffPaise", calculated.RoundOffPaise },
                { "payments", payments },
                { "batches", afterBatches }
            });

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Issued invoice {0} for Rs. {1} ({2} medicine line(s)).",
                invoiceNumber,
                InvoiceCalculator.FormatRupees(calculated.GrandTotalPaise),
                calculated.Lines.Count);

            const string sql = @"
INSERT INTO audit_log(
    audit_id, action_type, entity_type, entity_id, summary,
    before_json, after_json, created_at_utc, created_local_text)
VALUES(
    @id, 'INVOICE_ISSUED', 'INVOICE', @entity_id, @summary,
    @before_json, @after_json, @utc, @local);";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("@entity_id", invoiceId);
                command.Parameters.AddWithValue("@summary", summary);
                command.Parameters.AddWithValue("@before_json", beforeJson);
                command.Parameters.AddWithValue("@after_json", afterJson);
                command.Parameters.AddWithValue("@utc", utcText);
                command.Parameters.AddWithValue("@local", localText);
                command.ExecuteNonQuery();
            }
        }

        private static void MarkStockSnapshotDirty(SQLiteConnection connection, SQLiteTransaction transaction, string utcText)
        {
            StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, utcText);
        }

        private static void InvokeCheckpoint(BillingWriteCheckpoint checkpoint, string name)
        {
            if (checkpoint != null)
            {
                checkpoint(name);
            }
        }

        private static string BuildAvailabilityMessage(long availableMilli, string unitName, bool isLowStock)
        {
            if (availableMilli <= 0)
            {
                return "Out of stock";
            }

            string qtyText = InvoiceCalculator.FormatQuantity(availableMilli);
            string unitText = string.IsNullOrWhiteSpace(unitName) ? "units" : unitName.Trim();
            if (isLowStock)
            {
                return string.Format(CultureInfo.InvariantCulture, "Low stock: {0} {1} available", qtyText, unitText);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} {1} available", qtyText, unitText);
        }

        private static string NormalizeSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var parts = value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).ToUpperInvariant();
        }

        private static string NormalizeGstin(string gstin)
        {
            return string.IsNullOrWhiteSpace(gstin) ? null : gstin.Trim().ToUpperInvariant();
        }

        private static object DbNullable(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim();
        }

        private static string ReadNullableString(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static string EscapeLike(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string collapsed = string.Join(" ", parts);
            return collapsed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }
    }
}