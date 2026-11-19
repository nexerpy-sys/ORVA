namespace Orva.Domain
{
    public sealed class StockEntryInput
    {
        public string MedicineName { get; set; }
        public string StockUnitName { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYearMonth { get; set; }
        public long QuantityMilli { get; set; }
        public long PurchasePricePaise { get; set; }
        public long MrpPaise { get; set; }
        public string SupplierName { get; set; }
        public string SupplierLicence { get; set; }
        public string DocumentType { get; set; }
        public string DocumentReference { get; set; }
    }
}