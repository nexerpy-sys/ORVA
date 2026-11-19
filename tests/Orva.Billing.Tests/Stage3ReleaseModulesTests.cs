using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Orva.Controls;
using Orva.Domain;
using Orva.Domain.Billing;
using Orva.Infrastructure.Repositories;
using Orva.Services;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class Stage3ReleaseModulesTests
    {
        [Fact]
        public void SalesReturn_QuarantineDefaultKeepsStockUnchanged_AndRestockIncrementsNonExpiredBatch()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "P101", 202912, 2500, 10000);

                var billing = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 4000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 10000 });
                var inv = billing.FinalizeInvoice(draft);
                Assert.True(inv.Succeeded);
                Assert.Equal(6000L, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));

                var returnService = new SalesReturnService(db.Database);
                var lines = returnService.GetReturnableLines(inv.InvoiceId);
                Assert.Single(lines);
                Assert.Equal(4000L, lines[0].RemainingReturnableMilli);

                // 1) Return 1 strip as QUARANTINE (default) -> stock remains 6000
                var quarantineReq = new SalesReturnRequest
                {
                    InvoiceId = inv.InvoiceId,
                    Reason = "Damaged strip",
                    RefundMethod = "CASH"
                };
                quarantineReq.Lines.Add(new SalesReturnLineRequest
                {
                    InvoiceLineId = lines[0].InvoiceLineId,
                    ReturnQuantityMilli = 1000,
                    ResaleDisposition = "QUARANTINE"
                });

                var qPreview = returnService.PreviewReturn(quarantineReq);
                Assert.True(qPreview.CanFinalize);
                Assert.Equal(2500L, qPreview.TotalRefundPaise);
                Assert.Equal(1, qPreview.QuarantinedLineCount);
                Assert.Equal(0, qPreview.RestockedLineCount);

                var qResult = returnService.FinalizeReturn(quarantineReq);
                Assert.True(qResult.Succeeded);
                Assert.Equal(6000L, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));

                // 2) Return 1 strip as RESTOCK -> stock increments to 7000 and writes SALE_RETURN_RESTOCK
                var restockReq = new SalesReturnRequest
                {
                    InvoiceId = inv.InvoiceId,
                    Reason = "Unopened strip returned",
                    RefundMethod = "CASH"
                };
                restockReq.Lines.Add(new SalesReturnLineRequest
                {
                    InvoiceLineId = lines[0].InvoiceLineId,
                    ReturnQuantityMilli = 1000,
                    ResaleDisposition = "RESTOCK"
                });

                var rResult = returnService.FinalizeReturn(restockReq);
                Assert.True(rResult.Succeeded);
                Assert.Equal(7000L, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal(1L, db.QueryScalarInt64("SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'SALE_RETURN_RESTOCK';"));

                // 3) Duplicate lines exceeding remaining 2 strips (1.5 + 1.5 = 3.0) must be blocked
                var dupOverReq = new SalesReturnRequest
                {
                    InvoiceId = inv.InvoiceId,
                    Reason = "Duplicate over-return test",
                    RefundMethod = "CASH"
                };
                dupOverReq.Lines.Add(new SalesReturnLineRequest { InvoiceLineId = lines[0].InvoiceLineId, ReturnQuantityMilli = 1500, ResaleDisposition = "QUARANTINE" });
                dupOverReq.Lines.Add(new SalesReturnLineRequest { InvoiceLineId = lines[0].InvoiceLineId, ReturnQuantityMilli = 1500, ResaleDisposition = "QUARANTINE" });

                var overPreview = returnService.PreviewReturn(dupOverReq);
                Assert.False(overPreview.CanFinalize);
                Assert.NotEmpty(overPreview.Errors);

                // 4) Net GST Summary reflects only the remaining 2 strips (half of original 4 strips)
                var reporting = new ReportingService(db.Database);
                var netGst = reporting.GetGstSummary(null, null);
                Assert.Single(netGst);
                Assert.Equal(5000L, netGst[0].TotalValuePaise);
            }
        }

        [Fact]
        public void InvoiceCancellation_VoidsAndRestoresStockAtomically_AndBlocksIfReturnExists()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Amoxicillin 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "A202", 202912, 5000, 10000);

                var billing = db.CreateService();
                var draft1 = new InvoiceDraft();
                draft1.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                draft1.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 5000 });
                var inv1 = billing.FinalizeInvoice(draft1);
                Assert.True(inv1.Succeeded);
                Assert.Equal(9000L, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));

                var cancelService = new InvoiceCancellationService(db.Database);
                var cancelResult = cancelService.CancelInvoice(inv1.InvoiceId, "Wrong customer name", restoreStock: true);
                Assert.True(cancelResult.Succeeded);
                Assert.True(cancelResult.StockRestored);

                // Row preserved as CANCELLED, stock restored to 10000, movement + audit log recorded
                Assert.Equal(1L, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice WHERE invoice_id = '" + inv1.InvoiceId + "' AND status = 'CANCELLED';"));
                Assert.Equal(10000L, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal(1L, db.QueryScalarInt64("SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'ADJUSTMENT' AND reference_type = 'INVOICE_CANCEL';"));
                Assert.True(db.QueryScalarInt64("SELECT COUNT(*) FROM audit_log WHERE action_type = 'INVOICE_CANCELLED';") >= 1L);

                // Cancelled invoice is excluded from GST summary
                var reporting = new ReportingService(db.Database);
                var gstRows = reporting.GetGstSummary(null, null);
                Assert.Empty(gstRows);

                // Second invoice with a return cannot be cancelled
                var draft2 = new InvoiceDraft();
                draft2.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                draft2.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 5000 });
                var inv2 = billing.FinalizeInvoice(draft2);
                Assert.True(inv2.Succeeded);

                var returnService = new SalesReturnService(db.Database);
                var lines2 = returnService.GetReturnableLines(inv2.InvoiceId);
                var retReq = new SalesReturnRequest { InvoiceId = inv2.InvoiceId, Reason = "Partial return", RefundMethod = "CASH" };
                retReq.Lines.Add(new SalesReturnLineRequest { InvoiceLineId = lines2[0].InvoiceLineId, ReturnQuantityMilli = 1000, ResaleDisposition = "QUARANTINE" });
                Assert.True(returnService.FinalizeReturn(retReq).Succeeded);

                var blockedCancel = cancelService.CancelInvoice(inv2.InvoiceId, "Should fail");
                Assert.False(blockedCancel.Succeeded);
            }
        }

        [Fact]
        public void ReportingAndExports_CoversSalesGstStockValuationExpensesCsvAndXlsx()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Cetirizine 10mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "C303", 202912, 2000, 5000);

                var billing = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Upi, AmountPaise = 4000 });
                var inv = billing.FinalizeInvoice(draft);
                Assert.True(inv.Succeeded);

                var reporting = new ReportingService(db.Database);
                var sales = reporting.GetSalesRegister(null, null, "ALL", null);
                Assert.Single(sales);
                Assert.Equal("ISSUED", sales[0].Status);

                var reprint = reporting.GetInvoiceForReprint(inv.InvoiceId);
                Assert.NotNull(reprint);
                Assert.Equal(inv.InvoiceNumber, reprint.CommittedInvoice.InvoiceNumber);
                Assert.Single(reprint.Preview.Lines);

                var gst = reporting.GetGstSummary(null, null);
                Assert.Single(gst);
                Assert.Equal(1200, gst[0].GstRateBasisPoints);

                var valuation = reporting.GetStockValuation();
                Assert.Single(valuation);
                Assert.Equal(3000L, valuation[0].UsableQuantityMilli);

                reporting.AddExpense("Electricity", "Monthly shop bill", 125000, "UPI", "EB-101");
                var expenses = reporting.GetExpenses(null, null);
                Assert.Single(expenses);
                Assert.Equal(125000L, expenses[0].AmountPaise);

                string dir = Path.GetDirectoryName(db.DatabasePath);
                string csvPath = Path.Combine(dir, "sales_export.csv");
                reporting.ExportSalesRegisterCsv(csvPath, sales);
                Assert.True(File.Exists(csvPath));
                byte[] bytes = File.ReadAllBytes(csvPath);
                Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

                string xlsxPath = Path.Combine(dir, "sales_export.xlsx");
                reporting.ExportSalesRegisterXlsx(xlsxPath, sales);
                Assert.True(File.Exists(xlsxPath));
                using (var zip = ZipFile.OpenRead(xlsxPath))
                {
                    Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
                }
            }
        }

        [Fact]
        public void MedicineCatalogueImporter_SupportsFlexibleExpiryBarcodeAndXlsxImport()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                string dir = Path.GetDirectoryName(db.DatabasePath);
                string templatePath = Path.Combine(dir, "template.csv");
                MedicineCatalogueImporter.WriteTemplateCsv(templatePath);
                Assert.True(File.Exists(templatePath));

                // Test CSV with YYYY-MM expiry, Barcode, and blank purchase price on second additive row
                string csvPath = Path.Combine(dir, "import.csv");
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", MedicineCatalogueImporter.TemplateHeaders));
                sb.AppendLine("Dolo 650,Paracetamol,Micro Labs,650mg,Tablet,strip,3004,12,5,8901234567890,D650A,2029-12,10,22.50,30.00");
                sb.AppendLine("Dolo 650,Paracetamol,Micro Labs,650mg,Tablet,strip,3004,12,5,,D650A,12/2029,5,,30.00");
                File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(true));

                var importer = new MedicineCatalogueImporter(db.Database);
                var preview = importer.PreviewCsv(csvPath);
                Assert.True(preview.IsValid);
                Assert.Equal(2, preview.OpeningBatchRowCount);

                var result = importer.ExecuteImport(csvPath, markProvidedGstRatesAsConfigured: true);
                Assert.True(result.Succeeded);
                Assert.Equal(1, result.MedicinesCreated);
                Assert.Equal(15000L, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_number = 'D650A';"));
                Assert.Equal(2250L, db.QueryScalarInt64("SELECT latest_purchase_price_paise FROM stock_batch WHERE batch_number = 'D650A';"));
                Assert.Equal(1L, db.QueryScalarInt64("SELECT COUNT(*) FROM barcode_mapping WHERE barcode = '8901234567890';"));
                Assert.Equal(2L, db.QueryScalarInt64("SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'OPENING';"));
            }
        }

        [Fact]
        public void InvoicePrintService_ComputesTargetDipSizesForAllSupportedFormats()
        {
            var s58 = InvoicePrintService.GetTargetDipSize(InvoicePaperFormat.Thermal58, 800, 1000);
            var s80 = InvoicePrintService.GetTargetDipSize(InvoicePaperFormat.Thermal80, 800, 1000);
            var sA5 = InvoicePrintService.GetTargetDipSize(InvoicePaperFormat.A5, 800, 1000);
            var sA4 = InvoicePrintService.GetTargetDipSize(InvoicePaperFormat.A4, 800, 1000);

            Assert.True(s58.Width > 180 && s58.Width < 230 && s58.Height == 0.0);
            Assert.True(s80.Width > 260 && s80.Width < 310 && s80.Height == 0.0);
            Assert.True(sA5.Width > 500 && sA5.Width < 580);
            Assert.True(sA4.Width > 760 && sA4.Width <= 800);
        }

        [Fact]
        public void SubscriptionService_ValidatesHeaderMappedCsv_Enforces14DayOfflineGrace_AndBuildsWhatsAppUrl()
        {
            using (var db = new BillingTestDatabase())
            {
                var settings = new AppSettingsRepository(db.Database);
                var sub = new SubscriptionService(settings);
                string code = "ORVA-TEST-2026";
                string hash = SubscriptionService.ComputeSha256Hex(code);
                string csv = "username,activation_code_sha256,plan,start_date,expiry_date,status\n"
                    + "rindle_shop_01," + hash + ",Annual,2026-01-01,2027-01-01,ACTIVE\n";

                var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
                sub.SaveCredentials("rindle_shop_01", code, "https://example.invalid/licenses.csv");

                var active = sub.ApplyRegistryCsv(csv, "rindle_shop_01", code, t0);
                Assert.True(active.IsActive);
                Assert.True(active.AllowNewBilling);

                // 13 days offline -> still within 14-day grace period
                var day13 = sub.EvaluateLocalStatus(t0.AddDays(13));
                Assert.True(day13.AllowNewBilling);
                Assert.True(day13.IsInGracePeriod);
                Assert.Equal(1, day13.GraceDaysRemaining);

                // 15 days offline -> grace expired -> billing disabled, records remain accessible
                var day15 = sub.EvaluateLocalStatus(t0.AddDays(15));
                Assert.False(day15.AllowNewBilling);

                // Clock rollback detected -> billing disabled
                var rolledBack = sub.EvaluateLocalStatus(t0.AddDays(10));
                Assert.False(rolledBack.AllowNewBilling);
                Assert.Equal("ClockMismatch", rolledBack.Status);

                string wa = SubscriptionService.BuildWhatsAppClickToChatUrl(
                    "+91 98765-43210", "Rindle Medical", "rindle_shop_01", "Annual", "2400", "UTR123456");
                Assert.StartsWith("https://wa.me/919876543210?text=", wa);
            }
        }

        [Fact]
        public void PdfExportAndQrCode_ProduceValidPdfAndScannableMatrix_And60DayHueHasNoAmber()
        {
            using (var db = new BillingTestDatabase())
            {
                string dir = Path.GetDirectoryName(db.DatabasePath);
                string pdfPath = Path.Combine(dir, "register_test.pdf");
                PdfExportHelper.WriteTablePdf(
                    pdfPath,
                    "ORVA - Test Register",
                    "Period: All Dates",
                    new[] { "Invoice No", "Customer", "Total" },
                    new System.Collections.Generic.List<System.Collections.Generic.IList<string>>
                    {
                        new[] { "INV/26-27/000001", "Walk-in", "250.00" }
                    });
                Assert.True(File.Exists(pdfPath));
                string pdfText = Encoding.ASCII.GetString(File.ReadAllBytes(pdfPath));
                Assert.StartsWith("%PDF-1.4", pdfText);
                Assert.Contains("xref", pdfText);
                Assert.Contains("%%EOF", pdfText);
            }

            string upi = SubscriptionService.BuildUpiPaymentUri("rindle@upi", "Rindle Works", "499", "ORVA Monthly");
            bool[,] matrix = QrCodeHelper.EncodeMatrix(upi);
            int size = matrix.GetLength(0);
            Assert.True(size >= 21 && size <= 57 && size == matrix.GetLength(1));
            // Verify top-left 7x7 finder outer border is dark
            Assert.True(matrix[0, 0] && matrix[0, 6] && matrix[6, 0] && matrix[6, 6] && matrix[3, 3]);

            Assert.Equal("#FF7A57", InventoryAlertCalculator.GetExpiryColour(ExpiryAlertLevel.Warning60Days));
        }
    }
}
