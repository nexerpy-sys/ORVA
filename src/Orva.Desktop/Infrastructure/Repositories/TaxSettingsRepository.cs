using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Web.Script.Serialization;
using Orva.Infrastructure.Database;
using Orva.Services.Billing;

namespace Orva.Infrastructure.Repositories
{
    public sealed class MedicineTaxConfigItem
    {
        public string MedicineId { get; set; }
        public string DisplayName { get; set; }
        public string StockUnitName { get; set; }
        public string HsnCode { get; set; }
        public int GstRateBasisPoints { get; set; }
        public bool GstRateConfigured { get; set; }

        public string GstRatePercentText
        {
            get { return (GstRateBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%"; }
        }

        public string StatusText
        {
            get { return GstRateConfigured ? "Configured" : "Not configured"; }
        }
    }

    public sealed class TaxSettingsRepository
    {
        private readonly SqliteDatabase _database;

        public TaxSettingsRepository(SqliteDatabase database)
        {
            if (database == null) throw new ArgumentNullException("database");
            _database = database;
        }

        public BillingTaxSettings GetTaxSettings()
        {
            var settings = new BillingTaxSettings();
            const string sql = @"
SELECT setting_key, setting_value
FROM app_setting
WHERE setting_key IN (@k1, @k2, @k3, @k4);";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
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

        public void SaveTaxSettings(
            bool taxConfigured,
            string pricingMode,
            string defaultSupplyType,
            string roundOffMode)
        {
            string normalizedPricing = NormalizeAllowed(pricingMode, new[] { "INCLUSIVE", "EXCLUSIVE" }, "tax pricing mode");
            string normalizedSupply = NormalizeAllowed(defaultSupplyType, new[] { "INTRA_STATE", "INTER_STATE" }, "default supply type");
            string normalizedRoundOff = NormalizeAllowed(roundOffMode, new[] { "NONE", "NEAREST_RUPEE" }, "round-off mode");

            var nowUtc = DateTime.UtcNow;
            string utcText = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            string localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var serializer = new JavaScriptSerializer();

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var before = ReadTaxSettingsInTransaction(connection, transaction);

                // If marking configured, verify the shop profile has a valid 2-digit Indian GST state code.
                if (taxConfigured)
                {
                    using (var stateCmd = new SQLiteCommand("SELECT state_code FROM shop_profile WHERE profile_id = 1;", connection, transaction))
                    {
                        var stateCodeObj = stateCmd.ExecuteScalar();
                        string stateCode = stateCodeObj == null || stateCodeObj == DBNull.Value
                            ? null
                            : Convert.ToString(stateCodeObj, CultureInfo.InvariantCulture);
                        if (!IndianGstStateCatalog.IsValidShopStateCode(stateCode))
                        {
                            throw new InvalidOperationException("Select a valid shop State and 2-digit State Code before enabling tax configuration.");
                        }
                    }
                }

                UpsertSetting(connection, transaction, BillingSettingKeys.TaxConfigured, taxConfigured ? "1" : "0", utcText);
                UpsertSetting(connection, transaction, BillingSettingKeys.TaxPricingMode, normalizedPricing, utcText);
                UpsertSetting(connection, transaction, BillingSettingKeys.DefaultSupplyType, normalizedSupply, utcText);
                UpsertSetting(connection, transaction, BillingSettingKeys.RoundOffMode, normalizedRoundOff, utcText);

                string beforeJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { BillingSettingKeys.TaxConfigured, before.IsConfigured ? "1" : "0" },
                    { BillingSettingKeys.TaxPricingMode, before.PricingModeRaw },
                    { BillingSettingKeys.DefaultSupplyType, before.DefaultSupplyTypeRaw },
                    { BillingSettingKeys.RoundOffMode, before.RoundOffModeRaw }
                });

                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { BillingSettingKeys.TaxConfigured, taxConfigured ? "1" : "0" },
                    { BillingSettingKeys.TaxPricingMode, normalizedPricing },
                    { BillingSettingKeys.DefaultSupplyType, normalizedSupply },
                    { BillingSettingKeys.RoundOffMode, normalizedRoundOff }
                });

                InsertAuditLog(
                    connection,
                    transaction,
                    "TAX_SETTINGS_UPDATED",
                    "APP_SETTING",
                    "billing.tax",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Updated shop tax configuration (configured={0}, pricing={1}, supply={2}, roundOff={3}).",
                        taxConfigured ? "1" : "0",
                        normalizedPricing,
                        normalizedSupply,
                        normalizedRoundOff),
                    beforeJson,
                    afterJson,
                    utcText,
                    localText);

