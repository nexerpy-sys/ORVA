using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Orva.Infrastructure;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class ShopBackupManifest
    {
        public string BackupFormat { get; set; }
        public int FormatVersion { get; set; }
        public string ApplicationName { get; set; }
        public string ApplicationVersion { get; set; }
        public int DatabaseSchemaVersion { get; set; }
        public string ShopId { get; set; }
        public string ShopName { get; set; }
        public string BackupUtc { get; set; }
        public string BackupLocalText { get; set; }
        public string DatabaseFileName { get; set; }
        public long DatabaseLengthBytes { get; set; }
        public string DatabaseSha256 { get; set; }
        public long MedicineCount { get; set; }
        public long BatchCount { get; set; }
        public long InvoiceCount { get; set; }
        public long PurchaseCount { get; set; }
        public long ReturnCount { get; set; }
    }

    public sealed class ShopBackupPreview
    {
        public ShopBackupPreview()
        {
            Errors = new List<string>();
        }

        public bool IsValid { get; set; }
        public string BackupFilePath { get; set; }
        public ShopBackupManifest Manifest { get; set; }
        public string TargetShopId { get; set; }
        public string TargetShopName { get; set; }
        public bool IsDifferentShop { get; set; }
        public List<string> Errors { get; private set; }
    }

    public sealed class ShopBackupCreateResult
    {
        public bool Succeeded { get; set; }
        public string BackupFilePath { get; set; }
        public string PortableShopCopyPath { get; set; }
        public ShopBackupManifest Manifest { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class ShopRestoreResult
    {
        public bool Succeeded { get; set; }
        public string RestoredShopName { get; set; }
        public string SafetyBackupPath { get; set; }
        public bool SnapshotRefreshed { get; set; }
        public string SnapshotWarning { get; set; }
        public string ErrorMessage { get; set; }
        public bool OriginalDatabaseRecovered { get; set; }
    }

    /// <summary>
    /// Complete unencrypted single-file shop backup (.shopbackup) and safe restore service.
    /// Uses SQLite online backup API (never raw File.Copy on a live WAL database),
    /// SHA-256 corruption detection (not encryption), integrity/FK validation, and
    /// pre-restore safety backup with automatic rollback on failure.
    /// </summary>
    public sealed class ShopBackupService
    {
        public const string ExpectedBackupFormat = "ORVA_SHOP_BACKUP";
        public const int CurrentBackupFormatVersion = 1;
        public const int MaxSupportedSchemaVersion = DatabaseMigrator.CurrentSchemaVersion;
        public const string ManifestEntryName = "manifest.json";
        public const string DatabaseEntryName = "orva.sqlite";
        private const long MaxAllowedDatabaseBytes = 512L * 1024L * 1024L; // 512 MB safety ceiling
        private const long MaxAllowedManifestBytes = 1024L * 1024L;       // 1 MB safety ceiling

        private readonly SqliteDatabase _database;
        private readonly ShopRepository _shopRepository;
        private readonly StockSnapshotWriter _snapshotWriter;
        private readonly string _backupsRootDirectory;
        private readonly Func<string, string> _shopFolderResolver;
        private readonly object _maintenanceLock = new object();

        public ShopBackupService(
            SqliteDatabase database,
            ShopRepository shopRepository,
            StockSnapshotWriter snapshotWriter)
            : this(database, shopRepository, snapshotWriter, AppPaths.BackupsDirectory, null)
        {
        }

        internal ShopBackupService(
            SqliteDatabase database,
            ShopRepository shopRepository,
            StockSnapshotWriter snapshotWriter,
            string backupsRootDirectory,
            Func<string, string> shopFolderResolver)
        {
            if (database == null) throw new ArgumentNullException("database");
            if (shopRepository == null) throw new ArgumentNullException("shopRepository");
            _database = database;
            _shopRepository = shopRepository;
            _snapshotWriter = snapshotWriter;
            _backupsRootDirectory = string.IsNullOrWhiteSpace(backupsRootDirectory) ? AppPaths.BackupsDirectory : backupsRootDirectory;
            _shopFolderResolver = shopFolderResolver;
        }

        public string BackupsRootDirectory { get { return _backupsRootDirectory; } }

        public ShopBackupCreateResult CreateBackupToPath(string targetFilePath, bool alsoUpdatePortableShopCopy = false)
        {
            if (string.IsNullOrWhiteSpace(targetFilePath))
                throw new ArgumentException("A destination backup path is required.", "targetFilePath");

            lock (_maintenanceLock)
            {
                string stagingDir = Path.Combine(Path.GetTempPath(), "orva_backup_stage_" + Guid.NewGuid().ToString("N"));
                string tempArchivePath = targetFilePath + ".tmp";

                try
                {
                    Directory.CreateDirectory(stagingDir);
                    string targetDir = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                    string stagedDbPath = Path.Combine(stagingDir, DatabaseEntryName);
                    PerformOnlineSqliteBackup(_database.DatabasePath, stagedDbPath);

                    int schemaVersion;
                    string shopId;
                    string shopName;
                    long medCount, batchCount, invCount, purCount, retCount;
                    ValidateSqliteBackupFile(
                        stagedDbPath,
                        out schemaVersion,
                        out shopId,
                        out shopName,
                        out medCount,
                        out batchCount,
                        out invCount,
                        out purCount,
                        out retCount);

                    string sha256 = ComputeFileSha256(stagedDbPath);
                    long dbLength = new FileInfo(stagedDbPath).Length;
                    var nowUtc = DateTime.UtcNow;

                    var manifest = new ShopBackupManifest
                    {
                        BackupFormat = ExpectedBackupFormat,
                        FormatVersion = CurrentBackupFormatVersion,
                        ApplicationName = "ORVA",
                        ApplicationVersion = "1.0.0",
                        DatabaseSchemaVersion = schemaVersion,
                        ShopId = shopId,
                        ShopName = shopName,
                        BackupUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture),
                        BackupLocalText = nowUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        DatabaseFileName = DatabaseEntryName,
                        DatabaseLengthBytes = dbLength,
                        DatabaseSha256 = sha256,
                        MedicineCount = medCount,
                        BatchCount = batchCount,
                        InvoiceCount = invCount,
                        PurchaseCount = purCount,
                        ReturnCount = retCount
                    };

                    var serializer = new JavaScriptSerializer();
                    string manifestJson = serializer.Serialize(manifest);

                    if (File.Exists(tempArchivePath)) File.Delete(tempArchivePath);
                    using (var archive = ZipFile.Open(tempArchivePath, ZipArchiveMode.Create))
                    {
                        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                        using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                        {
                            writer.Write(manifestJson);
                        }

                        archive.CreateEntryFromFile(stagedDbPath, DatabaseEntryName, CompressionLevel.Optimal);
                    }

                    // Verify the completed package before replacing any existing backup.
                    var verify = ValidateArchivePackage(tempArchivePath);
                    if (!verify.IsValid)
                    {
                        throw new InvalidDataException("Generated backup failed post-write verification: " + string.Join("; ", verify.Errors));
                    }

                    AtomicReplaceFile(tempArchivePath, targetFilePath);

                    string portableCopyPath = null;
                    if (alsoUpdatePortableShopCopy && !string.IsNullOrWhiteSpace(shopName))
                    {
                        string shopFolder = ResolveShopFolder(shopName);
                        Directory.CreateDirectory(shopFolder);
                        portableCopyPath = Path.Combine(shopFolder, "ShopBackup.shopbackup");
                        if (!string.Equals(Path.GetFullPath(portableCopyPath), Path.GetFullPath(targetFilePath), StringComparison.OrdinalIgnoreCase))
                        {
                            string portableTemp = portableCopyPath + ".tmp";
                            File.Copy(targetFilePath, portableTemp, true);
                            AtomicReplaceFile(portableTemp, portableCopyPath);
                        }
                    }

                    return new ShopBackupCreateResult
                    {
                        Succeeded = true,
                        BackupFilePath = targetFilePath,
                        PortableShopCopyPath = portableCopyPath,
                        Manifest = manifest
                    };
                }
                catch (Exception ex)
                {
                    return new ShopBackupCreateResult
                    {
                        Succeeded = false,
                        ErrorMessage = "Backup could not be completed. Your current shop data is still saved. (" + ex.Message + ")"
                    };
                }
                finally
                {
                    SafeDeleteFile(tempArchivePath);
                    SafeDeleteDirectory(stagingDir);
                }
            }
        }

        public string CreateSafetyBackup(string reasonTag)
        {
            string safeTag = string.IsNullOrWhiteSpace(reasonTag) ? "safety" : AppPaths.MakeSafeFolderName(reasonTag.Trim());
            string safetyDir = Path.Combine(_backupsRootDirectory, "Safety");
            Directory.CreateDirectory(safetyDir);
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "ORVA_Safety_{0}_{1}.shopbackup",
                safeTag,
                DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            string path = Path.Combine(safetyDir, fileName);

            var res = CreateBackupToPath(path, alsoUpdatePortableShopCopy: false);
            if (!res.Succeeded)
            {
                throw new InvalidOperationException("Could not create pre-operation safety backup: " + res.ErrorMessage);
            }
            return res.BackupFilePath;
        }

        public ShopBackupPreview PreviewRestore(string backupFilePath)
        {
            var preview = ValidateArchivePackage(backupFilePath);
            if (preview.IsValid)
            {
                var target = _shopRepository.GetProfile();
                preview.TargetShopId = target != null ? target.ShopId : null;
                preview.TargetShopName = target != null ? target.ShopName : null;
                preview.IsDifferentShop = target != null
                    && preview.Manifest != null
                    && !string.Equals(target.ShopId, preview.Manifest.ShopId, StringComparison.Ordinal);
            }
            return preview;
        }

        public ShopRestoreResult ExecuteRestore(string backupFilePath)
        {
            return ExecuteRestore(backupFilePath, null);
        }

        internal ShopRestoreResult ExecuteRestore(string backupFilePath, Action<string> failureInjectionHook)
        {
            lock (_maintenanceLock)
            {
                string stagingDir = Path.Combine(Path.GetTempPath(), "orva_restore_stage_" + Guid.NewGuid().ToString("N"));
                string rollbackPath = _database.DatabasePath + ".pre_restore_rollback";
                string safetyBackupPath = null;
                bool replacementStarted = false;

                try
                {
                    var preview = ValidateArchivePackage(backupFilePath);
                    if (!preview.IsValid)
                    {
                        return new ShopRestoreResult
                        {
                            Succeeded = false,
                            ErrorMessage = preview.Errors.Count > 0 ? preview.Errors[0] : "Selected backup file is not valid.",
                            OriginalDatabaseRecovered = true
                        };
                    }

                    Directory.CreateDirectory(stagingDir);
                    string stagedDbPath = Path.Combine(stagingDir, DatabaseEntryName);

                    // Extract verified database entry to fixed path (never trusting archive filenames)
                    using (var archive = ZipFile.OpenRead(backupFilePath))
                    {
                        var dbEntry = archive.GetEntry(DatabaseEntryName);
                        if (dbEntry == null)
                            throw new InvalidDataException("Backup archive is missing orva.sqlite.");
                        if (dbEntry.Length <= 0 || dbEntry.Length > MaxAllowedDatabaseBytes)
                            throw new InvalidDataException("Backup database size is outside allowed limits.");

                        dbEntry.ExtractToFile(stagedDbPath, true);
                    }

                    string actualSha = ComputeFileSha256(stagedDbPath);
                    if (!string.Equals(actualSha, preview.Manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Backup database checksum mismatch. The file may be corrupted.");

                    int schemaVersion;
                    string shopId, shopName;
                    long medCount, batchCount, invCount, purCount, retCount;
                    ValidateSqliteBackupFile(
                        stagedDbPath,
                        out schemaVersion,
                        out shopId,
                        out shopName,
                        out medCount,
                        out batchCount,
                        out invCount,
                        out purCount,
                        out retCount);

                    if (failureInjectionHook != null) failureInjectionHook("after_staging_validation");

                    // Create permanent safety backup of current database if one exists
                    if (File.Exists(_database.DatabasePath))
                    {
                        safetyBackupPath = CreateSafetyBackup("pre-restore");
                    }

                    if (failureInjectionHook != null) failureInjectionHook("after_safety_backup");

                    // Release all pooled SQLite connections before replacing the live database file
                    SQLiteConnection.ClearAllPools();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    replacementStarted = true;

                    if (File.Exists(rollbackPath)) File.Delete(rollbackPath);
                    if (File.Exists(_database.DatabasePath))
                    {
                        // Create a verified consistent rollback snapshot before touching live file
                        PerformOnlineSqliteBackup(_database.DatabasePath, rollbackPath);
                        SQLiteConnection.ClearAllPools();
                    }

                    if (failureInjectionHook != null) failureInjectionHook("before_live_replace");

                    // Remove stale WAL/SHM sidecars so SQLite never replays old WAL frames over the restored DB
                    SafeDeleteFile(_database.DatabasePath + "-wal");
                    SafeDeleteFile(_database.DatabasePath + "-shm");

                    File.Copy(stagedDbPath, _database.DatabasePath, true);

                    if (failureInjectionHook != null) failureInjectionHook("after_live_replace");

                    // Reopen, run forward migrations if backup came from an older schema, and verify integrity
                    new DatabaseMigrator(_database).Migrate();

                    int postSchema;
                    string postShopId, postShopName;
                    long pm, pb, pi, pp, pr;
                    ValidateSqliteBackupFile(
                        _database.DatabasePath,
                        out postSchema,
                        out postShopId,
                        out postShopName,
                        out pm,
                        out pb,
                        out pi,
                        out pp,
                        out pr);

                    if (failureInjectionHook != null) failureInjectionHook("after_post_restore_verify");

                    // Cleanup temporary rollback copy ONLY after verified success (permanent safety backup is preserved)
                    SafeDeleteFile(rollbackPath);

                    var result = new ShopRestoreResult
                    {
                        Succeeded = true,
                        RestoredShopName = postShopName,
                        SafetyBackupPath = safetyBackupPath,
                        OriginalDatabaseRecovered = false
                    };

                    if (_snapshotWriter != null)
                    {
                        try
                        {
                            _snapshotWriter.WriteCurrentSnapshot();
                            result.SnapshotRefreshed = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.TraceError("Snapshot refresh failed after restore: " + ex);
                            result.SnapshotRefreshed = false;
                            result.SnapshotWarning = "Shop database was restored safely, but StockData.json could not be refreshed yet.";
                        }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    bool recovered = true;
                    if (replacementStarted && File.Exists(rollbackPath))
                    {
                        recovered = TryRollbackOriginalDatabase(rollbackPath);
                    }

                    return new ShopRestoreResult
                    {
                        Succeeded = false,
                        SafetyBackupPath = safetyBackupPath,
                        OriginalDatabaseRecovered = recovered,
                        ErrorMessage = recovered
                            ? "Restore could not be completed, so your previous shop data was safely restored. (" + ex.Message + ")"
                            : "Restore failed and automatic rollback encountered an error. Your pre-restore safety backup is preserved at: " + (safetyBackupPath ?? "Backups folder")
                    };
                }
                finally
                {
                    SafeDeleteDirectory(stagingDir);
                }
            }
        }

        private bool TryRollbackOriginalDatabase(string rollbackPath)
        {
            try
            {
                SQLiteConnection.ClearAllPools();
                SafeDeleteFile(_database.DatabasePath + "-wal");
                SafeDeleteFile(_database.DatabasePath + "-shm");
                File.Copy(rollbackPath, _database.DatabasePath, true);
                SafeDeleteFile(rollbackPath);

                using (var conn = _database.OpenConnection())
                using (var cmd = new SQLiteCommand("PRAGMA integrity_check;", conn))
                {
                    string check = Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    return string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private ShopBackupPreview ValidateArchivePackage(string backupFilePath)
        {
            var preview = new ShopBackupPreview { BackupFilePath = backupFilePath };
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                preview.Errors.Add("The selected backup file does not exist.");
                return preview;
            }

            try
            {
                using (var archive = ZipFile.OpenRead(backupFilePath))
                {
                    ZipArchiveEntry manifestEntry = null;
                    ZipArchiveEntry dbEntry = null;

                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        var entry = archive.Entries[i];
                        string fullName = entry.FullName ?? string.Empty;

                        // Reject path traversal or nested directory entries
                        if (fullName.Contains("..") || fullName.Contains("/") || fullName.Contains("\\") || Path.IsPathRooted(fullName))
                        {
                            preview.Errors.Add("Backup archive contains an unsafe entry path and was rejected.");
                            return preview;
                        }

                        if (string.Equals(fullName, ManifestEntryName, StringComparison.Ordinal))
                            manifestEntry = entry;
                        else if (string.Equals(fullName, DatabaseEntryName, StringComparison.Ordinal))
                            dbEntry = entry;
                    }

                    if (manifestEntry == null)
                    {
                        preview.Errors.Add("Backup archive is missing manifest.json.");
                        return preview;
                    }
                    if (dbEntry == null)
                    {
                        preview.Errors.Add("Backup archive is missing orva.sqlite.");
                        return preview;
                    }
                    if (manifestEntry.Length <= 0 || manifestEntry.Length > MaxAllowedManifestBytes)
                    {
                        preview.Errors.Add("Backup manifest size is invalid.");
                        return preview;
                    }
                    if (dbEntry.Length <= 0 || dbEntry.Length > MaxAllowedDatabaseBytes)
                    {
                        preview.Errors.Add("Backup database size is invalid.");
                        return preview;
                    }

                    string manifestJson;
                    using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                    {
                        manifestJson = reader.ReadToEnd();
                    }

                    var serializer = new JavaScriptSerializer();
                    var manifest = serializer.Deserialize<ShopBackupManifest>(manifestJson);
                    if (manifest == null || !string.Equals(manifest.BackupFormat, ExpectedBackupFormat, StringComparison.Ordinal))
                    {
                        preview.Errors.Add("This file is not a valid ORVA shop backup.");
                        return preview;
                    }
                    if (manifest.FormatVersion != CurrentBackupFormatVersion)
                    {
                        preview.Errors.Add("Unsupported backup format version (" + manifest.FormatVersion + ").");
                        return preview;
                    }
                    if (manifest.DatabaseSchemaVersion < 1 || manifest.DatabaseSchemaVersion > MaxSupportedSchemaVersion)
                    {
                        preview.Errors.Add("This backup was created by a newer ORVA database version (schema v" + manifest.DatabaseSchemaVersion + "). Update ORVA before restoring.");
                        return preview;
                    }
                    if (string.IsNullOrWhiteSpace(manifest.DatabaseSha256))
                    {
                        preview.Errors.Add("Backup manifest is missing its integrity checksum.");
                        return preview;
                    }
                    if (dbEntry.Length != manifest.DatabaseLengthBytes)
                    {
                        preview.Errors.Add("Backup database size does not match the manifest.");
                        return preview;
                    }

                    // Stream-hash the database entry directly to verify SHA-256 without loading entire DB in RAM
                    string computedHash;
                    using (var sha = SHA256.Create())
                    using (var stream = dbEntry.Open())
                    {
                        byte[] hashBytes = sha.ComputeHash(stream);
                        computedHash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
                    }

                    if (!string.Equals(computedHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        preview.Errors.Add("Backup integrity checksum mismatch. The file appears corrupted.");
                        return preview;
                    }

                    preview.Manifest = manifest;
                    preview.IsValid = true;
                }
            }
            catch (InvalidDataException)
            {
                preview.Errors.Add("The selected file is corrupted or not a valid .shopbackup archive.");
            }
            catch (Exception ex)
            {
                preview.Errors.Add("Could not validate backup file: " + ex.Message);
            }

            return preview;
        }

        private static void PerformOnlineSqliteBackup(string sourceDbPath, string destinationDbPath)
        {
            if (!File.Exists(sourceDbPath))
                throw new FileNotFoundException("Source SQLite database does not exist.", sourceDbPath);

            var srcBuilder = new SQLiteConnectionStringBuilder { DataSource = sourceDbPath, Version = 3, Pooling = false };
            var dstBuilder = new SQLiteConnectionStringBuilder { DataSource = destinationDbPath, Version = 3, Pooling = false };

            using (var source = new SQLiteConnection(srcBuilder.ConnectionString))
            using (var destination = new SQLiteConnection(dstBuilder.ConnectionString))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination, "main", "main", -1, null, 0);
            }
        }

        private static void ValidateSqliteBackupFile(
            string dbPath,
            out int schemaVersion,
            out string shopId,
            out string shopName,
            out long medicineCount,
            out long batchCount,
            out long invoiceCount,
            out long purchaseCount,
            out long returnCount)
        {
            var builder = new SQLiteConnectionStringBuilder { DataSource = dbPath, Version = 3, Pooling = false };
            using (var conn = new SQLiteConnection(builder.ConnectionString))
            {
                conn.Open();

                using (var ic = new SQLiteCommand("PRAGMA integrity_check;", conn))
                {
                    string res = Convert.ToString(ic.ExecuteScalar(), CultureInfo.InvariantCulture);
                    if (!string.Equals(res, "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SQLite integrity check failed: " + res);
                }

                using (var fk = new SQLiteCommand("PRAGMA foreign_key_check;", conn))
                using (var reader = fk.ExecuteReader())
                {
                    if (reader.Read())
                        throw new InvalidDataException("SQLite foreign-key integrity check failed.");
                }

                using (var uv = new SQLiteCommand("PRAGMA user_version;", conn))
                {
                    schemaVersion = Convert.ToInt32(uv.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                if (schemaVersion < 1 || schemaVersion > MaxSupportedSchemaVersion)
                    throw new InvalidDataException("Unsupported database schema version: " + schemaVersion);

                shopId = null;
                shopName = null;
                using (var shopCmd = new SQLiteCommand("SELECT shop_id, shop_name FROM shop_profile WHERE profile_id = 1;", conn))
                using (var reader = shopCmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        shopId = reader.GetString(0);
                        shopName = reader.GetString(1);
                    }
                }

                medicineCount = ScalarInt64(conn, "SELECT COUNT(*) FROM medicine;");
                batchCount = ScalarInt64(conn, "SELECT COUNT(*) FROM stock_batch;");
                invoiceCount = ScalarInt64(conn, "SELECT COUNT(*) FROM invoice;");
                purchaseCount = ScalarInt64(conn, "SELECT COUNT(*) FROM purchase_receipt;");
                returnCount = ScalarInt64(conn, "SELECT COUNT(*) FROM sales_return;");
            }
        }

        private static long ScalarInt64(SQLiteConnection conn, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static string ComputeFileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void AtomicReplaceFile(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                string previousPath = targetPath + ".previous";
                if (File.Exists(previousPath)) File.Delete(previousPath);
                File.Replace(tempPath, targetPath, previousPath, true);
                SafeDeleteFile(previousPath);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }

        private string ResolveShopFolder(string shopName)
        {
            return _shopFolderResolver != null ? _shopFolderResolver(shopName) : AppPaths.GetShopFolder(shopName);
        }

        private static void SafeDeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
