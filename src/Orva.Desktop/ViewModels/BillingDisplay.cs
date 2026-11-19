using System;
using System.Globalization;

namespace Orva.ViewModels
{
    public enum QuantityParseStatus
    {
        Valid = 0,
        Invalid = 1,
        NotPositive = 2,
        TooLarge = 3,
        TooPrecise = 4
    }

    public enum MoneyParseStatus
    {
        Valid = 0,
        Invalid = 1,
        NotPositive = 2,
        TooLarge = 3,
        TooPrecise = 4
    }

    public static class BillingDisplay
    {
        public static string FormatPaise(long paise)
        {
            return (paise / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        }

        public static string FormatRupees(long paise)
        {
            return "\u20B9 " + FormatPaise(paise);
        }

        public static string FormatQuantity(long quantityMilli)
        {
            return (quantityMilli / 1000m).ToString("0.###", CultureInfo.CurrentCulture);
        }

        public static bool TryParseQuantity(string text, out long quantityMilli, out QuantityParseStatus status)
        {
            quantityMilli = 0;
            decimal value;

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            {
                status = QuantityParseStatus.Invalid;
                return false;
            }

            if (value <= 0)
            {
                status = QuantityParseStatus.NotPositive;
                return false;
            }

            if (value > 999999999m)
            {
                status = QuantityParseStatus.TooLarge;
                return false;
            }

            var scaled = value * 1000m;
            if (scaled != decimal.Truncate(scaled))
            {
                status = QuantityParseStatus.TooPrecise;
                return false;
            }

            quantityMilli = checked((long)scaled);
            status = QuantityParseStatus.Valid;
            return true;
        }

        public static bool TryParseMoney(string text, out long amountPaise, out MoneyParseStatus status)
        {
            amountPaise = 0;
            decimal value;

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            {
                status = MoneyParseStatus.Invalid;
                return false;
            }

            if (value <= 0)
            {
                status = MoneyParseStatus.NotPositive;
                return false;
            }

            if (value > 999999999m)
            {
                status = MoneyParseStatus.TooLarge;
                return false;
            }

            var scaled = value * 100m;
            if (scaled != decimal.Truncate(scaled))
            {
                status = MoneyParseStatus.TooPrecise;
                return false;
            }

            amountPaise = checked((long)scaled);
            status = MoneyParseStatus.Valid;
            return true;
        }

        public static string DescribeQuantityError(QuantityParseStatus status)
        {
            switch (status)
            {
                case QuantityParseStatus.NotPositive:
                    return BillingText.Get("BillQuantityPositive", "Enter a quantity greater than zero.");
                case QuantityParseStatus.TooLarge:
                    return BillingText.Get("BillQuantityTooLarge", "That quantity is too large.");
                case QuantityParseStatus.TooPrecise:
                    return BillingText.Get("BillQuantityPrecision", "Use no more than three decimal places.");
                default:
                    return BillingText.Get("BillQuantityInvalid", "Enter a valid quantity.");
            }
        }

        public static string DescribeMoneyError(MoneyParseStatus status)
        {
            switch (status)
            {
                case MoneyParseStatus.NotPositive:
                    return BillingText.Get("BillAmountPositive", "Enter an amount greater than zero.");
                case MoneyParseStatus.TooLarge:
                    return BillingText.Get("BillAmountTooLarge", "That amount is too large.");
                case MoneyParseStatus.TooPrecise:
                    return BillingText.Get("BillAmountPrecision", "Use no more than two decimal places.");
                default:
                    return BillingText.Get("BillAmountInvalid", "Enter a valid amount.");
            }
        }
    }
}