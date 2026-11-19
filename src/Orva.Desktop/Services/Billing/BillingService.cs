using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using Orva.Domain.Billing;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;

namespace Orva.Services.Billing
{
    /// <summary>
    /// Production implementation of <see cref="IBillingService"/>.
    /// Enforces FEFO allocation, explicit tax configuration safeguards, exact integer paise
    /// accounting, payment reconciliation, and atomic SQLite invoice persistence.
    /// </summary>
    public sealed class BillingService : IBillingService
    {
        private readonly BillingRepository _repository;
        private readonly FefoAllocator _allocator;
        private readonly InvoiceCalculator _calculator;
        private readonly StockSnapshotWriter _snapshotWriter;
        private readonly IBillingClock _clock;
        private readonly BillingWriteCheckpoint _writeCheckpoint;

        public BillingService(
            SqliteDatabase database,
            StockSnapshotWriter snapshotWriter = null,
            IBillingClock clock = null)
            : this(new BillingRepository(database), snapshotWriter, clock, null)
        {
        }

        internal BillingService(
            BillingRepository repository,
            StockSnapshotWriter snapshotWriter,
            IBillingClock clock,
            BillingWriteCheckpoint writeCheckpoint)
        {
            if (repository == null) throw new ArgumentNullException("repository");
            _repository = repository;
            _allocator = new FefoAllocator();
            _calculator = new InvoiceCalculator();
            _snapshotWriter = snapshotWriter;
            _clock = clock ?? new SystemBillingClock();
            _writeCheckpoint = writeCheckpoint;
        }

        public IList<MedicineLookupResult> SearchMedicines(string query, int maximumResults)
        {
            if (maximumResults <= 0)
            {
                return new List<MedicineLookupResult>();
            }

            return _repository.SearchMedicines(query, maximumResults, _clock.CurrentExpiryYearMonth);
        }

        public InvoicePreviewResult PreviewInvoice(InvoiceDraft draft)
        {
            var mergedItems = new List<MergedDraftItem>();
            var issues = new List<BillingIssue>();
            var validatedTenders = new List<ValidatedTender>();
            long tenderTotalPaise = 0;

            ValidateAndMergeDraft(draft, mergedItems, validatedTenders, out tenderTotalPaise, issues);

            var medicineIds = CollectMedicineIds(mergedItems);
            int currentMonth = _clock.CurrentExpiryYearMonth;
            var snapshot = _repository.LoadReadOnlySnapshot(medicineIds, currentMonth);

            var evaluation = EvaluateAgainstSnapshot(
                draft,
                mergedItems,
                validatedTenders,
                tenderTotalPaise,
                snapshot,
                currentMonth,
                issues,
                requireExactPaymentForIssues: false);

            return BuildPreviewResult(evaluation);
        }

        public InvoiceResult FinalizeInvoice(InvoiceDraft draft)
        {
            var mergedItems = new List<MergedDraftItem>();
            var preIssues = new List<BillingIssue>();
            var validatedTenders = new List<ValidatedTender>();
            long tenderTotalPaise = 0;

            ValidateAndMergeDraft(draft, mergedItems, validatedTenders, out tenderTotalPaise, preIssues);

            if (mergedItems.Count == 0 || HasBlockingIssues(preIssues))
            {
                return FailedInvoiceResult(preIssues);
            }

            var medicineIds = CollectMedicineIds(mergedItems);
            CommittedInvoiceRecord committed = null;

            using (var connection = _repository.OpenWriteConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                BillingInstant now = BillingInstant.Capture(_clock);
                DateTime utcNow = now.UtcNow;
                int localOffsetMinutes = now.LocalOffsetMinutes;
                DateTime localNow = now.LocalNow;
                int currentMonth = localNow.Year * 100 + localNow.Month;

                var freshIssues = new List<BillingIssue>();
                var snapshot = _repository.LoadSnapshot(connection, transaction, medicineIds, currentMonth);

                var evaluation = EvaluateAgainstSnapshot(
                    draft,
                    mergedItems,
                    validatedTenders,
                    tenderTotalPaise,
                    snapshot,
                    currentMonth,
                    freshIssues,
                    requireExactPaymentForIssues: true);

                if (!evaluation.CanFinalize || evaluation.Calculated == null || evaluation.TaxPolicy == null)
                {
                    transaction.Rollback();
                    return FailedInvoiceResult(evaluation.Issues);
                }

                committed = _repository.PersistInvoiceInTransaction(
                    connection,
                    transaction,
                    snapshot.Shop,
                    evaluation.TaxPolicy,
                    evaluation.Calculated,
                    draft != null ? draft.Customer : null,
                    validatedTenders,
                    utcNow,
                    localOffsetMinutes,
                    localNow,
                    currentMonth,
                    _writeCheckpoint);

                transaction.Commit();
            }

            var result = new InvoiceResult
            {
                Succeeded = true,
                InvoiceId = committed.InvoiceId,
                InvoiceNumber = committed.InvoiceNumber,
                IssuedAtUtc = committed.IssuedAtUtc,
                IssuedLocalOffsetMinutes = committed.IssuedLocalOffsetMinutes,
                GrandTotalPaise = committed.GrandTotalPaise
            };

            TryRefreshStockSnapshot(result);
            return result;
        }

