using System;
using Orva.Domain;
using Orva.Infrastructure.Repositories;
using Orva.Services;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class Stage2InventoryAlertsTests
    {
        [Fact]
        public void ExpiryCalculator_HandlesMonthEndLeapYearAndExact90_60_30Boundaries()
        {
            // Leap year February 2028 ends on Feb 29, 2028
            var leapEnd = InventoryAlertCalculator.GetMonthEndExpiryDate(202802);
            Assert.Equal(new DateTime(2028, 2, 29), leapEnd);

            // Non-leap February 2027 ends on Feb 28, 2027
            var nonLeapEnd = InventoryAlertCalculator.GetMonthEndExpiryDate(202702);
            Assert.Equal(new DateTime(2027, 2, 28), nonLeapEnd);

            // Exact boundaries relative to 2027-02-28
            Assert.Equal(ExpiryAlertLevel.Expired, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2027, 3, 1)));
            Assert.Equal(ExpiryAlertLevel.UrgentWarning30Days, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2027, 2, 28)));
            Assert.Equal(ExpiryAlertLevel.UrgentWarning30Days, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2027, 1, 29))); // 30 days
            Assert.Equal(ExpiryAlertLevel.Warning60Days, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2027, 1, 28)));      // 31 days
            Assert.Equal(ExpiryAlertLevel.Warning60Days, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2026, 12, 30)));     // 60 days
            Assert.Equal(ExpiryAlertLevel.EarlyWarning90Days, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2026, 12, 29))); // 61 days
            Assert.Equal(ExpiryAlertLevel.EarlyWarning90Days, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2026, 11, 30))); // 90 days
            Assert.Equal(ExpiryAlertLevel.None, InventoryAlertCalculator.ClassifyExpiry("ACTIVE", 202702, new DateTime(2026, 11, 29)));               // 91 days
        }

        [Fact]
        public void LowStockAlerts_ExcludeExpiredQuarantinedBlockedInactive_AndClearAfterReplenish()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true, "30049099", lowStockThresholdMilli: 20000);
                db.SeedMedicine("med-inactive", "Inactive Med", "strip", 1200, true, "30049099", lowStockThresholdMilli: 50000, isActive: false);

                // Usable = 14 strips; expired/quarantined/blocked must NOT inflate usable stock
                db.SeedBatch("b-ok", "med-1", "OK1", 202812, 2500, 14000, "ACTIVE");
                db.SeedBatch("b-exp", "med-1", "EXP1", 202610, 2500, 50000, "ACTIVE");
                db.SeedBatch("b-q", "med-1", "Q1", 202812, 2500, 50000, "QUARANTINED");
                db.SeedBatch("b-blk", "med-1", "BLK1", 202812, 2500, 50000, "BLOCKED");

                var repo = new InventoryRepository(db.Database);
                var businessDate = new DateTime(2026, 11, 19);

                var alerts = repo.GetLowStockAlerts(businessDate);
                Assert.Single(alerts);
                Assert.Equal("med-1", alerts[0].MedicineId);
                Assert.Equal(14000, alerts[0].UsableQuantityMilli);
                Assert.Equal(20000, alerts[0].ThresholdMilli);

                // Replenish 10 strips -> usable becomes 24 > 20 -> alert clears automatically
                repo.SaveStockEntry(new StockEntryInput
                {
                    MedicineName = "Paracetamol 500mg",
                    StockUnitName = "strip",
                    BatchNumber = "OK2",
                    ExpiryYearMonth = 202906,
                    QuantityMilli = 10000,
                    PurchasePricePaise = 1800,
                    MrpPaise = 2500,
                    DocumentType = "NO_DOCUMENT"
                });

                Assert.Empty(repo.GetLowStockAlerts(businessDate));
            }
        }

        [Fact]
        public void StockAdjustment_UpdatesBatchRecordsMovementDiscrepancyAuditAndIncrementsGeneration()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedMedicine("med-1", "Cetirizine 10mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "C101", 202812, 3000, 10000);

                var repo = new InventoryRepository(db.Database);
                var writer = new StockSnapshotWriter(db.Database, new ShopRepository(db.Database));
                long genBefore = writer.GetCurrentGeneration();

                var result = repo.AdjustBatchStock(new StockAdjustmentInput
                {
                    BatchId = "b-1",
                    CountedQuantityMilli = 8000,
                    Reason = "2 damaged strips removed"
                });

                Assert.Equal(10000, result.PreviousQuantityMilli);
                Assert.Equal(8000, result.NewQuantityMilli);
                Assert.Equal(-2000, result.DifferenceMilli);

                Assert.Equal(8000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal(-2000, db.QueryScalarInt64("SELECT quantity_delta_milli FROM stock_movement WHERE batch_id = 'b-1' AND movement_type = 'ADJUSTMENT';"));
                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM stock_discrepancy WHERE batch_id = 'b-1' AND difference_milli = -2000;"));
                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM audit_log WHERE action_type = 'STOCK_ADJUSTED' AND entity_id = 'b-1';"));
                Assert.True(writer.GetCurrentGeneration() > genBefore);
                Assert.True(writer.IsDirty());
            }
        }
    }
}
