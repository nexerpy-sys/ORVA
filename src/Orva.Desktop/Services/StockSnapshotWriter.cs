using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Orva.Domain;
using Orva.Infrastructure;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class StockSnapshotWriter
    {
        public const string ExpectedFormat = "ORVA_STOCK_DATA";
        public const int CurrentFormatVersion = 2;

        private readonly SqliteDatabase _database;
        private readonly ShopRepository _shopRepository;
        private readonly object _writeLock = new object();
        private readonly Func<string, string> _folderResolver;
        private readonly Action _beforeMarkCleanHook;

        public StockSnapshotWriter(SqliteDatabase database, ShopRepository shopRepository)
            : this(database, shopRepository, null, null)
        {
        }

        internal StockSnapshotWriter(
            SqliteDatabase database,
            ShopRepository shopRepository,
            Func<string, string> folderResolver,
            Action beforeMarkCleanHook)
        {
            if (database == null) throw new ArgumentNullException("database");
            if (shopRepository == null) throw new ArgumentNullException("shopRepository");
            _database = database;
            _shopRepository = shopRepository;
            _folderResolver = folderResolver;
            _beforeMarkCleanHook = beforeMarkCleanHook;
        }

        public string GetSnapshotPath()
        {
            var shop = _shopRepository.GetProfile();
            if (shop == null) throw new InvalidOperationException("Shop setup must be completed before creating the stock file.");
            return Path.Combine(ResolveShopFolder(shop.ShopName), "StockData.json");
        }

        /// <summary>
        /// Writes StockData.json (Format Version 2) atomically and clears stock_snapshot_dirty ONLY if
        /// stock_snapshot_generation has not advanced since the snapshot was read.
        /// Returns true if the snapshot is clean after writing; false if a newer stock transaction
        /// committed while the file was being written (dirty remains 1).
        /// </summary>
        public bool WriteCurrentSnapshot()
        {
            lock (_writeLock)
            {
                var shop = _shopRepository.GetProfile();
                if (shop == null) throw new InvalidOperationException("Shop setup must be completed before creating the stock file.");

                var folder = ResolveShopFolder(shop.ShopName);
                Directory.CreateDirectory(folder);
                var targetPath = Path.Combine(folder, "StockData.json");
                var tempPath = targetPath + ".tmp";
                var previousPath = targetPath + ".previous";

                long capturedGeneration;
                var snapshot = ReadSnapshotConsistent(shop, out capturedGeneration);

                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
                var json = serializer.Serialize(snapshot);
                var bytes = new UTF8Encoding(false).GetBytes(json);

                try
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }

                    // Parse and validate the exact file written before replacing the previous good snapshot.
                    var check = File.ReadAllText(tempPath, Encoding.UTF8);
                    var validated = serializer.Deserialize<StockSnapshot>(check);
                    if (validated == null
                        || !string.Equals(validated.Format, ExpectedFormat, StringComparison.Ordinal)
                        || validated.FormatVersion != CurrentFormatVersion
                        || !string.Equals(validated.ShopId, shop.ShopId, StringComparison.Ordinal)
                        || validated.SnapshotGeneration != capturedGeneration)
                    {
                        throw new InvalidDataException("The generated stock file did not pass validation.");
                    }

                    if (File.Exists(targetPath))
                    {
                        if (File.Exists(previousPath)) File.Delete(previousPath);
                        File.Replace(tempPath, targetPath, previousPath, true);
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }

                    if (_beforeMarkCleanHook != null)
                    {
                        _beforeMarkCleanHook();
                    }

                    return TryMarkCleanIfGenerationMatches(capturedGeneration);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* Preserve the original exception/result. */ }
                    }
                }
            }
        }

        public void MarkDirty()
        {
            var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                StockSnapshotState.MarkDirtyAndIncrementGeneration(connection, transaction, now);
                transaction.Commit();
            }
        }

        public bool IsDirty()
        {
            using (var connection = _database.OpenConnection())
            using (var command = new SQLiteCommand(
                "SELECT setting_value FROM app_setting WHERE setting_key = @key;",
                connection))
            {
                command.Parameters.AddWithValue("@key", StockSnapshotState.DirtyKey);
                var value = command.ExecuteScalar() as string;
                return string.Equals(value, "1", StringComparison.Ordinal);
            }
        }

        public long GetCurrentGeneration()
        {
            using (var connection = _database.OpenConnection())
            {
                return StockSnapshotState.ReadGeneration(connection, null);
            }
        }

        private bool TryMarkCleanIfGenerationMatches(long capturedGeneration)
        {
            var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                long currentGeneration = StockSnapshotState.ReadGeneration(connection, transaction);
                if (currentGeneration != capturedGeneration)
                {
                    StockSnapshotState.UpsertSetting(connection, transaction, StockSnapshotState.DirtyKey, "1", now);
                    transaction.Commit();
                    return false;
                }

                StockSnapshotState.UpsertSetting(connection, transaction, StockSnapshotState.DirtyKey, "0", now);
                StockSnapshotState.UpsertSetting(connection, transaction, StockSnapshotState.LastWrittenUtcKey, now, now);
                transaction.Commit();
                return true;
            }
        }

        private StockSnapshot ReadSnapshotConsistent(ShopProfile shop, out long capturedGeneration)
        {
            var snapshot = new StockSnapshot
            {
                Format = ExpectedFormat,
                FormatVersion = CurrentFormatVersion,
                ShopId = shop.ShopId,
                ShopName = shop.ShopName,
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ApplicationVersion = "1.0.0",
                Medicines = new List<StockSnapshotMedicine>()
            };

            var medicines = new Dictionary<string, StockSnapshotMedicine>(StringComparer.Ordinal);
            using (var connection = _database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                capturedGeneration = StockSnapshotState.ReadGeneration(connection, transaction);
                snapshot.SnapshotGeneration = capturedGeneration;

                using (var command = new SQLiteCommand(@"
SELECT m.medicine_id, m.display_name, m.normalized_name, m.generic_name, m.brand_name,
       m.strength, m.dosage_form, m.stock_unit_name, m.hsn_code, m.gst_rate_basis_points,
       m.gst_rate_configured, m.low_stock_threshold_milli, m.is_active,
       b.batch_id, b.batch_number, b.expiry_yyyymm, b.mrp_paise, b.latest_purchase_price_paise,
       b.quantity_milli, b.batch_status, b.updated_at_utc
FROM medicine m
LEFT JOIN stock_batch b ON b.medicine_id = m.medicine_id
WHERE m.is_active = 1
ORDER BY m.display_name COLLATE NOCASE, b.expiry_yyyymm, b.batch_number;", connection, transaction))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var medicineId = reader.GetString(0);
                        StockSnapshotMedicine medicine;
                        if (!medicines.TryGetValue(medicineId, out medicine))
                        {
                            medicine = new StockSnapshotMedicine
                            {
                                MedicineId = medicineId,
                                Name = reader.GetString(1),
                                NormalizedName = reader.GetString(2),
                                GenericName = NullableString(reader, 3),
                                BrandName = NullableString(reader, 4),
                                Strength = NullableString(reader, 5),
                                DosageForm = NullableString(reader, 6),
                                StockUnit = reader.GetString(7),
                                HsnCode = NullableString(reader, 8),
                                GstRateBasisPoints = reader.GetInt32(9),
                                GstRateConfigured = reader.GetInt32(10) == 1,
                                LowStockThresholdMilli = reader.GetInt64(11),
                                IsActive = reader.GetInt32(12) == 1,
                                Barcodes = new List<string>(),
                                Batches = new List<StockSnapshotBatch>()
                            };
                            medicines.Add(medicineId, medicine);
                            snapshot.Medicines.Add(medicine);
                        }

                        if (!reader.IsDBNull(13))
                        {
                            medicine.Batches.Add(new StockSnapshotBatch
                            {
                                BatchId = reader.GetString(13),
                                MedicineId = medicineId,
                                BatchNumber = reader.GetString(14),
                                ExpiryYearMonth = reader.GetInt32(15),
                                MrpPaise = reader.GetInt64(16),
                                LatestPurchasePricePaise = reader.GetInt64(17),
                                QuantityMilli = reader.GetInt64(18),
                                Status = reader.GetString(19),
                                UpdatedAtUtc = reader.GetString(20)
                            });
                        }
                    }
                }

                // Include internal barcode mappings for active medicines
                using (var barcodeCmd = new SQLiteCommand(
                    "SELECT medicine_id, barcode FROM barcode_mapping ORDER BY barcode ASC;",
                    connection,
                    transaction))
                using (var reader = barcodeCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string medId = reader.GetString(0);
                        string barcode = reader.GetString(1);
                        StockSnapshotMedicine med;
                        if (medicines.TryGetValue(medId, out med))
                        {
                            med.Barcodes.Add(barcode);
                        }
                    }
                }

                transaction.Commit();
            }
            return snapshot;
        }

        private string ResolveShopFolder(string shopName)
        {
            return _folderResolver != null ? _folderResolver(shopName) : AppPaths.GetShopFolder(shopName);
        }

        private static string NullableString(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }

    public sealed class StockSnapshot
    {
        public string Format { get; set; }
        public int FormatVersion { get; set; }
        public string ShopId { get; set; }
        public string ShopName { get; set; }
        public string GeneratedAtUtc { get; set; }
        public string ApplicationVersion { get; set; }
        public long SnapshotGeneration { get; set; }
        public List<StockSnapshotMedicine> Medicines { get; set; }
    }

    public sealed class StockSnapshotMedicine
    {
        public string MedicineId { get; set; }
        public string Name { get; set; }
        public string NormalizedName { get; set; }
        public string GenericName { get; set; }
        public string BrandName { get; set; }
        public string Strength { get; set; }
        public string DosageForm { get; set; }
        public string StockUnit { get; set; }
        public string HsnCode { get; set; }
        public int GstRateBasisPoints { get; set; }
        public bool? GstRateConfigured { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public bool? IsActive { get; set; }
        public List<string> Barcodes { get; set; }
        public List<StockSnapshotBatch> Batches { get; set; }
    }

    public sealed class StockSnapshotBatch
    {
        public string BatchId { get; set; }
        public string MedicineId { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYearMonth { get; set; }
        public long MrpPaise { get; set; }
        public long LatestPurchasePricePaise { get; set; }
        public long QuantityMilli { get; set; }
        public string Status { get; set; }
        public string UpdatedAtUtc { get; set; }
    }
}