        private void TryRefreshStockSnapshot(InvoiceResult result)
        {
            if (_snapshotWriter == null) return;

            try
            {
                bool snapshotClean = _snapshotWriter.WriteCurrentSnapshot();
                if (!snapshotClean)
                {
                    result.Warnings.Add("Invoice was saved, but newer stock changes occurred while refreshing StockData.json. The portable stock file remains queued for refresh.");
                }
            }
            catch (Exception ex)
            {
                // The invoice transaction is already committed and stock_snapshot_dirty is already 1.
                // Never turn a committed invoice into a reported failure.
                Trace.TraceError("Stock snapshot refresh failed after committed invoice " + result.InvoiceNumber + ": " + ex);
                result.Warnings.Add("Invoice was saved, but the portable stock file could not be refreshed. Use Refresh stock file in Inventory & Stock.");
            }
        }

        private DraftEvaluation EvaluateAgainstSnapshot(
            InvoiceDraft draft,
            IList<MergedDraftItem> mergedItems,
            IList<ValidatedTender> validatedTenders,
            long tenderTotalPaise,
            BillingDataSnapshot snapshot,
            int currentExpiryYearMonth,
            List<BillingIssue> issues,
            bool requireExactPaymentForIssues)
        {
            var evaluation = new DraftEvaluation
            {
                Issues = issues,
                TenderTotalPaise = tenderTotalPaise
            };

            if (snapshot.Shop == null)
            {
                AddBlockingIssue(issues, BillingIssueCodes.ShopProfileMissing, "Shop profile setup must be completed before billing.", null);
            }

            ResolvedTaxPolicy taxPolicy = ResolveTaxPolicy(snapshot.Shop, snapshot.TaxSettings, draft != null ? draft.Customer : null, issues);
            evaluation.TaxPolicy = taxPolicy;

            var lineInputs = new List<InvoiceLineCalculationInput>();
            for (int i = 0; i < mergedItems.Count; i++)
            {
                var item = mergedItems[i];
                BillingMedicineRecord medicine;
                if (!snapshot.Medicines.TryGetValue(item.MedicineId, out medicine) || medicine == null)
                {
                    AddBlockingIssue(issues, BillingIssueCodes.MedicineNotFound, "One of the selected medicines could not be found.", item.MedicineId);
                    continue;
                }

                if (!medicine.IsActive)
                {
                    AddBlockingIssue(issues, BillingIssueCodes.MedicineInactive, medicine.DisplayName + " is no longer active for sale.", medicine.MedicineId);
                    continue;
                }

                if (!medicine.GstRateConfigured)
                {
                    AddBlockingIssue(
                        issues,
                        BillingIssueCodes.MedicineTaxNotConfigured,
                        "Tax setup needs to be completed for " + medicine.DisplayName + " in Settings. Please contact support.",
                        medicine.MedicineId);
                }

                List<BillingBatchRecord> batches;
                snapshot.EligibleBatchesByMedicine.TryGetValue(item.MedicineId, out batches);

                FefoAllocationOutcome allocationOutcome;
                try
                {
                    allocationOutcome = _allocator.Allocate(batches, item.QuantityMilli, currentExpiryYearMonth);
                }
                catch (OverflowException)
                {
                    AddBlockingIssue(issues, BillingIssueCodes.QuantityOverflow, "Requested quantity for " + medicine.DisplayName + " is too large.", medicine.MedicineId);
                    continue;
                }

                if (!allocationOutcome.Succeeded)
                {
                    string availabilityMessage = allocationOutcome.AvailableQuantityMilli <= 0
                        ? medicine.DisplayName + " is currently out of stock."
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "Only {0} {1} of {2} available.",
                            InvoiceCalculator.FormatQuantity(allocationOutcome.AvailableQuantityMilli),
                            medicine.StockUnitName,
                            medicine.DisplayName);

                    AddBlockingIssue(issues, BillingIssueCodes.InsufficientStock, availabilityMessage, medicine.MedicineId);
                    continue;
                }

                lineInputs.Add(new InvoiceLineCalculationInput
                {
                    Medicine = medicine,
                    RequestedQuantityMilli = item.QuantityMilli,
                    Allocations = allocationOutcome.Allocations
                });
            }

