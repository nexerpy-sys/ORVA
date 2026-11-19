using System;
using System.Collections.Generic;

namespace Orva.Domain.Billing
{
    // Shared, versioned boundary between the billing engine and the WPF billing UI.
    // Keep this contract compatible with C# 7.3 / .NET Framework 4.8.1.

    public enum InvoicePaymentMethod
    {
        Cash = 1,
        Upi = 2,
        DebitCard = 3,
        CreditCard = 4
    }

    public enum BillingIssueSeverity
    {
        Information = 1,
        Warning = 2,
        Blocking = 3
    }

    public sealed class InvoiceDraft
    {
        public InvoiceDraft()
        {
            Items = new List<InvoiceItemDraft>();
            Tenders = new List<PaymentTenderDraft>();
            Customer = new InvoiceCustomerDraft();
        }

        public List<InvoiceItemDraft> Items { get; set; }
        public List<PaymentTenderDraft> Tenders { get; set; }
        public InvoiceCustomerDraft Customer { get; set; }
    }

    public sealed class InvoiceItemDraft
    {
        public string MedicineId { get; set; }
        public long QuantityMilli { get; set; }
    }

    public sealed class PaymentTenderDraft
    {
        public InvoicePaymentMethod Method { get; set; }
        public long AmountPaise { get; set; }
        public string ReferenceText { get; set; }
    }

    public sealed class InvoiceCustomerDraft
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Gstin { get; set; }
    }

    public sealed class MedicineLookupResult
    {
        public string MedicineId { get; set; }
        public string DisplayName { get; set; }
        public string GenericName { get; set; }
        public string BrandName { get; set; }
        public string Strength { get; set; }
        public string DosageForm { get; set; }
        public string StockUnitName { get; set; }
        public long AvailableQuantityMilli { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public bool IsLowStock { get; set; }
        public bool CanSell { get; set; }
        public string AvailabilityMessage { get; set; }
    }

    public sealed class InvoicePreviewResult
    {
        public InvoicePreviewResult()
        {
            Lines = new List<InvoiceLinePreview>();
            Issues = new List<BillingIssue>();
        }

        public bool IsDraftValid { get; set; }
        public bool CanFinalize { get; set; }
        public List<InvoiceLinePreview> Lines { get; set; }
        public List<BillingIssue> Issues { get; set; }
        public long SubtotalPaise { get; set; }
        public long DiscountPaise { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long RoundOffPaise { get; set; }
        public long GrandTotalPaise { get; set; }
        public long TenderTotalPaise { get; set; }
        public long AmountRemainingPaise { get; set; }
        public bool IsFullyPaid { get; set; }
    }

    public sealed class InvoiceLinePreview
    {
        public InvoiceLinePreview()
        {
            BatchAllocations = new List<BatchAllocationPreview>();
        }

        public string MedicineId { get; set; }
        public string MedicineName { get; set; }
        public long QuantityMilli { get; set; }
        public string StockUnitName { get; set; }
        public List<BatchAllocationPreview> BatchAllocations { get; set; }
        public long SubtotalPaise { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long LineTotalPaise { get; set; }
    }

    public sealed class BatchAllocationPreview
    {
        public string BatchNumber { get; set; }
        public int ExpiryYyyyMm { get; set; }
        public long QuantityMilli { get; set; }
        public long UnitPricePaise { get; set; }
        public long SubtotalPaise { get; set; }
        public long TaxableValuePaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
        public long IgstPaise { get; set; }
        public long LineTotalPaise { get; set; }
    }

    public sealed class BillingIssue
    {
        public BillingIssueSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string MedicineId { get; set; }
    }

    public sealed class InvoiceResult
    {
        public InvoiceResult()
        {
            Issues = new List<BillingIssue>();
            Warnings = new List<string>();
        }

        public bool Succeeded { get; set; }
        public string InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime? IssuedAtUtc { get; set; }
        public int IssuedLocalOffsetMinutes { get; set; }
        public long GrandTotalPaise { get; set; }
        public List<BillingIssue> Issues { get; set; }
        public List<string> Warnings { get; set; }
    }

    public interface IBillingService
    {
        IList<MedicineLookupResult> SearchMedicines(string query, int maximumResults);
        InvoicePreviewResult PreviewInvoice(InvoiceDraft draft);
        InvoiceResult FinalizeInvoice(InvoiceDraft draft);
    }
}