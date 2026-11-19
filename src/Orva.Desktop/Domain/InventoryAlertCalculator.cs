using System;

namespace Orva.Domain
{
    public enum ExpiryAlertLevel
    {
        None = 0,
        EarlyWarning90Days = 1,
        Warning60Days = 2,
        UrgentWarning30Days = 3,
        Expired = 4
    }

    public sealed class MedicineDetailsInput
    {
        public string MedicineId { get; set; }
        public string DisplayName { get; set; }
        public string GenericName { get; set; }
        public string BrandName { get; set; }
        public string Strength { get; set; }
        public string DosageForm { get; set; }
        public string StockUnitName { get; set; }
        public long LowStockThresholdMilli { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class StockAdjustmentInput
    {
        public string BatchId { get; set; }
        public long CountedQuantityMilli { get; set; }
        public string Reason { get; set; }
    }

    public sealed class StockAdjustmentResult
    {
        public string MedicineName { get; set; }
        public string BatchNumber { get; set; }
        public long PreviousQuantityMilli { get; set; }
        public long NewQuantityMilli { get; set; }
        public long DifferenceMilli { get; set; }
        public bool SnapshotUpdated { get; set; }
        public string SnapshotWarning { get; set; }
    }

    public sealed class ExpiryAlertItem
    {
        public string MedicineId { get; set; }
        public string BatchId { get; set; }
        public string MedicineName { get; set; }
        public string BatchNumber { get; set; }
        public int ExpiryYearMonth { get; set; }
        public DateTime ExpiryDate { get; set; }
        public int DaysRemaining { get; set; }
        public long QuantityMilli { get; set; }
        public string StockUnitName { get; set; }
        public ExpiryAlertLevel AlertLevel { get; set; }
        public string AlertLabel { get; set; }
    }

    public sealed class LowStockAlertItem
    {
        public string MedicineId { get; set; }
        public string MedicineName { get; set; }
        public string StockUnitName { get; set; }
        public long UsableQuantityMilli { get; set; }
        public long ThresholdMilli { get; set; }
    }

    /// <summary>
    /// Authoritative month-end expiry date and low-stock alert evaluator.
    /// Correctly handles leap-year February and exact 90/60/30-day boundaries.
    /// </summary>
    public static class InventoryAlertCalculator
    {
        public static DateTime GetMonthEndExpiryDate(int expiryYyyyMm)
        {
            int year = expiryYyyyMm / 100;
            int month = expiryYyyyMm % 100;
            if (year < 2000 || year > 2199 || month < 1 || month > 12)
                throw new ArgumentOutOfRangeException("expiryYyyyMm", "Expiry must be a valid YYYYMM value between 200001 and 219912.");

            int lastDay = DateTime.DaysInMonth(year, month);
            return new DateTime(year, month, lastDay);
        }

        public static ExpiryAlertLevel ClassifyExpiry(string batchStatus, int expiryYyyyMm, DateTime localBusinessDate)
        {
            if (string.Equals(batchStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase))
                return ExpiryAlertLevel.Expired;

            DateTime today = localBusinessDate.Date;
            int expYear = expiryYyyyMm / 100;
            int expMonth = expiryYyyyMm % 100;
            if (expYear < 2000 || expYear > 2199 || expMonth < 1 || expMonth > 12)
                return ExpiryAlertLevel.Expired;
            DateTime expiryDate = GetMonthEndExpiryDate(expiryYyyyMm);

            if (expiryDate < today)
                return ExpiryAlertLevel.Expired;

            int daysRemaining = (expiryDate - today).Days;
            if (daysRemaining <= 30)
                return ExpiryAlertLevel.UrgentWarning30Days;
            if (daysRemaining <= 60)
                return ExpiryAlertLevel.Warning60Days;
            if (daysRemaining <= 90)
                return ExpiryAlertLevel.EarlyWarning90Days;

            return ExpiryAlertLevel.None;
        }

        public static bool IsBatchUsableForSale(string batchStatus, int expiryYyyyMm, long quantityMilli, DateTime localBusinessDate)
        {
            if (!string.Equals(batchStatus, "ACTIVE", StringComparison.Ordinal)) return false;
            if (quantityMilli <= 0) return false;
            return GetMonthEndExpiryDate(expiryYyyyMm) >= localBusinessDate.Date;
        }

        public static bool IsLowStock(bool isMedicineActive, long usableQuantityMilli, long thresholdMilli)
        {
            if (!isMedicineActive) return false;
            if (thresholdMilli <= 0) return false;
            return usableQuantityMilli <= thresholdMilli;
        }

        public static string GetExpiryLabel(ExpiryAlertLevel level)
        {
            switch (level)
            {
                case ExpiryAlertLevel.Expired: return "Expired";
                case ExpiryAlertLevel.UrgentWarning30Days: return "Expiring (≤30d)";
                case ExpiryAlertLevel.Warning60Days: return "Expiring (≤60d)";
                case ExpiryAlertLevel.EarlyWarning90Days: return "Expiring (≤90d)";
                default: return "Available";
            }
        }

        public static string GetExpiryColour(ExpiryAlertLevel level)
        {
            switch (level)
            {
                case ExpiryAlertLevel.Expired: return "#C9362B";
                case ExpiryAlertLevel.UrgentWarning30Days: return "#FF4B1F";
                case ExpiryAlertLevel.Warning60Days: return "#FF7A57";
                case ExpiryAlertLevel.EarlyWarning90Days: return "#1E3FA6";
                default: return "#16845B";
            }
        }
    }
}
