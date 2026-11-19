namespace Orva.Domain
{
    public sealed class InventoryRow
    {
        public string MedicineName { get; set; }
        public string BatchNumber { get; set; }
        public string Expiry { get; set; }
        public int ExpiryYearMonth { get; set; }
        public string Quantity { get; set; }
        public long QuantityMilli { get; set; }
        public string Unit { get; set; }
        public string Mrp { get; set; }
        public string Status { get; set; }
        public string StatusColour { get; set; }
        public string BatchStatus { get; set; }
        public ExpiryAlertLevel ExpiryLevel { get; set; }
        public bool IsLowStock { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public long UsableMedicineTotalMilli { get; set; }
        public string MedicineId { get; set; }
        public string BatchId { get; set; }
    }
}