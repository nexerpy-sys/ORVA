using System;
using System.IO;
using Orva.Domain;
using Orva.Domain.Billing;
using Orva.Infrastructure.Repositories;
using Orva.Services;
using Orva.Services.Billing;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class Stage1ReliabilityTests
    {
        [Fact]
        public void GstinValidator_ValidatesStructureAndOfficialMod36Checksum()
        {
            var validWb = GstinValidator.Validate("19ABCDE1234F1ZX");
            Assert.True(validWb.IsValid);
            Assert.Equal("19", validWb.StateCode);
            Assert.Equal("19ABCDE1234F1ZX", validWb.NormalizedGstin);

            var validMh = GstinValidator.Validate("27ABCDE1234F1Z0");
            Assert.True(validMh.IsValid);
            Assert.Equal("27", validMh.StateCode);

            var badChecksum = GstinValidator.Validate("19ABCDE1234F1Z5");
            Assert.False(badChecksum.IsValid);

            var badState = GstinValidator.Validate("95ABCDE1234F1Z1");
            Assert.False(badState.IsValid);

            var badStructure = GstinValidator.Validate("1912345ABCD12Z0");
            Assert.False(badStructure.IsValid);
        }

        [Fact]
        public void BillingService_BlocksWhenShopStateCodeMissing_AndFailsSafelyOnOutOfStateCustomerGstin()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop(stateCode: null);
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "P101", 202812, 2500, 5000);

                var svc = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 2500 });

                var missingState = svc.PreviewInvoice(draft);
                Assert.False(missingState.CanFinalize);
                Assert.Contains(missingState.Issues, i => i.Code == BillingIssueCodes.ShopStateCodeMissing);

                // Set valid West Bengal state code (19), test out-of-state Maharashtra GSTIN (27)
                db.ExecuteNonQuery("UPDATE shop_profile SET state_code = '19' WHERE profile_id = 1;");
                draft.Customer.Gstin = "27ABCDE1234F1Z0";

                var outOfState = svc.PreviewInvoice(draft);
                Assert.False(outOfState.CanFinalize);
                Assert.Contains(outOfState.Issues, i => i.Code == BillingIssueCodes.PlaceOfSupplyRequiresReview);

                // Same-state valid GSTIN succeeds
                draft.Customer.Gstin = "19ABCDE1234F1ZX";
                var sameState = svc.PreviewInvoice(draft);
                Assert.True(sameState.CanFinalize);
            }
        }

        [Fact]
        public void InvoiceCalculator_Enforces16CharacterGstRule46Limit()
        {
            Assert.True(InvoiceCalculator.IsValidInvoicePrefix("INV"));
            Assert.True(InvoiceCalculator.IsValidInvoicePrefix("OR"));
            Assert.False(InvoiceCalculator.IsValidInvoicePrefix("ORVA"));
            Assert.False(InvoiceCalculator.IsValidInvoicePrefix(""));

            string ok = InvoiceCalculator.FormatInvoiceNumber("INV", "26-27", 1);
            Assert.Equal("INV/26-27/000001", ok);
            Assert.Equal(16, ok.Length);

            Assert.Throws<InvalidOperationException>(() =>
                InvoiceCalculator.FormatInvoiceNumber("ORVA", "26-27", 1));
        }

        [Fact]
        public void TaxSettingsRepository_SavesShopAndMedicineTaxAtomicallyWithAuditLogs()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop(stateCode: "19");
                db.SeedMedicine("med-1", "Insulin Glargine", "vial", 0, false, null);

                var repo = new TaxSettingsRepository(db.Database);
                repo.SaveTaxSettings(true, "INCLUSIVE", "INTRA_STATE", "NEAREST_RUPEE");

                var loaded = repo.GetTaxSettings();
                Assert.True(loaded.IsConfigured);
                Assert.Equal("INCLUSIVE", loaded.PricingModeRaw);
                Assert.Equal("INTRA_STATE", loaded.DefaultSupplyTypeRaw);
                Assert.Equal("NEAREST_RUPEE", loaded.RoundOffModeRaw);

                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM audit_log WHERE action_type = 'TAX_SETTINGS_UPDATED';"));

                // Configure medicine explicitly as 0% exempt with HSN
                repo.UpdateMedicineTaxConfig("med-1", "30043110", 0, true);
                Assert.Equal(1, db.QueryScalarInt64("SELECT gst_rate_configured FROM medicine WHERE medicine_id = 'med-1';"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT gst_rate_basis_points FROM medicine WHERE medicine_id = 'med-1';"));
                Assert.Equal("30043110", db.QueryScalarString("SELECT hsn_code FROM medicine WHERE medicine_id = 'med-1';"));
                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM audit_log WHERE action_type = 'MEDICINE_TAX_UPDATED' AND entity_id = 'med-1';"));
            }
        }

        [Fact]
        public void StockSnapshotWriter_GenerationRace_NeverMarksNewerStockClean()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop(stateCode: "19");
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "P101", 202812, 2500, 10000);

                var shopRepo = new ShopRepository(db.Database);
                var inventoryRepo = new InventoryRepository(db.Database);
                string snapshotDir = Path.Combine(Path.GetDirectoryName(db.DatabasePath), "SnapshotRaceTest");

                bool concurrentWriteInjected = false;
                var writer = new StockSnapshotWriter(
                    db.Database,
                    shopRepo,
                    shopName => snapshotDir,
                    () =>
                    {
                        if (!concurrentWriteInjected)
                        {
                            concurrentWriteInjected = true;
                            // Simulate a concurrent stock receipt committing after snapshot read
                            // and before the older snapshot writer attempts to mark dirty=0.
                            inventoryRepo.SaveStockEntry(new StockEntryInput
                            {
                                MedicineName = "Paracetamol 500mg",
                                StockUnitName = "strip",
                                BatchNumber = "P102",
                                ExpiryYearMonth = 202906,
                                QuantityMilli = 5000,
                                PurchasePricePaise = 1800,
                                MrpPaise = 2500,
                                DocumentType = "NO_DOCUMENT"
                            });
                        }
                    });

                // Initial dirty state from generation 1
                writer.MarkDirty();
                long genBefore = writer.GetCurrentGeneration();
                Assert.True(genBefore >= 1);

                // First write encounters concurrent generation bump -> must NOT mark clean
                bool markedClean = writer.WriteCurrentSnapshot();
                Assert.False(markedClean);
                Assert.True(writer.IsDirty());
                Assert.True(writer.GetCurrentGeneration() > genBefore);

                // Second write with no concurrent mutation -> marks clean
                bool secondClean = writer.WriteCurrentSnapshot();
                Assert.True(secondClean);
                Assert.False(writer.IsDirty());
                Assert.True(File.Exists(Path.Combine(snapshotDir, "StockData.json")));
            }
        }
    }
}
