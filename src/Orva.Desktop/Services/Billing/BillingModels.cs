using System;
using System.Collections.Generic;
using Orva.Domain.Billing;

namespace Orva.Services.Billing
{
    public enum TaxPricingMode
    {
        TaxInclusive = 1,
        TaxExclusive = 2
    }

    public enum SupplyType
    {
        IntraState = 1,
        InterState = 2
    }

    public enum RoundOffMode
    {
        None = 0,
        NearestRupee = 1
    }

    public static class BillingSettingKeys
    {
        public const string TaxConfigured = "billing.tax_configured";
        public const string TaxPricingMode = "billing.tax_pricing_mode";
        public const string DefaultSupplyType = "billing.default_supply_type";
        public const string RoundOffMode = "billing.round_off_mode";
    }

    public static class BillingIssueCodes
    {
        public const string DraftEmpty = "DRAFT_EMPTY";
        public const string InvalidItem = "INVALID_ITEM";
        public const string InvalidQuantity = "INVALID_QUANTITY";
        public const string QuantityOverflow = "QUANTITY_OVERFLOW";
        public const string MedicineNotFound = "MEDICINE_NOT_FOUND";
        public const string MedicineInactive = "MEDICINE_INACTIVE";
        public const string InsufficientStock = "INSUFFICIENT_STOCK";
        public const string ShopProfileMissing = "SHOP_PROFILE_MISSING";
        public const string TaxConfigMissing = "TAX_CONFIG_MISSING";
        public const string MedicineTaxNotConfigured = "MEDICINE_TAX_NOT_CONFIGURED";
        public const string CustomerGstinInvalid = "CUSTOMER_GSTIN_INVALID";
        public const string InvalidPaymentMethod = "INVALID_PAYMENT_METHOD";
        public const string InvalidPaymentAmount = "INVALID_PAYMENT_AMOUNT";
        public const string PaymentShort = "PAYMENT_SHORT";
        public const string PaymentOverpaid = "PAYMENT_OVERPAID";
        public const string CalculationOverflow = "CALCULATION_OVERFLOW";
        public const string ShopStateCodeMissing = "SHOP_STATE_CODE_MISSING";
        public const string PlaceOfSupplyRequiresReview = "PLACE_OF_SUPPLY_REQUIRES_REVIEW";
        public const string InvoicePrefixInvalid = "INVOICE_PREFIX_INVALID";
    }

    public sealed class BillingShopContext
    {
        public string ShopId { get; set; }
        public string ShopName { get; set; }
        public string StateCode { get; set; }
        public string Gstin { get; set; }
        public string InvoicePrefix { get; set; }
        public int FinancialYearStartMonth { get; set; }
    }

    public sealed class BillingTaxSettings
    {
        public bool IsConfigured { get; set; }
        public string PricingModeRaw { get; set; }
        public string DefaultSupplyTypeRaw { get; set; }
        public string RoundOffModeRaw { get; set; }
    }

    public sealed class ResolvedTaxPolicy
    {
        public TaxPricingMode PricingMode { get; set; }
        public SupplyType SupplyType { get; set; }
        public RoundOffMode RoundOffMode { get; set; }
        public string PlaceOfSupplyStateCode { get; set; }
    }

    public sealed class BillingMedicineRecord
    {
        public string MedicineId { get; set; }
        public string DisplayName { get; set; }
        public string GenericName { get; set; }
        public string BrandName { get; set; }
        public string Strength { get; set; }
        public string DosageForm { get; set; }
        public string StockUnitName { get; set; }
        public string HsnCode { get; set; }
        public int GstRateBasisPoints { get; set; }
        public bool GstRateConfigured { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class BillingBatchRecord
    {
        public string BatchId { get; set; }
        public string MedicineId { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYyyyMm { get; set; }
        public string StockUnitName { get; set; }
        public long MrpPaise { get; set; }
        public long QuantityMilli { get; set; }
        public string BatchStatus { get; set; }
    }

    public sealed class BillingDataSnapshot
    {
        public BillingDataSnapshot()
        {
            Medicines = new Dictionary<string, BillingMedicineRecord>(StringComparer.Ordinal);
            EligibleBatchesByMedicine = new Dictionary<string, List<BillingBatchRecord>>(StringComparer.Ordinal);
        }

        public BillingShopContext Shop { get; set; }
        public BillingTaxSettings TaxSettings { get; set; }
        public Dictionary<string, BillingMedicineRecord> Medicines { get; private set; }
        public Dictionary<string, List<BillingBatchRecord>> EligibleBatchesByMedicine { get; private set; }
    }

    public sealed class FefoBatchAllocation
    {
        public string BatchId { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYyyyMm { get; set; }
        public long QuantityMilli { get; set; }
        public long UnitPricePaise { get; set; }
        public string StockUnitName { get; set; }
        public long PreviousQuantityMilli { get; set; }
    }

    public sealed class FefoAllocationOutcome
    {
        public FefoAllocationOutcome()
        {
            Allocations = new List<FefoBatchAllocation>();
        }

        public bool Succeeded { get; set; }
        public long RequestedQuantityMilli { get; set; }
        public long AvailableQuantityMilli { get; set; }
        public List<FefoBatchAllocation> Allocations { get; private set; }
    }

    public sealed class CalculatedBatchLine
    {
        public string BatchId { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYyyyMm { get; set; }
        public long QuantityMilli { get; set; }
        public string StockUnitName { get; set; }
        public long UnitPricePaise { get; set; }
        public int GstRateBasisPoints { get; set; }
        public string HsnCode { get; set; }
        public long PreviousQuantityMilli { get; set; }
        public long SubtotalPaise { get; set; }
        public long DiscountPaise { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long LineTotalPaise { get; set; }
    }

    public sealed class CalculatedMedicineLine
    {
        public CalculatedMedicineLine()
        {
            BatchLines = new List<CalculatedBatchLine>();
        }

        public string LineGroupId { get; set; }
        public string MedicineId { get; set; }
        public string MedicineName { get; set; }
        public string StockUnitName { get; set; }
        public long QuantityMilli { get; set; }
        public List<CalculatedBatchLine> BatchLines { get; private set; }
        public long SubtotalPaise { get; set; }
        public long DiscountPaise { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long LineTotalPaise { get; set; }
    }

    public sealed class CalculatedInvoice
    {
        public CalculatedInvoice()
        {
            Lines = new List<CalculatedMedicineLine>();
        }

        public List<CalculatedMedicineLine> Lines { get; private set; }
        public long SubtotalPaise { get; set; }
        public long DiscountPaise { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long RoundOffPaise { get; set; }
        public long GrandTotalPaise { get; set; }
    }

    public sealed class ValidatedTender
    {
        public InvoicePaymentMethod Method { get; set; }
        public string DatabaseMethod { get; set; }
        public long AmountPaise { get; set; }
        public string ReferenceText { get; set; }
    }

    public sealed class CommittedInvoiceRecord
    {
        public string InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string FinancialYear { get; set; }
        public DateTime IssuedAtUtc { get; set; }
        public int IssuedLocalOffsetMinutes { get; set; }
        public long GrandTotalPaise { get; set; }
    }

    /// <summary>
    /// Optional checkpoint delegate used by automated rollback tests to inject failures
    /// after specific database write operations within the finalisation transaction.
    /// </summary>
    public delegate void BillingWriteCheckpoint(string checkpointName);
}