            if (lineInputs.Count > 0 && taxPolicy != null)
            {
                try
                {
                    evaluation.Calculated = _calculator.Calculate(lineInputs, taxPolicy);
                }
                catch (OverflowException)
                {
                    AddBlockingIssue(issues, BillingIssueCodes.CalculationOverflow, "Invoice amount exceeds supported numeric limits.", null);
                }
            }

            long grandTotal = evaluation.Calculated != null ? evaluation.Calculated.GrandTotalPaise : 0L;
            long remaining = checked(grandTotal - tenderTotalPaise);
            evaluation.AmountRemainingPaise = remaining;

            if (evaluation.Calculated != null)
            {
                if (tenderTotalPaise > grandTotal)
                {
                    long overpaid = checked(tenderTotalPaise - grandTotal);
                    AddBlockingIssue(
                        issues,
                        BillingIssueCodes.PaymentOverpaid,
                        "Payment exceeds the invoice total by Rs. " + InvoiceCalculator.FormatRupees(overpaid) + ".",
                        null);
                }
                else if (requireExactPaymentForIssues && tenderTotalPaise < grandTotal)
                {
                    long shortBy = checked(grandTotal - tenderTotalPaise);
                    AddBlockingIssue(
                        issues,
                        BillingIssueCodes.PaymentShort,
                        "Payment is short by Rs. " + InvoiceCalculator.FormatRupees(shortBy) + ".",
                        null);
                }
            }

            bool hasBlocking = HasBlockingIssues(issues);
            bool hasLines = mergedItems.Count > 0 && evaluation.Calculated != null && evaluation.Calculated.Lines.Count == mergedItems.Count;
            bool isFullyPaid = hasLines && remaining == 0 && (grandTotal == 0 || validatedTenders.Count > 0);

            evaluation.IsDraftValid = hasLines && !hasBlocking;
            evaluation.IsFullyPaid = isFullyPaid;
            evaluation.CanFinalize = evaluation.IsDraftValid && isFullyPaid;

            return evaluation;
        }

