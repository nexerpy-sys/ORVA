using System;
using System.IO;
using System.Web.Script.Serialization;
using Orva.Domain.Billing;
using Orva.Infrastructure.Repositories;
using Orva.Services;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class Stage2StockDataV2Tests
    {
        [Fact]
        public void StockDataV2_ExportsAndImportsPreservingHistoryAndBarcodes()
        {
            using (var srcDb = new BillingTestDatabase())
            using (var dstDb = new BillingTestDatabase())
            {
                srcDb.SeedDefaultShop();
                srcDb.SeedMedicine("med-1", "Azithromycin 500mg", "strip", 1200, true, "30042099", 5000);
                srcDb.SeedBatch("b-1", "med-1", "AZ1", 202812, 9500, 12000);
                srcDb.SeedBarcode("8901112223334", "med-1");

                string exportDir = Path.Combine(Path.GetDirectoryName(srcDb.DatabasePath), "Export");
                var srcWriter = new StockSnapshotWriter(srcDb.Database, new ShopRepository(srcDb.Database), s => exportDir, null);
                Assert.True(srcWriter.WriteCurrentSnapshot());

                string stockFile = Path.Combine(exportDir, "StockData.json");
                Assert.True(File.Exists(stockFile));

                // Verify V2 schema fields
                var serializer = new JavaScriptSerializer();
                var parsed = serializer.Deserialize<StockSnapshot>(File.ReadAllText(stockFile));
                Assert.Equal(2, parsed.FormatVersion);
                Assert.Equal("ORVA_STOCK_DATA", parsed.Format);
                Assert.Single(parsed.Medicines);
                Assert.True(parsed.Medicines[0].GstRateConfigured == true);
                Assert.Contains("8901112223334", parsed.Medicines[0].Barcodes);

                // Seed target DB with an existing historical invoice on another medicine (med-old)
                dstDb.SeedDefaultShop();
                dstDb.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                dstDb.SeedMedicine("med-old", "Old Sold Med", "strip", 1200, true);
                dstDb.SeedBatch("b-old", "med-old", "OLD1", 202812, 2000, 5000);

                var billing = dstDb.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-old", QuantityMilli = 1000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 2000 });
                var inv = billing.FinalizeInvoice(draft);
                Assert.True(inv.Succeeded);

                // Import source StockData.json into target DB
                var importer = new StockDataImporter(dstDb.Database, new ShopRepository(dstDb.Database), null);
                var preview = importer.PreviewImport(stockFile);
                Assert.True(preview.IsValid);
                Assert.Equal(1, preview.NewMedicineCount);
                Assert.Equal(1, preview.ZeroedBatchCount);

                var importRes = importer.ExecuteImport(stockFile, confirmDifferentShop: false);
                Assert.True(importRes.Succeeded);

                // Historical invoice & old medicine row MUST remain intact; old batch zeroed via IMPORT movement
                Assert.Equal(1, dstDb.QueryScalarInt64("SELECT COUNT(*) FROM invoice WHERE invoice_id = @id;", p => p.AddWithValue("@id", inv.InvoiceId)));
                Assert.Equal(0, dstDb.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-old';"));
                Assert.Equal(0, dstDb.QueryScalarInt64("SELECT is_active FROM medicine WHERE medicine_id = 'med-old';"));
                Assert.Equal(0, dstDb.QueryScalarInt64("SELECT COUNT(*) FROM purchase_receipt;")); // Never fabricate purchases!
                Assert.Equal(12000, dstDb.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal(1, dstDb.QueryScalarInt64("SELECT COUNT(*) FROM barcode_mapping WHERE barcode = '8901112223334' AND medicine_id = 'med-1';"));
            }
        }

        [Fact]
        public void StockDataV1_ImportTreatsMissingGstFlagAsUnconfigured_AndRejectsFutureVersions()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                string dir = Path.GetDirectoryName(db.DatabasePath);

                // V1 file without GstRateConfigured or Barcodes
                string v1Path = Path.Combine(dir, "v1.json");
                File.WriteAllText(v1Path, @"{
                    ""Format"": ""ORVA_STOCK_DATA"",
                    ""FormatVersion"": 1,
                    ""ShopId"": ""shop-001"",
                    ""ShopName"": ""ORVA Test Pharmacy"",
                    ""GeneratedAtUtc"": ""2026-11-18T10:00:00Z"",
                    ""Medicines"": [{
                        ""MedicineId"": ""med-v1"",
                        ""Name"": ""Legacy Med"",
                        ""StockUnit"": ""strip"",
                        ""GstRateBasisPoints"": 0,
                        ""LowStockThresholdMilli"": 0,
                        ""Batches"": [{
                            ""BatchId"": ""b-v1"",
                            ""BatchNumber"": ""L1"",
                            ""ExpiryYearMonth"": 202812,
                            ""MrpPaise"": 1500,
                            ""LatestPurchasePricePaise"": 1000,
                            ""QuantityMilli"": 5000,
                            ""Status"": ""ACTIVE""
                        }]
                    }]
                }");

                var importer = new StockDataImporter(db.Database, new ShopRepository(db.Database), null);
                var v1Res = importer.ExecuteImport(v1Path, false);
                Assert.True(v1Res.Succeeded);
                // Critical: V1 missing GST flag MUST remain unconfigured (0), NEVER assumed 0% exempt (1)
                Assert.Equal(0, db.QueryScalarInt64("SELECT gst_rate_configured FROM medicine WHERE medicine_id = 'med-v1';"));

                // Future version v99 must be rejected
                string v99Path = Path.Combine(dir, "v99.json");
                File.WriteAllText(v99Path, @"{ ""Format"": ""ORVA_STOCK_DATA"", ""FormatVersion"": 99, ""ShopId"": ""shop-001"", ""ShopName"": ""Shop"", ""Medicines"": [] }");
                var v99Prev = importer.PreviewImport(v99Path);
                Assert.False(v99Prev.IsValid);
            }
        }

        [Fact]
        public void StockDataImport_MidpointFailure_RollsBackCompletely()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedMedicine("med-existing", "Existing Med", "strip", 1200, true);
                db.SeedBatch("b-existing", "med-existing", "E1", 202812, 2000, 7000);

                string file = Path.Combine(Path.GetDirectoryName(db.DatabasePath), "rollback_import.json");
                File.WriteAllText(file, @"{
                    ""Format"": ""ORVA_STOCK_DATA"",
                    ""FormatVersion"": 2,
                    ""ShopId"": ""shop-001"",
                    ""ShopName"": ""ORVA Test Pharmacy"",
                    ""GeneratedAtUtc"": ""2026-11-19T00:00:00Z"",
                    ""Medicines"": [{
                        ""MedicineId"": ""med-new"",
                        ""Name"": ""New Med"",
                        ""StockUnit"": ""strip"",
                        ""GstRateBasisPoints"": 1200,
                        ""GstRateConfigured"": true,
                        ""Batches"": [{
                            ""BatchId"": ""b-new"",
                            ""BatchNumber"": ""N1"",
                            ""ExpiryYearMonth"": 202812,
                            ""MrpPaise"": 3000,
                            ""LatestPurchasePricePaise"": 2000,
                            ""QuantityMilli"": 4000,
                            ""Status"": ""ACTIVE""
                        }]
                    }]
                }");

                var importer = new StockDataImporter(
                    db.Database,
                    new ShopRepository(db.Database),
                    null,
                    null,
                    cp => { if (cp == "before_audit_and_commit") throw new InvalidOperationException("Simulated import failure"); });

                Assert.Throws<InvalidOperationException>(() => importer.ExecuteImport(file, false));

                // Original inventory untouched; new medicine not persisted
                Assert.Equal(7000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-existing';"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM medicine WHERE medicine_id = 'med-new';"));
            }
        }
    }
}
