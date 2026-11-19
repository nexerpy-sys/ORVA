using System;
using System.Collections.Generic;
using Orva.Domain.Billing;
using Orva.Services.Billing;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class BillingServiceTests
    {
        // 1. Search by partial medicine name and recognised barcode.
        [Fact]
        public void T01_SearchMedicines_FindsByPartialNameAndRecognisedBarcode()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true);
                db.SeedMedicine("med-2", "Azithromycin 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "P101", 202812, 2500, 5000);
                db.SeedBatch("b-2", "med-2", "A201", 202812, 9500, 3000);
                db.SeedBarcode("8901234567890", "med-2");

                var svc = db.CreateService();

                var byName = svc.SearchMedicines("paracet", 10);
                Assert.Single(byName);
                Assert.Equal("med-1", byName[0].MedicineId);
                Assert.True(byName[0].CanSell);
                Assert.Equal(5000, byName[0].AvailableQuantityMilli);

                var byBarcode = svc.SearchMedicines("8901234567890", 10);
                Assert.Single(byBarcode);
                Assert.Equal("med-2", byBarcode[0].MedicineId);
                Assert.True(byBarcode[0].CanSell);
                Assert.Equal(3000, byBarcode[0].AvailableQuantityMilli);
            }
        }

        // 2. Search bounds and empty query behaviour.
        [Fact]
        public void T02_SearchMedicines_RespectsBoundsAndEmptyQuery()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedMedicine("med-1", "Alpha 10mg", "strip", 1200, true);
                db.SeedMedicine("med-2", "Beta 20mg", "strip", 1200, true);
                db.SeedMedicine("med-3", "Gamma 30mg", "strip", 1200, true);

                var svc = db.CreateService();

                Assert.Empty(svc.SearchMedicines("Alpha", 0));
                Assert.Empty(svc.SearchMedicines("Alpha", -5));

                var bounded = svc.SearchMedicines("", 2);
                Assert.Equal(2, bounded.Count);
                Assert.False(bounded[0].CanSell);
                Assert.Equal("Out of stock", bounded[0].AvailabilityMessage);
            }
        }

        // 3. One medicine/one batch successful invoice.
        [Fact]
        public void T03_FinalizeInvoice_SingleMedicineSingleBatch_Succeeds()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true, "30049099");
                db.SeedBatch("b-1", "med-1", "P101", 202812, 2500, 10000);

                var svc = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 5000 });

                var result = svc.FinalizeInvoice(draft);

                Assert.True(result.Succeeded);
                Assert.Equal("INV/26-27/000001", result.InvoiceNumber);
                Assert.Equal(5000, result.GrandTotalPaise);
                Assert.Equal(8000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
            }
        }

        // 4. FEFO selection across two or more batches.
        [Fact]
        public void T04_FefoSelection_PicksEarliestExpiryFirst()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Cetirizine 10mg", "strip", 1200, true);
                db.SeedBatch("b-late", "med-1", "LATE01", 202906, 3000, 5000);
                db.SeedBatch("b-early", "med-1", "EARLY01", 202703, 3000, 5000);

                var svc = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 6000 });

                var preview = svc.PreviewInvoice(draft);
                Assert.Single(preview.Lines[0].BatchAllocations);
                Assert.Equal("EARLY01", preview.Lines[0].BatchAllocations[0].BatchNumber);

                var finalized = svc.FinalizeInvoice(draft);
                Assert.True(finalized.Succeeded);
                Assert.Equal(3000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-early';"));
                Assert.Equal(5000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-late';"));
            }
        }

        // 5 & 6. Split one medicine line across multiple batches and respect different MRP per batch.
        [Fact]
        public void T05_T06_SplitAcrossBatches_RespectsDifferentMrpPerBatch()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Pantoprazole 40mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "B1", 202701, 4000, 2000); // 2 strips @ Rs 40.00 = Rs 80.00
                db.SeedBatch("b-2", "med-1", "B2", 202801, 5500, 5000); // 3 strips @ Rs 55.00 = Rs 165.00

                var svc = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 5000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Upi, AmountPaise = 24500, ReferenceText = "UTR123" });

                var preview = svc.PreviewInvoice(draft);
                Assert.True(preview.CanFinalize);
                Assert.Single(preview.Lines);
                Assert.Equal(2, preview.Lines[0].BatchAllocations.Count);
                Assert.Equal(4000, preview.Lines[0].BatchAllocations[0].UnitPricePaise);
                Assert.Equal(8000, preview.Lines[0].BatchAllocations[0].LineTotalPaise);
                Assert.Equal(5500, preview.Lines[0].BatchAllocations[1].UnitPricePaise);
                Assert.Equal(16500, preview.Lines[0].BatchAllocations[1].LineTotalPaise);
                Assert.Equal(24500, preview.GrandTotalPaise);

                var finalized = svc.FinalizeInvoice(draft);
                Assert.True(finalized.Succeeded);
                Assert.Equal(0, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal(2000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-2';"));
                Assert.Equal(2, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice_line WHERE invoice_id = @id;", p => p.AddWithValue("@id", finalized.InvoiceId)));
            }
        }

        // 7. Expired, quarantined, blocked and empty batches excluded.
        [Fact]
        public void T07_IneligibleBatches_AreExcludedFromSearchPreviewAndFinalize()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Dolo 650", "strip", 1200, true);
                db.SeedBatch("b-exp", "med-1", "EXP1", 202610, 3000, 5000, "ACTIVE");     // Expired relative to 202611
                db.SeedBatch("b-quar", "med-1", "QUA1", 202812, 3000, 5000, "QUARANTINED");
                db.SeedBatch("b-blk", "med-1", "BLK1", 202812, 3000, 5000, "BLOCKED");
                db.SeedBatch("b-zero", "med-1", "ZER1", 202812, 3000, 0, "ACTIVE");
                db.SeedBatch("b-ok", "med-1", "OK1", 202611, 3000, 2000, "ACTIVE");       // Valid through end of 2026-11

                var svc = db.CreateService();
                var lookup = svc.SearchMedicines("Dolo", 10);
                Assert.Single(lookup);
                Assert.Equal(2000, lookup[0].AvailableQuantityMilli);

                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 6000 });

                var result = svc.FinalizeInvoice(draft);
                Assert.True(result.Succeeded);
                Assert.Equal(0, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-ok';"));
                Assert.Equal(5000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-exp';"));
            }
        }

        // 8 & 9. Exact stock boundary succeeds; one-thousandth above available fails with no writes.
        [Fact]
        public void T08_T09_ExactStockBoundarySucceeds_AndOneMilliAboveFailsWithoutWrites()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Metformin 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "M1", 202812, 2000, 3000);

                var svc = db.CreateService();

                var overDraft = new InvoiceDraft();
                overDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 3001 });
                overDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 6002 });

                var overResult = svc.FinalizeInvoice(overDraft);
                Assert.False(overResult.Succeeded);
                Assert.Contains(overResult.Issues, i => i.Code == BillingIssueCodes.InsufficientStock);
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice;"));
                Assert.Equal(3000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));

                var exactDraft = new InvoiceDraft();
                exactDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 3000 });
                exactDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 6000 });

                var exactResult = svc.FinalizeInvoice(exactDraft);
                Assert.True(exactResult.Succeeded);
                Assert.Equal(0, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
            }
        }

        // 10. Duplicate medicine lines are merged safely.
        [Fact]
        public void T10_DuplicateMedicineLines_AreMergedSafely()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "ORS Powder", "sachet", 500, true);
                db.SeedBatch("b-1", "med-1", "ORS1", 202812, 2000, 3000);

                var svc = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 6000 });

                var preview = svc.PreviewInvoice(draft);
                Assert.Single(preview.Lines);
                Assert.Equal(3000, preview.Lines[0].QuantityMilli);
                Assert.True(preview.CanFinalize);

                var result = svc.FinalizeInvoice(draft);
                Assert.True(result.Succeeded);
                Assert.Equal(0, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
            }
        }

        // 11 & 12. Split tender exact match succeeds; one-paise overpayment and underpayment fail.
        [Fact]
        public void T11_T12_SplitTenderExactMatchSucceeds_OnePaiseMismatchFails()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Insulin Pen", "unit", 500, true);
                db.SeedBatch("b-1", "med-1", "INS1", 202812, 50000, 5000);

                var svc = db.CreateService();

                var underDraft = new InvoiceDraft();
                underDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                underDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 49999 });
                var underRes = svc.FinalizeInvoice(underDraft);
                Assert.False(underRes.Succeeded);
                Assert.Contains(underRes.Issues, i => i.Code == BillingIssueCodes.PaymentShort);

                var overDraft = new InvoiceDraft();
                overDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                overDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 50001 });
                var overRes = svc.FinalizeInvoice(overDraft);
                Assert.False(overRes.Succeeded);
                Assert.Contains(overRes.Issues, i => i.Code == BillingIssueCodes.PaymentOverpaid);
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice;"));

                var splitDraft = new InvoiceDraft();
                splitDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                splitDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 10000 });
                splitDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Upi, AmountPaise = 15000, ReferenceText = "UPI01" });
                splitDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.DebitCard, AmountPaise = 15000, ReferenceText = "POS01" });
                splitDraft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.CreditCard, AmountPaise = 10000, ReferenceText = "POS02" });

                var okRes = svc.FinalizeInvoice(splitDraft);
                Assert.True(okRes.Succeeded);
                Assert.Equal(4, db.QueryScalarInt64("SELECT COUNT(*) FROM payment WHERE invoice_id = @id;", p => p.AddWithValue("@id", okRes.InvoiceId)));
            }
        }

        // 13 & 14. Missing tax configuration fails before writes; explicit 0% differs from unconfigured 0%.
        [Fact]
        public void T13_T14_MissingTaxConfigFails_AndExplicitZeroDiffersFromUnconfiguredZero()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedMedicine("med-unconfigured", "Unconfigured Med", "strip", 0, false);
                db.SeedMedicine("med-zero-ok", "Explicit Zero Med", "strip", 0, true);
                db.SeedBatch("b-1", "med-unconfigured", "U1", 202812, 1000, 5000);
                db.SeedBatch("b-2", "med-zero-ok", "Z1", 202812, 1000, 5000);

                var svc = db.CreateService();

                // Shop tax setup missing
                var draftZero = new InvoiceDraft();
                draftZero.Items.Add(new InvoiceItemDraft { MedicineId = "med-zero-ok", QuantityMilli = 1000 });
                draftZero.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 1000 });
                var missingShopTax = svc.FinalizeInvoice(draftZero);
                Assert.False(missingShopTax.Succeeded);
                Assert.Contains(missingShopTax.Issues, i => i.Code == BillingIssueCodes.TaxConfigMissing);

                // Configure shop tax, test unconfigured 0% medicine -> must block
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                var draftUnconfigured = new InvoiceDraft();
                draftUnconfigured.Items.Add(new InvoiceItemDraft { MedicineId = "med-unconfigured", QuantityMilli = 1000 });
                draftUnconfigured.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 1000 });
                var unconfRes = svc.FinalizeInvoice(draftUnconfigured);
                Assert.False(unconfRes.Succeeded);
                Assert.Contains(unconfRes.Issues, i => i.Code == BillingIssueCodes.MedicineTaxNotConfigured);
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice;"));

                // Explicit 0% medicine -> must succeed
                var zeroRes = svc.FinalizeInvoice(draftZero);
                Assert.True(zeroRes.Succeeded);
                Assert.Equal(0, db.QueryScalarInt64("SELECT cgst_paise + sgst_paise + igst_paise FROM invoice WHERE invoice_id = @id;", p => p.AddWithValue("@id", zeroRes.InvoiceId)));
            }
        }

        // 15 & 16. Inclusive/exclusive tax arithmetic and intra-state CGST+SGST vs inter-state IGST reconciliation.
        [Fact]
        public void T15_T16_InclusiveExclusive_AndIntraInterState_Reconcile()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop(stateCode: "19");
                db.SeedTaxConfig(true, "EXCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Test Antibiotic", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "AB1", 202812, 10000, 10000);

                var svc = db.CreateService();

                // Exclusive Intra-State
                var intraDraft = new InvoiceDraft();
                intraDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                var intraPreview = svc.PreviewInvoice(intraDraft);
                Assert.Equal(10000, intraPreview.TaxableValuePaise);
                Assert.Equal(600, intraPreview.CgstPaise);
                Assert.Equal(600, intraPreview.SgstPaise);
                Assert.Equal(0, intraPreview.IgstPaise);
                Assert.Equal(11200, intraPreview.GrandTotalPaise);

                // Exclusive Inter-State via explicitly configured INTER_STATE default supply policy
                db.SeedTaxConfig(true, "EXCLUSIVE", "INTER_STATE", "NONE");
                var interDraft = new InvoiceDraft();
                interDraft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                var interPreview = svc.PreviewInvoice(interDraft);
                Assert.Equal(10000, interPreview.TaxableValuePaise);
                Assert.Equal(0, interPreview.CgstPaise);
                Assert.Equal(0, interPreview.SgstPaise);
                Assert.Equal(1200, interPreview.IgstPaise);
                Assert.Equal(11200, interPreview.GrandTotalPaise);

                // Inclusive mode
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                var inclPreview = svc.PreviewInvoice(intraDraft);
                Assert.Equal(10000, inclPreview.GrandTotalPaise);
                Assert.Equal(inclPreview.GrandTotalPaise, inclPreview.TaxableValuePaise + inclPreview.CgstPaise + inclPreview.SgstPaise + inclPreview.IgstPaise);
            }
        }

        // 17 & 18. Financial-year boundary increments and failed/cancelled attempt never reuses committed invoice numbers.
        [Fact]
        public void T17_T18_FinancialYearBoundaryAndMonotonicSequence()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop(fyStartMonth: 4);
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SeedMedicine("med-1", "Vitamin C", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "VC1", 202912, 1000, 10000);

                var clockMar31 = new FakeBillingClock(new DateTime(2026, 3, 31, 10, 0, 0, DateTimeKind.Utc), 330);
                var svcMar31 = db.CreateService(clockMar31);

                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 1000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 1000 });

                var inv1 = svcMar31.FinalizeInvoice(draft);
                Assert.True(inv1.Succeeded);
                Assert.Equal("INV/25-26/000001", inv1.InvoiceNumber);

                // Mark inv1 CANCELLED in DB and verify next invoice in same FY gets 000002 (never reuses 000001).
                db.ExecuteNonQuery("UPDATE invoice SET status = 'CANCELLED', cancellation_reason = 'Test' WHERE invoice_id = @id;", p => p.AddWithValue("@id", inv1.InvoiceId));

                var inv2 = svcMar31.FinalizeInvoice(draft);
                Assert.True(inv2.Succeeded);
                Assert.Equal("INV/25-26/000002", inv2.InvoiceNumber);

                // Cross into new FY (April 1, 2026) -> starts at 26-27/000001
                var clockApr1 = new FakeBillingClock(new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc), 330);
                var svcApr1 = db.CreateService(clockApr1);
                var inv3 = svcApr1.FinalizeInvoice(draft);
                Assert.True(inv3.Succeeded);
                Assert.Equal("INV/26-27/000001", inv3.InvoiceNumber);
            }
        }

        // 19. Injected failure after each critical insert/update rolls the whole transaction back.
        [Theory]
        [InlineData("after_sequence_increment")]
        [InlineData("after_invoice_insert")]
        [InlineData("after_invoice_line_insert")]
        [InlineData("after_batch_deduction")]
        [InlineData("after_stock_movement_insert")]
        [InlineData("after_payment_insert")]
        [InlineData("after_audit_log_insert")]
        [InlineData("after_snapshot_dirty_update")]
        public void T19_InjectedFailureAtEveryCheckpoint_RollsBackAtomically(string failAtCheckpoint)
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SetSetting("stock_snapshot_dirty", "0");
                db.SeedMedicine("med-1", "Paracetamol 500mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "P101", 202812, 2500, 10000);

                BillingWriteCheckpoint hook = name =>
                {
                    if (string.Equals(name, failAtCheckpoint, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Simulated crash at " + name);
                    }
                };

                var svc = db.CreateService(checkpoint: hook);
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 5000 });

                Assert.Throws<InvalidOperationException>(() => svc.FinalizeInvoice(draft));

                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice_line;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM payment;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM stock_movement;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM audit_log;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice_sequence;"));
                Assert.Equal(10000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal("0", db.GetSetting("stock_snapshot_dirty"));
            }
        }

        // 20, 21, 22, 23. Timestamps, immutable snapshots after master-data edits, movement/payment/audit parity, stock_snapshot_dirty=1.
        [Fact]
        public void T20_T21_T22_T23_TimestampsSnapshotsLedgerParityAndSnapshotDirty()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SetSetting("stock_snapshot_dirty", "0");
                db.SeedMedicine("med-1", "Original Name 500mg", "strip", 1200, true, "30049099");
                db.SeedBatch("b-1", "med-1", "ORIG-B1", 202812, 3000, 10000);

                var fixedUtc = new DateTime(2026, 11, 18, 12, 30, 0, DateTimeKind.Utc);
                var clock = new FakeBillingClock(fixedUtc, 330);
                var svc = db.CreateService(clock);

                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 2000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 6000 });

                var result = svc.FinalizeInvoice(draft);
                Assert.True(result.Succeeded);
                Assert.Equal(fixedUtc, result.IssuedAtUtc);
                Assert.Equal(330, result.IssuedLocalOffsetMinutes);
                Assert.Equal("1", db.GetSetting("stock_snapshot_dirty"));

                // Verify stock_movement, payment, and audit_log parity
                Assert.Equal(-2000, db.QueryScalarInt64("SELECT quantity_delta_milli FROM stock_movement WHERE reference_id = @id AND movement_type = 'SALE';", p => p.AddWithValue("@id", result.InvoiceId)));
                Assert.Equal(6000, db.QueryScalarInt64("SELECT SUM(amount_paise) FROM payment WHERE invoice_id = @id;", p => p.AddWithValue("@id", result.InvoiceId)));
                Assert.Equal(1, db.QueryScalarInt64("SELECT COUNT(*) FROM audit_log WHERE entity_type = 'INVOICE' AND entity_id = @id AND action_type = 'INVOICE_ISSUED';", p => p.AddWithValue("@id", result.InvoiceId)));

                // Mutate master data after sale -> invoice_line snapshot must remain immutable
                db.ExecuteNonQuery("UPDATE medicine SET display_name = 'Renamed Med', gst_rate_basis_points = 1800, hsn_code = '99999999' WHERE medicine_id = 'med-1';");
                db.ExecuteNonQuery("UPDATE stock_batch SET batch_number = 'RENAMED-B1', mrp_paise = 9999 WHERE batch_id = 'b-1';");

                Assert.Equal("Original Name 500mg", db.QueryScalarString("SELECT medicine_name_snapshot FROM invoice_line WHERE invoice_id = @id;", p => p.AddWithValue("@id", result.InvoiceId)));
                Assert.Equal("ORIG-B1", db.QueryScalarString("SELECT batch_number_snapshot FROM invoice_line WHERE invoice_id = @id;", p => p.AddWithValue("@id", result.InvoiceId)));
                Assert.Equal(3000, db.QueryScalarInt64("SELECT unit_price_paise FROM invoice_line WHERE invoice_id = @id;", p => p.AddWithValue("@id", result.InvoiceId)));
                Assert.Equal(1200, db.QueryScalarInt64("SELECT gst_rate_basis_points FROM invoice_line WHERE invoice_id = @id;", p => p.AddWithValue("@id", result.InvoiceId)));
                Assert.Equal("30049099", db.QueryScalarString("SELECT hsn_code_snapshot FROM invoice_line WHERE invoice_id = @id;", p => p.AddWithValue("@id", result.InvoiceId)));
            }
        }

        // 24. PreviewInvoice performs zero writes and returns totals identical to FinalizeInvoice.
        [Fact]
        public void T24_PreviewInvoice_PerformsZeroWrites_AndMatchesFinalizeTotals()
        {
            using (var db = new BillingTestDatabase())
            {
                db.SeedDefaultShop();
                db.SeedTaxConfig(true, "INCLUSIVE", "INTRA_STATE", "NONE");
                db.SetSetting("stock_snapshot_dirty", "0");
                db.SeedMedicine("med-1", "Atorvastatin 10mg", "strip", 1200, true);
                db.SeedBatch("b-1", "med-1", "AT1", 202705, 4500, 2000);
                db.SeedBatch("b-2", "med-1", "AT2", 202805, 5000, 5000);

                var svc = db.CreateService();
                var draft = new InvoiceDraft();
                draft.Items.Add(new InvoiceItemDraft { MedicineId = "med-1", QuantityMilli = 3000 });
                draft.Tenders.Add(new PaymentTenderDraft { Method = InvoicePaymentMethod.Cash, AmountPaise = 14000 });

                var preview = svc.PreviewInvoice(draft);
                Assert.True(preview.CanFinalize);
                Assert.Equal(14000, preview.GrandTotalPaise);

                // Verify zero writes after PreviewInvoice
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM invoice_sequence;"));
                Assert.Equal(0, db.QueryScalarInt64("SELECT COUNT(*) FROM stock_movement;"));
                Assert.Equal(2000, db.QueryScalarInt64("SELECT quantity_milli FROM stock_batch WHERE batch_id = 'b-1';"));
                Assert.Equal("0", db.GetSetting("stock_snapshot_dirty"));

                var finalized = svc.FinalizeInvoice(draft);
                Assert.True(finalized.Succeeded);
                Assert.Equal(preview.GrandTotalPaise, finalized.GrandTotalPaise);
                Assert.Equal(preview.TaxableValuePaise, db.QueryScalarInt64("SELECT taxable_value_paise FROM invoice WHERE invoice_id = @id;", p => p.AddWithValue("@id", finalized.InvoiceId)));
                Assert.Equal(preview.CgstPaise, db.QueryScalarInt64("SELECT cgst_paise FROM invoice WHERE invoice_id = @id;", p => p.AddWithValue("@id", finalized.InvoiceId)));
                Assert.Equal(preview.SgstPaise, db.QueryScalarInt64("SELECT sgst_paise FROM invoice WHERE invoice_id = @id;", p => p.AddWithValue("@id", finalized.InvoiceId)));
                Assert.Equal(preview.IgstPaise, db.QueryScalarInt64("SELECT igst_paise FROM invoice WHERE invoice_id = @id;", p => p.AddWithValue("@id", finalized.InvoiceId)));
            }
        }
    }
}