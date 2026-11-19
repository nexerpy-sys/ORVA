namespace Orva.Domain
{
    public sealed class StockEntryResult
    {
        public string MedicineName { get; set; }
        public string BatchNumber { get; set; }
        public long NewQuantityMilli { get; set; }
        public bool SnapshotUpdated { get; set; }
        public string SnapshotWarning { get; set; }
    }
}