        private static ResolvedTaxPolicy ResolveTaxPolicy(
            BillingShopContext shop,
            BillingTaxSettings settings,
            InvoiceCustomerDraft customer,
            List<BillingIssue> issues)
        {
            const string taxSetupMessage = "Tax setup needs to be completed in Settings. Please contact support.";

            if (settings == null || !settings.IsConfigured)
            {
                AddBlockingIssue(issues, BillingIssueCodes.TaxConfigMissing, taxSetupMessage, null);
                return null;
            }

            TaxPricingMode pricingMode;
            if (!TryParsePricingMode(settings.PricingModeRaw, out pricingMode))
            {
                AddBlockingIssue(issues, BillingIssueCodes.TaxConfigMissing, taxSetupMessage, null);
                return null;
            }

            RoundOffMode roundOffMode;
            if (!TryParseRoundOffMode(settings.RoundOffModeRaw, out roundOffMode))
            {
                AddBlockingIssue(issues, BillingIssueCodes.TaxConfigMissing, taxSetupMessage, null);
                return null;
            }

            string shopStateCode = shop != null && !string.IsNullOrWhiteSpace(shop.StateCode)
                ? shop.StateCode.Trim()
                : null;

            if (!IndianGstStateCatalog.IsValidShopStateCode(shopStateCode))
            {
                AddBlockingIssue(
                    issues,
                    BillingIssueCodes.ShopStateCodeMissing,
                    "Shop State and 2-digit State Code must be configured in Settings before billing.",
                    null);
                return null;
            }

            if (shop != null && !InvoiceCalculator.IsValidInvoicePrefix(shop.InvoicePrefix))
            {
                AddBlockingIssue(
                    issues,
                    BillingIssueCodes.InvoicePrefixInvalid,
                    "Invoice prefix in Settings must be 1 to 3 characters to stay within the 16-character GST invoice number limit.",
                    null);
                return null;
            }

            string customerGstinRaw = customer != null && !string.IsNullOrWhiteSpace(customer.Gstin)
                ? customer.Gstin.Trim().ToUpperInvariant()
                : null;

            SupplyType supplyType;
            string placeOfSupplyStateCode;

            if (customerGstinRaw != null)
            {
                var gstinValidation = GstinValidator.Validate(customerGstinRaw);
                if (!gstinValidation.IsValid)
                {
                    AddBlockingIssue(
                        issues,
                        BillingIssueCodes.CustomerGstinInvalid,
                        gstinValidation.ErrorMessage,
                        null);
                    return null;
                }

                string customerStateCode = gstinValidation.StateCode;
                if (string.Equals(customerStateCode, shopStateCode, StringComparison.Ordinal))
                {
                    placeOfSupplyStateCode = shopStateCode;
                    supplyType = SupplyType.IntraState;
                }
                else
                {
                    // Do not silently infer inter-state place-of-supply from the customer's GSTIN prefix alone.
                    // Over-the-counter pickup vs. inter-state delivery requires explicit CA-approved policy / place-of-supply input.
                    AddBlockingIssue(
                        issues,
                        BillingIssueCodes.PlaceOfSupplyRequiresReview,
                        "Customer GSTIN is registered in another state (" + customerStateCode + "). Inter-state place of supply cannot be assumed automatically; confirm tax treatment with your CA.",
                        null);
                    return null;
                }
            }
            else
            {
                if (!TryParseSupplyType(settings.DefaultSupplyTypeRaw, out supplyType))
                {
                    AddBlockingIssue(issues, BillingIssueCodes.TaxConfigMissing, taxSetupMessage, null);
                    return null;
                }
                placeOfSupplyStateCode = shopStateCode;
            }

            return new ResolvedTaxPolicy
            {
                PricingMode = pricingMode,
                SupplyType = supplyType,
                RoundOffMode = roundOffMode,
                PlaceOfSupplyStateCode = placeOfSupplyStateCode
            };
        }