                transaction.Commit();
            }
        }

        public IList<MedicineTaxConfigItem> GetMedicinesForTaxConfig(string searchText)
        {
            var items = new List<MedicineTaxConfigItem>();
            string search = (searchText ?? string.Empty).Trim().ToUpperInvariant();

            const string sql = @"
SELECT medicine_id, display_name, stock_unit_name, hsn_code, gst_rate_basis_points, gst_rate_configured
FROM medicine
WHERE is_active = 1
  AND (@search = '' OR normalized_name LIKE @like ESCAPE '\' OR UPPER(COALESCE(hsn_code, '')) LIKE @like ESCAPE '\')
ORDER BY gst_rate_configured ASC, display_name COLLATE NOCASE ASC;";

            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@search", search);
                command.Parameters.AddWithValue("@like", "%" + EscapeLike(search) + "%");
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        items.Add(new MedicineTaxConfigItem
                        {
                            MedicineId = reader.GetString(0),
                            DisplayName = reader.GetString(1),
                            StockUnitName = reader.GetString(2),
                            HsnCode = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            GstRateBasisPoints = reader.GetInt32(4),
                            GstRateConfigured = reader.GetInt32(5) == 1
                        });
                    }
                }
            }

            return items;
        }

        public void UpdateMedicineTaxConfig(
            string medicineId,
            string hsnCode,
            int gstRateBasisPoints,
            bool gstRateConfigured)
        {
            if (string.IsNullOrWhiteSpace(medicineId))
                throw new ArgumentException("Select a medicine first.", "medicineId");
            if (gstRateBasisPoints < 0 || gstRateBasisPoints > 10000)
                throw new ArgumentOutOfRangeException("gstRateBasisPoints", "GST rate must be between 0% and 100%.");

            string cleanHsn = string.IsNullOrWhiteSpace(hsnCode) ? null : hsnCode.Trim();
            if (cleanHsn != null)
            {
                if (cleanHsn.Length < 4 || cleanHsn.Length > 8)
                    throw new ArgumentException("HSN code must be 4 to 8 digits when provided.", "hsnCode");
                for (int i = 0; i < cleanHsn.Length; i++)
                {
                    if (!char.IsDigit(cleanHsn[i]))
                        throw new ArgumentException("HSN code must contain digits only.", "hsnCode");
                }
            }

            var nowUtc = DateTime.UtcNow;
            string utcText = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            string localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var serializer = new JavaScriptSerializer();

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                string displayName = null;
                string oldHsn = null;
                int oldRate = 0;
                int oldConfigured = 0;

                using (var find = new SQLiteCommand(
                    "SELECT display_name, hsn_code, gst_rate_basis_points, gst_rate_configured FROM medicine WHERE medicine_id = @id;",
                    connection,
                    transaction))
                {
                    find.Parameters.AddWithValue("@id", medicineId);
                    using (var reader = find.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException("Selected medicine was not found.");

                        displayName = reader.GetString(0);
                        oldHsn = reader.IsDBNull(1) ? null : reader.GetString(1);
                        oldRate = reader.GetInt32(2);
                        oldConfigured = reader.GetInt32(3);
                    }
                }

                using (var update = new SQLiteCommand(@"
UPDATE medicine
SET hsn_code = @hsn,
    gst_rate_basis_points = @rate,
    gst_rate_configured = @configured,
    updated_at_utc = @updated
WHERE medicine_id = @id;", connection, transaction))
                {
                    update.Parameters.AddWithValue("@hsn", cleanHsn == null ? (object)DBNull.Value : cleanHsn);
                    update.Parameters.AddWithValue("@rate", gstRateBasisPoints);
                    update.Parameters.AddWithValue("@configured", gstRateConfigured ? 1 : 0);
                    update.Parameters.AddWithValue("@updated", utcText);
                    update.Parameters.AddWithValue("@id", medicineId);
                    update.ExecuteNonQuery();
                }

                string beforeJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "hsnCode", oldHsn },
                    { "gstRateBasisPoints", oldRate },
                    { "gstRateConfigured", oldConfigured == 1 }
                });

                string afterJson = serializer.Serialize(new Dictionary<string, object>
                {
                    { "hsnCode", cleanHsn },
                    { "gstRateBasisPoints", gstRateBasisPoints },
                    { "gstRateConfigured", gstRateConfigured }
                });

                InsertAuditLog(
                    connection,
                    transaction,
                    "MEDICINE_TAX_UPDATED",
                    "MEDICINE",
                    medicineId,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Updated GST/HSN for {0} (HSN={1}, rateBps={2}, configured={3}).",
                        displayName,
                        cleanHsn ?? "none",
                        gstRateBasisPoints,
                        gstRateConfigured ? "1" : "0"),
                    beforeJson,
                    afterJson,
                    utcText,
                    localText);

                transaction.Commit();
            }
        }

        private static BillingTaxSettings ReadTaxSettingsInTransaction(SQLiteConnection connection, SQLiteTransaction transaction)
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

        private static void UpsertSetting(SQLiteConnection connection, SQLiteTransaction transaction, string key, string value, string utcText)
        {
            const string sql = @"
INSERT INTO app_setting(setting_key, setting_value, updated_at_utc)
VALUES(@key, @value, @updated)
ON CONFLICT(setting_key) DO UPDATE
SET setting_value = excluded.setting_value,
    updated_at_utc = excluded.updated_at_utc;";

            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.Parameters.AddWithValue("@updated", utcText);
                cmd.ExecuteNonQuery();
            }
        }

        private static void InsertAuditLog(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string actionType,
            string entityType,
            string entityId,
            string summary,
            string beforeJson,
            string afterJson,
            string utcText,
            string localText)
        {
            const string sql = @"
INSERT INTO audit_log(
    audit_id, action_type, entity_type, entity_id, summary,
    before_json, after_json, created_at_utc, created_local_text)
VALUES(
    @id, @action, @entity_type, @entity_id, @summary,
    @before_json, @after_json, @utc, @local);";

            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@action", actionType);
                cmd.Parameters.AddWithValue("@entity_type", entityType);
                cmd.Parameters.AddWithValue("@entity_id", entityId);
                cmd.Parameters.AddWithValue("@summary", summary);
                cmd.Parameters.AddWithValue("@before_json", (object)beforeJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@after_json", (object)afterJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@utc", utcText);
                cmd.Parameters.AddWithValue("@local", localText);
                cmd.ExecuteNonQuery();
            }
        }

        private static string NormalizeAllowed(string raw, string[] allowed, string label)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("Choose a valid " + label + ".");

            string trimmed = raw.Trim().ToUpperInvariant();
            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(trimmed, allowed[i], StringComparison.Ordinal))
                    return allowed[i];
            }

            throw new ArgumentException("Unsupported " + label + ": " + raw);
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
