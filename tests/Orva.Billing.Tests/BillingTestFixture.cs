using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;
using Orva.Services.Billing;

namespace Orva.Billing.Tests
{
    public sealed class FakeBillingClock : IBillingClock
    {
        public FakeBillingClock(DateTime utcNow, int localOffsetMinutes)
        {
            UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            LocalOffsetMinutes = localOffsetMinutes;
        }

        public DateTime UtcNow { get; set; }
        public int LocalOffsetMinutes { get; set; }

        public DateTime LocalNow
        {
            get { return UtcNow.AddMinutes(LocalOffsetMinutes); }
        }

        public int CurrentExpiryYearMonth
        {
            get
            {
                var local = LocalNow;
                return local.Year * 100 + local.Month;
            }
        }
    }

    public sealed class BillingTestDatabase : IDisposable
    {
        private readonly string _tempDirectory;
        public string DatabasePath { get; private set; }
        public SqliteDatabase Database { get; private set; }

        public BillingTestDatabase()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "orva_billing_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            DatabasePath = Path.Combine(_tempDirectory, "orva_test.sqlite");
            Database = new SqliteDatabase(DatabasePath);

            var migrator = new DatabaseMigrator(Database);
            migrator.Migrate();
        }

        public BillingService CreateService(IBillingClock clock = null, BillingWriteCheckpoint checkpoint = null)
        {
            var effectiveClock = clock ?? new FakeBillingClock(new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc), 330);
            return new BillingService(new BillingRepository(Database), null, effectiveClock, checkpoint);
        }

        public void SeedDefaultShop(string stateCode = "19", string invoicePrefix = "INV", int fyStartMonth = 4)
        {
            string now = "2026-11-18T10:00:00.0000000Z";
            ExecuteNonQuery(@"
INSERT INTO shop_profile(
    profile_id, shop_id, shop_name, state_name, state_code, gstin,
    invoice_prefix, financial_year_start_month, created_at_utc, updated_at_utc)
VALUES(1, 'shop-001', 'ORVA Test Pharmacy', 'West Bengal', @state, '19ABCDE1234F1ZX', @prefix, @fy_month, @now, @now);",
                p =>
                {
                    p.AddWithValue("@state", (object)stateCode ?? DBNull.Value);
                    p.AddWithValue("@prefix", invoicePrefix);
                    p.AddWithValue("@fy_month", fyStartMonth);
                    p.AddWithValue("@now", now);
                });
        }

        public void SeedTaxConfig(
            bool configured = true,
            string pricingMode = "INCLUSIVE",
            string defaultSupplyType = "INTRA_STATE",
            string roundOffMode = "NONE")
        {
            SetSetting(BillingSettingKeys.TaxConfigured, configured ? "1" : "0");
            if (pricingMode != null) SetSetting(BillingSettingKeys.TaxPricingMode, pricingMode);
            if (defaultSupplyType != null) SetSetting(BillingSettingKeys.DefaultSupplyType, defaultSupplyType);
            if (roundOffMode != null) SetSetting(BillingSettingKeys.RoundOffMode, roundOffMode);
        }

        public void SetSetting(string key, string value)
        {
            string now = "2026-11-18T10:00:00.0000000Z";
            ExecuteNonQuery(@"
INSERT INTO app_setting(setting_key, setting_value, updated_at_utc)
VALUES(@key, @value, @now)
ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value, updated_at_utc = excluded.updated_at_utc;",
                p =>
                {
                    p.AddWithValue("@key", key);
                    p.AddWithValue("@value", value);
                    p.AddWithValue("@now", now);
                });
        }

        public string GetSetting(string key)
        {
            using (var conn = Database.OpenConnection())
            using (var cmd = new SQLiteCommand("SELECT setting_value FROM app_setting WHERE setting_key = @key;", conn))
            {
                cmd.Parameters.AddWithValue("@key", key);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
            }
        }

        public void SeedMedicine(
            string medicineId,
            string displayName,
            string stockUnitName = "strip",
            int gstRateBasisPoints = 1200,
            bool gstRateConfigured = true,
            string hsnCode = "30049099",
            long lowStockThresholdMilli = 0,
            bool isActive = true)
        {
            string now = "2026-11-18T10:00:00.0000000Z";
            ExecuteNonQuery(@"
INSERT INTO medicine(
    medicine_id, display_name, normalized_name, stock_unit_name, hsn_code,
    gst_rate_basis_points, gst_rate_configured, low_stock_threshold_milli,
    is_active, created_at_utc, updated_at_utc)
VALUES(@id, @name, @norm, @unit, @hsn, @gst, @gst_cfg, @low, @active, @now, @now);",
                p =>
                {
                    p.AddWithValue("@id", medicineId);
                    p.AddWithValue("@name", displayName);
                    p.AddWithValue("@norm", displayName.Trim().ToUpperInvariant());
                    p.AddWithValue("@unit", stockUnitName);
                    p.AddWithValue("@hsn", (object)hsnCode ?? DBNull.Value);
                    p.AddWithValue("@gst", gstRateBasisPoints);
                    p.AddWithValue("@gst_cfg", gstRateConfigured ? 1 : 0);
                    p.AddWithValue("@low", lowStockThresholdMilli);
                    p.AddWithValue("@active", isActive ? 1 : 0);
                    p.AddWithValue("@now", now);
                });
        }

        public void SeedBarcode(string barcode, string medicineId)
        {
            ExecuteNonQuery(
                "INSERT INTO barcode_mapping(barcode, medicine_id, created_at_utc) VALUES(@bc, @med, '2026-11-18T10:00:00.0000000Z');",
                p =>
                {
                    p.AddWithValue("@bc", barcode);
                    p.AddWithValue("@med", medicineId);
                });
        }

        public void SeedBatch(
            string batchId,
            string medicineId,
            string batchNumber,
            int expiryYyyyMm,
            long mrpPaise,
            long quantityMilli,
            string status = "ACTIVE",
            string stockUnitName = "strip")
        {
            string now = "2026-11-18T10:00:00.0000000Z";
            ExecuteNonQuery(@"
INSERT INTO stock_batch(
    batch_id, medicine_id, batch_number, expiry_yyyymm, stock_unit_name,
    mrp_paise, latest_purchase_price_paise, quantity_milli, batch_status,
    created_at_utc, updated_at_utc)
VALUES(@id, @med, @num, @exp, @unit, @mrp, @mrp, @qty, @status, @now, @now);",
                p =>
                {
                    p.AddWithValue("@id", batchId);
                    p.AddWithValue("@med", medicineId);
                    p.AddWithValue("@num", batchNumber);
                    p.AddWithValue("@exp", expiryYyyyMm);
                    p.AddWithValue("@unit", stockUnitName);
                    p.AddWithValue("@mrp", mrpPaise);
                    p.AddWithValue("@qty", quantityMilli);
                    p.AddWithValue("@status", status);
                    p.AddWithValue("@now", now);
                });
        }

        public long QueryScalarInt64(string sql, Action<SQLiteParameterCollection> bind = null)
        {
            using (var conn = Database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                if (bind != null) bind(cmd.Parameters);
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public string QueryScalarString(string sql, Action<SQLiteParameterCollection> bind = null)
        {
            using (var conn = Database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                if (bind != null) bind(cmd.Parameters);
                var val = cmd.ExecuteScalar();
                return val == null || val == DBNull.Value ? null : Convert.ToString(val, CultureInfo.InvariantCulture);
            }
        }

        public void ExecuteNonQuery(string sql, Action<SQLiteParameterCollection> bind = null)
        {
            using (var conn = Database.OpenConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                if (bind != null) bind(cmd.Parameters);
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Best-effort cleanup of temp SQLite directory on Windows.
            }
        }
    }
}