        private static void ValidateAndMergeDraft(
            InvoiceDraft draft,
            List<MergedDraftItem> mergedItems,
            List<ValidatedTender> validatedTenders,
            out long tenderTotalPaise,
            List<BillingIssue> issues)
        {
            tenderTotalPaise = 0;
            if (draft == null || draft.Items == null || draft.Items.Count == 0)
            {
                AddBlockingIssue(issues, BillingIssueCodes.DraftEmpty, "Add at least one medicine to create an invoice.", null);
            }
            else
            {
                var indexByMedicineId = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < draft.Items.Count; i++)
                {
                    var item = draft.Items[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.MedicineId))
                    {
                        AddBlockingIssue(issues, BillingIssueCodes.InvalidItem, "Each invoice line must reference a valid medicine.", null);
                        continue;
                    }

                    string medicineId = item.MedicineId.Trim();
                    if (item.QuantityMilli <= 0)
                    {
                        AddBlockingIssue(issues, BillingIssueCodes.InvalidQuantity, "Enter a quantity greater than zero.", medicineId);
                        continue;
                    }

                    int existingIndex;
                    if (indexByMedicineId.TryGetValue(medicineId, out existingIndex))
                    {
                        try
                        {
                            mergedItems[existingIndex].QuantityMilli = checked(mergedItems[existingIndex].QuantityMilli + item.QuantityMilli);
                        }
                        catch (OverflowException)
                        {
                            AddBlockingIssue(issues, BillingIssueCodes.QuantityOverflow, "Combined quantity for this medicine is too large.", medicineId);
                        }
                    }
                    else
                    {
                        indexByMedicineId.Add(medicineId, mergedItems.Count);
                        mergedItems.Add(new MergedDraftItem
                        {
                            MedicineId = medicineId,
                            QuantityMilli = item.QuantityMilli
                        });
                    }
                }
            }

            if (draft != null && draft.Tenders != null)
            {
                for (int i = 0; i < draft.Tenders.Count; i++)
                {
                    var tender = draft.Tenders[i];
                    if (tender == null)
                    {
                        AddBlockingIssue(issues, BillingIssueCodes.InvalidPaymentAmount, "Remove empty payment entries before finalising.", null);
                        continue;
                    }

                    string dbMethod;
                    if (!TryMapPaymentMethod(tender.Method, out dbMethod))
                    {
                        AddBlockingIssue(issues, BillingIssueCodes.InvalidPaymentMethod, "Choose an approved payment method (Cash, UPI, Debit Card, or Credit Card).", null);
                        continue;
                    }

                    if (tender.AmountPaise <= 0)
                    {
                        AddBlockingIssue(issues, BillingIssueCodes.InvalidPaymentAmount, "Enter a payment amount greater than zero.", null);
                        continue;
                    }

                    try
                    {
                        tenderTotalPaise = checked(tenderTotalPaise + tender.AmountPaise);
                    }
                    catch (OverflowException)
                    {
                        AddBlockingIssue(issues, BillingIssueCodes.CalculationOverflow, "Payment total exceeds supported numeric limits.", null);
                        continue;
                    }

                    validatedTenders.Add(new ValidatedTender
                    {
                        Method = tender.Method,
                        DatabaseMethod = dbMethod,
                        AmountPaise = tender.AmountPaise,
                        ReferenceText = string.IsNullOrWhiteSpace(tender.ReferenceText) ? null : tender.ReferenceText.Trim()
                    });
                }
            }
        }

        private static InvoicePreviewResult BuildPreviewResult(DraftEvaluation evaluation)
        {
            var preview = new InvoicePreviewResult
            {
                IsDraftValid = evaluation.IsDraftValid,
                CanFinalize = evaluation.CanFinalize,
                TenderTotalPaise = evaluation.TenderTotalPaise,
                AmountRemainingPaise = evaluation.AmountRemainingPaise,
                IsFullyPaid = evaluation.IsFullyPaid
            };

            preview.Issues.AddRange(evaluation.Issues);

            if (evaluation.Calculated != null)
            {
                preview.SubtotalPaise = evaluation.Calculated.SubtotalPaise;
                preview.DiscountPaise = evaluation.Calculated.DiscountPaise;
                preview.TaxableValuePaise = evaluation.Calculated.TaxableValuePaise;
                preview.CgstPaise = evaluation.Calculated.CgstPaise;
                preview.SgstPaise = evaluation.Calculated.SgstPaise;
                preview.IgstPaise = evaluation.Calculated.IgstPaise;
                preview.RoundOffPaise = evaluation.Calculated.RoundOffPaise;
                preview.GrandTotalPaise = evaluation.Calculated.GrandTotalPaise;

                for (int i = 0; i < evaluation.Calculated.Lines.Count; i++)
                {
                    var srcLine = evaluation.Calculated.Lines[i];
                    var dstLine = new InvoiceLinePreview
                    {
                        MedicineId = srcLine.MedicineId,
                        MedicineName = srcLine.MedicineName,
                        QuantityMilli = srcLine.QuantityMilli,
                        StockUnitName = srcLine.StockUnitName,
                        SubtotalPaise = srcLine.SubtotalPaise,
                        TaxableValuePaise = srcLine.TaxableValuePaise,
                        CgstPaise = srcLine.CgstPaise,
                        SgstPaise = srcLine.SgstPaise,
                        IgstPaise = srcLine.IgstPaise,
                        LineTotalPaise = srcLine.LineTotalPaise
                    };

                    for (int j = 0; j < srcLine.BatchLines.Count; j++)
                    {
                        var srcBatch = srcLine.BatchLines[j];
                        dstLine.BatchAllocations.Add(new BatchAllocationPreview
                        {
                            BatchNumber = srcBatch.BatchNumber,
                            ExpiryYyyyMm = srcBatch.ExpiryYyyyMm,
                            QuantityMilli = srcBatch.QuantityMilli,
                            UnitPricePaise = srcBatch.UnitPricePaise,
                            SubtotalPaise = srcBatch.SubtotalPaise,
                            TaxableValuePaise = srcBatch.TaxableValuePaise,
                            CgstPaise = srcBatch.CgstPaise,
                            SgstPaise = srcBatch.SgstPaise,
                            IgstPaise = srcBatch.IgstPaise,
                            LineTotalPaise = srcBatch.LineTotalPaise
                        });
                    }

                    preview.Lines.Add(dstLine);
                }
            }

            return preview;
        }

        private static InvoiceResult FailedInvoiceResult(IList<BillingIssue> issues)
        {
            var result = new InvoiceResult { Succeeded = false };
            if (issues != null)
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    result.Issues.Add(issues[i]);
                }
            }
            return result;
        }

        private static List<string> CollectMedicineIds(IList<MergedDraftItem> mergedItems)
        {
            var ids = new List<string>(mergedItems.Count);
            for (int i = 0; i < mergedItems.Count; i++)
            {
                ids.Add(mergedItems[i].MedicineId);
            }
            return ids;
        }

        private static bool TryMapPaymentMethod(InvoicePaymentMethod method, out string databaseValue)
        {
            switch (method)
            {
                case InvoicePaymentMethod.Cash:
                    databaseValue = "CASH";
                    return true;
                case InvoicePaymentMethod.Upi:
                    databaseValue = "UPI";
                    return true;
                case InvoicePaymentMethod.DebitCard:
                    databaseValue = "DEBIT_CARD";
                    return true;
                case InvoicePaymentMethod.CreditCard:
                    databaseValue = "CREDIT_CARD";
                    return true;
                default:
                    databaseValue = null;
                    return false;
            }
        }

        private static bool TryParsePricingMode(string raw, out TaxPricingMode mode)
        {
            if (string.Equals(raw, "INCLUSIVE", StringComparison.OrdinalIgnoreCase))
            {
                mode = TaxPricingMode.TaxInclusive;
                return true;
            }
            if (string.Equals(raw, "EXCLUSIVE", StringComparison.OrdinalIgnoreCase))
            {
                mode = TaxPricingMode.TaxExclusive;
                return true;
            }
            mode = TaxPricingMode.TaxInclusive;
            return false;
        }

        private static bool TryParseSupplyType(string raw, out SupplyType supplyType)
        {
            if (string.Equals(raw, "INTRA_STATE", StringComparison.OrdinalIgnoreCase))
            {
                supplyType = SupplyType.IntraState;
                return true;
            }
            if (string.Equals(raw, "INTER_STATE", StringComparison.OrdinalIgnoreCase))
            {
                supplyType = SupplyType.InterState;
                return true;
            }
            supplyType = SupplyType.IntraState;
            return false;
        }

        private static bool TryParseRoundOffMode(string raw, out RoundOffMode mode)
        {
            if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                mode = RoundOffMode.None;
                return true;
            }
            if (string.Equals(raw, "NEAREST_RUPEE", StringComparison.OrdinalIgnoreCase))
            {
                mode = RoundOffMode.NearestRupee;
                return true;
            }
            mode = RoundOffMode.None;
            return false;
        }

        private static bool HasBlockingIssues(IList<BillingIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == BillingIssueSeverity.Blocking) return true;
            }
            return false;
        }

        private static void AddBlockingIssue(List<BillingIssue> issues, string code, string message, string medicineId)
        {
            issues.Add(new BillingIssue
            {
                Severity = BillingIssueSeverity.Blocking,
                Code = code,
                Message = message,
                MedicineId = medicineId
            });
        }

        private sealed class MergedDraftItem
        {
            public string MedicineId { get; set; }
            public long QuantityMilli { get; set; }
        }

        private sealed class DraftEvaluation
        {
            public List<BillingIssue> Issues { get; set; }
            public ResolvedTaxPolicy TaxPolicy { get; set; }
            public CalculatedInvoice Calculated { get; set; }
            public long TenderTotalPaise { get; set; }
            public long AmountRemainingPaise { get; set; }
            public bool IsDraftValid { get; set; }
            public bool IsFullyPaid { get; set; }
            public bool CanFinalize { get; set; }
        }
    }
}