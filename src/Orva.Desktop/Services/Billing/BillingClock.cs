using System;

namespace Orva.Services.Billing
{
    public sealed class BillingInstant
    {
        public DateTime UtcNow { get; set; }
        public DateTime LocalNow { get; set; }
        public int LocalOffsetMinutes { get; set; }
        public int CurrentExpiryYearMonth { get { return LocalNow.Year * 100 + LocalNow.Month; } }

        public static BillingInstant Capture(IBillingClock clock)
        {
            if (clock == null || clock is SystemBillingClock)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                return new BillingInstant
                {
                    UtcNow = now.UtcDateTime,
                    LocalNow = now.LocalDateTime,
                    LocalOffsetMinutes = (int)Math.Round(now.Offset.TotalMinutes, MidpointRounding.AwayFromZero)
                };
            }
            return new BillingInstant
            {
                UtcNow = clock.UtcNow,
                LocalNow = clock.LocalNow,
                LocalOffsetMinutes = clock.LocalOffsetMinutes
            };
        }
    }

    public interface IBillingClock
    {
        DateTime UtcNow { get; }
        int LocalOffsetMinutes { get; }
        DateTime LocalNow { get; }
        int CurrentExpiryYearMonth { get; }
    }

    public sealed class SystemBillingClock : IBillingClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        public int LocalOffsetMinutes
        {
            get
            {
                var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
                return (int)offset.TotalMinutes;
            }
        }

        public DateTime LocalNow
        {
            get { return DateTime.Now; }
        }

        public int CurrentExpiryYearMonth
        {
            get
            {
                var local = DateTime.Now;
                return local.Year * 100 + local.Month;
            }
        }
    }
}