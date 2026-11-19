using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Web.Script.Serialization;
using Orva.Domain;
using Orva.Infrastructure.Database;
using Orva.Services.Billing;

namespace Orva.Infrastructure.Repositories
{
    public sealed class ShopRepository
    {
        private readonly SqliteDatabase _database;

        public ShopRepository(SqliteDatabase database) { _database = database; }

        public ShopProfile GetProfile()
        {
            using (var connection = _database.OpenConnection())
            {
                return ReadProfile(connection, null);
            }
        }

        public void SaveProfile(ShopProfile profile)
        {
            SaveProfileInternal(profile, writeAudit: false);
        }

        public void SaveProfileWithAudit(ShopProfile profile)
        {
            SaveProfileInternal(profile, writeAudit: true);
        }

        private void SaveProfileInternal(ShopProfile profile, bool writeAudit)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (string.IsNullOrWhiteSpace(profile.ShopName)) throw new ArgumentException("Enter the shop name.");

            string normalizedPrefix = string.IsNullOrWhiteSpace(profile.InvoicePrefix)
                ? "INV"
                : profile.InvoicePrefix.Trim().ToUpperInvariant();

            if (!InvoiceCalculator.IsValidInvoicePrefix(normalizedPrefix))
                throw new ArgumentException("Invoice prefix must be 1 to 3 alphanumeric characters so invoice numbers stay within the 16-character GST limit.");

            string normalizedGstin = string.IsNullOrWhiteSpace(profile.Gstin)
                ? null
                : profile.Gstin.Trim().ToUpperInvariant();

            if (normalizedGstin != null)
            {
                var validation = GstinValidator.Validate(normalizedGstin);
                if (!validation.IsValid)
                    throw new ArgumentException(validation.ErrorMessage);
                normalizedGstin = validation.NormalizedGstin;
            }

            string normalizedStateCode = string.IsNullOrWhiteSpace(profile.StateCode)
                ? null
                : profile.StateCode.Trim();

            if (normalizedStateCode != null && !IndianGstStateCatalog.IsValidShopStateCode(normalizedStateCode))
                throw new ArgumentException("Choose a valid Indian GST State and 2-digit State Code.");

            if (normalizedGstin != null && normalizedStateCode != null && !string.Equals(normalizedGstin.Substring(0, 2), normalizedStateCode, StringComparison.Ordinal))
                throw new ArgumentException("The first two digits of the shop GSTIN must match the selected State Code.");

            var nowUtc = DateTime.UtcNow;
            var now = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            var localText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var before = writeAudit ? ReadProfile(connection, transaction) : null;
                string shopId = !string.IsNullOrWhiteSpace(profile.ShopId)
                    ? profile.ShopId
                    : (before != null && !string.IsNullOrWhiteSpace(before.ShopId) ? before.ShopId : Guid.NewGuid().ToString("N"));

                using (var command = new SQLiteCommand(@"
INSERT INTO shop_profile(profile_id, shop_id, shop_name, legal_name, address, phone, email, state_name, state_code, gstin, drug_licence_number, invoice_prefix, created_at_utc, updated_at_utc)
VALUES(1, @shop_id, @shop_name, @legal_name, @address, @phone, @email, @state_name, @state_code, @gstin, @drug_licence, @invoice_prefix, @created, @updated)
ON CONFLICT(profile_id) DO UPDATE SET shop_name=excluded.shop_name, legal_name=excluded.legal_name, address=excluded.address, phone=excluded.phone, email=excluded.email, state_name=excluded.state_name, state_code=excluded.state_code, gstin=excluded.gstin, drug_licence_number=excluded.drug_licence_number, invoice_prefix=excluded.invoice_prefix, updated_at_utc=excluded.updated_at_utc;", connection, transaction))
                {
                    command.Parameters.AddWithValue("@shop_id", shopId);
                    command.Parameters.AddWithValue("@shop_name", profile.ShopName.Trim());
                    command.Parameters.AddWithValue("@legal_name", Db(profile.LegalName));
                    command.Parameters.AddWithValue("@address", Db(profile.Address));
                    command.Parameters.AddWithValue("@phone", Db(profile.Phone));
                    command.Parameters.AddWithValue("@email", Db(profile.Email));
                    command.Parameters.AddWithValue("@state_name", Db(profile.StateName));
                    command.Parameters.AddWithValue("@state_code", Db(normalizedStateCode));
                    command.Parameters.AddWithValue("@gstin", Db(normalizedGstin));
                    command.Parameters.AddWithValue("@drug_licence", Db(profile.DrugLicenceNumber));
                    command.Parameters.AddWithValue("@invoice_prefix", normalizedPrefix);
                    command.Parameters.AddWithValue("@created", now);
                    command.Parameters.AddWithValue("@updated", now);
                    command.ExecuteNonQuery();
                }

                if (writeAudit)
                {
                    var serializer = new JavaScriptSerializer();
                    string beforeJson = before == null ? null : serializer.Serialize(new Dictionary<string, object>
                    {
                        { "shopName", before.ShopName },
                        { "stateName", before.StateName },
                        { "stateCode", before.StateCode },
                        { "gstin", before.Gstin },
                        { "invoicePrefix", before.InvoicePrefix }
                    });
                    string afterJson = serializer.Serialize(new Dictionary<string, object>
                    {
                        { "shopName", profile.ShopName.Trim() },
                        { "stateName", profile.StateName },
                        { "stateCode", normalizedStateCode },
                        { "gstin", normalizedGstin },
                        { "invoicePrefix", normalizedPrefix }
                    });

                    using (var audit = new SQLiteCommand(@"
INSERT INTO audit_log(audit_id, action_type, entity_type, entity_id, summary, before_json, after_json, created_at_utc, created_local_text)
VALUES(@id, 'SHOP_PROFILE_UPDATED', 'SHOP_PROFILE', @entity, @summary, @before_json, @after_json, @utc, @local);", connection, transaction))
                    {
                        audit.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                        audit.Parameters.AddWithValue("@entity", shopId);
                        audit.Parameters.AddWithValue("@summary", "Updated shop profile and tax registration details.");
                        audit.Parameters.AddWithValue("@before_json", (object)beforeJson ?? DBNull.Value);
                        audit.Parameters.AddWithValue("@after_json", afterJson);
                        audit.Parameters.AddWithValue("@utc", now);
                        audit.Parameters.AddWithValue("@local", localText);
                        audit.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        private static ShopProfile ReadProfile(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            using (var command = new SQLiteCommand("SELECT shop_id, shop_name, legal_name, address, phone, email, state_name, state_code, gstin, drug_licence_number, invoice_prefix, financial_year_start_month FROM shop_profile WHERE profile_id = 1;", connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) return null;
                return new ShopProfile
                {
                    ShopId = reader.GetString(0),
                    ShopName = reader.GetString(1),
                    LegalName = ReadNullable(reader, 2),
                    Address = ReadNullable(reader, 3),
                    Phone = ReadNullable(reader, 4),
                    Email = ReadNullable(reader, 5),
                    StateName = ReadNullable(reader, 6),
                    StateCode = ReadNullable(reader, 7),
                    Gstin = ReadNullable(reader, 8),
                    DrugLicenceNumber = ReadNullable(reader, 9),
                    InvoicePrefix = reader.GetString(10),
                    FinancialYearStartMonth = reader.IsDBNull(11) ? 4 : reader.GetInt32(11)
                };
            }
        }

        private static object Db(string value) { return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim(); }
        private static string ReadNullable(SQLiteDataReader reader, int ordinal) { return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal); }
    }
}