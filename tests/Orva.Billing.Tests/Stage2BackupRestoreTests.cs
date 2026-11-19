using System;
using System.IO;
using System.IO.Compression;
using Orva.Domain.Billing;
using Orva.Infrastructure.Repositories;
using Orva.Services;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class Stage2BackupRestoreTests
    {
        [Fact]
        public void ShopBackup_CreateValidateTamperDetectAndRestoreWithRollbackRecovery()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "P101", 202812, 2500, 10000);

                // Issue an invoice so backup includes invoice + payment + movement + audit history
                var billing = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 5000 });
                var inv = billing.FinalizeInvoice(draft);
                Assert.True(inv.Succeeded);

                string testRoot = Path.GetDirectoryName(db.DatabasePath);
                string backupsDir = Path.Combine(testRoot, "Backups");
                string shopFolder = Path.Combine(testRoot, "ShopFolder");

                var backupService = new ShopBackupService(
                    db.Database,
                    new ShopRepository(db.Database),
                    null,
                    backupsDir,
                    s => shopFolder);

                string backupFile = Path.Combine(testRoot, "ShopBackup.shopbackup");
                var created = backupService.CreateBackupToPath(backupFile, alsoUpdatePortableShopCopy: true);
                Assert.True(created.Succeeded);
                Assert.True(File.Exists(backupFile));
                Assert.True(File.Exists(Path.Combine(shopFolder, "ShopBackup.shopbackup")));
                Assert.Equal(1, created.Manifest.InvoiceCount);

                // Mutate DB after backup (add a second medicine)
                db.SeedMedicine("med-after", "Added After Backup", "strip", 1200, true);
                Assert.Equal(2, db.QueryScalarInt64("SELECT COUNT(*) FROM medicine;"));

                // Test failure during restore after live replace -> original DB (2 medicines) MUST be recovered!
                var failedRestore = backupService.ExecuteRestore(backupFile, phase =>
                {
                    if (phase == "after_live_replace") throw new InvalidOperationException("Simulated restore crash");
                });
                Assert.False(failedRestore.Succeeded);
                Assert.True(failedRestore.OriginalDatabaseRecovered);
                Assert.Equal(2, db.QueryScalarInt64("SELECT COUNT(*) FROM medicine;"));

                // Now perform clean restore -> DB reverts to 1 medicine and preserves invoice
                var okRestore = backupService.ExecuteRestore(backupFile);
                Assert.True(okRestore.Succeeded);
                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM medicine;"));
                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice;"));

                // Tamper with archive checksum -> PreviewRestore must reject it
                string tamperedPath = Path.Combine(testRoot, "Tampered.shopbackup");
                File.Copy(backupFile, tamperedPath, true);
                using (var zip = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
                {
                    var dbEntry = zip.GetEntry(ShopBackupService.DatabaseEntryName);
                    dbEntry.Delete();
                    var bad = zip.CreateEntry(ShopBackupService.DatabaseEntryName);
                    using (var w = new StreamWriter(bad.Open())) w.Write("corrupted bytes");
                }
                var tamperedPreview = backupService.PreviewRestore(tamperedPath);
                Assert.False(tamperedPreview.IsValid);
            }
        }

        [Fact]
        public void AutomaticBackupCoordinator_RespectsDailyScheduleAndRetentionNeverDeletesLastBackup()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                string testRoot = Path.GetDirectoryName(db.DatabasePath);
                string backupsDir = Path.Combine(testRoot, "AutoBackups");

                var backupService = new ShopBackupService(db.Database, new ShopRepository(db.Database), null, backupsDir, s => Path.Combine(testRoot, "Docs"));
                var coordinator = new AutomaticBackupCoordinator(backupService, new AppSettingsRepository(db.Database));
                coordinator.SetRetentionDays(2);

                var day1 = new DateTime(2026, 11, 19, 10, 0, 0);
                var r1 = coordinator.RunDailyBackupIfDue(day1);
                Assert.True(r1.Succeeded);
                Assert.False(coordinator.GetStatus(day1).IsDueToday);

                // Running again on same day does not create duplicate
                var r1Again = coordinator.RunDailyBackupIfDue(day1.AddHours(2));
                Assert.True(r1Again.Succeeded);
                Assert.Single(Directory.GetFiles(coordinator.DailyBackupDirectory, "ORVA_Daily_*.shopbackup"));

                // Run on day 2 and day 3 -> retention keeps only 2 newest copies
                Assert.True(coordinator.RunDailyBackupIfDue(day1.AddDays(1)).Succeeded);
                Assert.True(coordinator.RunDailyBackupIfDue(day1.AddDays(2)).Succeeded);
                Assert.Equal(2, Directory.GetFiles(coordinator.DailyBackupDirectory, "ORVA_Daily_*.shopbackup").Length);
            }
        }
    }
}
