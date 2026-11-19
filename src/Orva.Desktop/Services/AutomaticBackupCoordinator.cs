using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class BackupStatusSummary
    {
        public string LastSuccessUtc { get; set; }
        public string LastSuccessLocalDate { get; set; }
        public string LastBackupPath { get; set; }
        public string LastError { get; set; }
        public int RetentionDays { get; set; }
        public string DailyBackupDirectory { get; set; }
        public bool IsDueToday { get; set; }
    }

    /// <summary>
    /// Coordinates automatic daily local backups and configurable retention.
    /// Single-flight guarded; never deletes the last valid backup if a new backup fails.
    /// </summary>
    public sealed class AutomaticBackupCoordinator
    {
        public const string LastSuccessUtcKey = "backup.last_success_utc";
        public const string LastSuccessLocalDateKey = "backup.last_success_local_date";
        public const string LastPathKey = "backup.last_path";
        public const string LastErrorKey = "backup.last_error";
        public const string RetentionDaysKey = "backup.retention_days";
        public const int DefaultRetentionDays = 7;

        private readonly ShopBackupService _backupService;
        private readonly AppSettingsRepository _settings;
        private int _runningFlag;

        public AutomaticBackupCoordinator(ShopBackupService backupService, AppSettingsRepository settings)
        {
            if (backupService == null) throw new ArgumentNullException("backupService");
            if (settings == null) throw new ArgumentNullException("settings");
            _backupService = backupService;
            _settings = settings;
        }

        public string DailyBackupDirectory
        {
            get { return Path.Combine(_backupService.BackupsRootDirectory, "Daily"); }
        }

        public int GetRetentionDays()
        {
            string raw = _settings.Get(RetentionDaysKey, DefaultRetentionDays.ToString(CultureInfo.InvariantCulture));
            int parsed;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < 1)
                return DefaultRetentionDays;
            return parsed;
        }

        public void SetRetentionDays(int days)
        {
            if (days < 1 || days > 365)
                throw new ArgumentOutOfRangeException("days", "Retention count must be between 1 and 365 daily backups.");
            _settings.Set(RetentionDaysKey, days.ToString(CultureInfo.InvariantCulture));
        }

        public BackupStatusSummary GetStatus(DateTime? localNow = null)
        {
            DateTime today = (localNow ?? DateTime.Now).Date;
            string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string lastDate = _settings.Get(LastSuccessLocalDateKey, string.Empty);

            return new BackupStatusSummary
            {
                LastSuccessUtc = _settings.Get(LastSuccessUtcKey, string.Empty),
                LastSuccessLocalDate = lastDate,
                LastBackupPath = _settings.Get(LastPathKey, string.Empty),
                LastError = _settings.Get(LastErrorKey, string.Empty),
                RetentionDays = GetRetentionDays(),
                DailyBackupDirectory = DailyBackupDirectory,
                IsDueToday = !string.Equals(lastDate, todayKey, StringComparison.Ordinal)
            };
        }

        public ShopBackupCreateResult RunDailyBackupIfDue(DateTime? localNow = null)
        {
            var status = GetStatus(localNow);
            if (!status.IsDueToday)
            {
                return new ShopBackupCreateResult
                {
                    Succeeded = true,
                    BackupFilePath = status.LastBackupPath
                };
            }

            return RunBackupNow(localNow);
        }

        public ShopBackupCreateResult RunBackupNow(DateTime? localNow = null)
        {
            if (Interlocked.CompareExchange(ref _runningFlag, 1, 0) != 0)
            {
                return new ShopBackupCreateResult
                {
                    Succeeded = false,
                    ErrorMessage = "A backup is already in progress."
                };
            }

            try
            {
                DateTime nowLocal = localNow ?? DateTime.Now;
                string todayKey = nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Directory.CreateDirectory(DailyBackupDirectory);

                string fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "ORVA_Daily_{0}.shopbackup",
                    nowLocal.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                string targetPath = Path.Combine(DailyBackupDirectory, fileName);

                var result = _backupService.CreateBackupToPath(targetPath, alsoUpdatePortableShopCopy: true);
                if (!result.Succeeded)
                {
                    _settings.Set(LastErrorKey, result.ErrorMessage ?? "Backup failed.");
                    // Never delete older backups when a new backup fails.
                    return result;
                }

                _settings.Set(LastSuccessUtcKey, result.Manifest != null ? result.Manifest.BackupUtc : DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                _settings.Set(LastSuccessLocalDateKey, todayKey);
                _settings.Set(LastPathKey, result.BackupFilePath);
                _settings.Set(LastErrorKey, string.Empty);

                PruneOldDailyBackups(GetRetentionDays());
                return result;
            }
            catch (Exception ex)
            {
                string msg = "Backup could not be completed. Your current shop data is still saved.";
                try { _settings.Set(LastErrorKey, msg); } catch { }
                System.Diagnostics.Trace.TraceError("Automatic backup failed: " + ex);
                return new ShopBackupCreateResult
                {
                    Succeeded = false,
                    ErrorMessage = msg
                };
            }
            finally
            {
                Interlocked.Exchange(ref _runningFlag, 0);
            }
        }

        private void PruneOldDailyBackups(int keepCount)
        {
            if (keepCount < 1) keepCount = 1;
            if (!Directory.Exists(DailyBackupDirectory)) return;

            var files = new List<FileInfo>(new DirectoryInfo(DailyBackupDirectory).GetFiles("ORVA_Daily_*.shopbackup"));
            if (files.Count <= keepCount) return;

            // Sort newest first by creation/write time
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            // Always keep at least keepCount valid backups (minimum 1)
            for (int i = keepCount; i < files.Count; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch
                {
                    // Non-fatal cleanup failure
                }
            }
        }
    }
}
