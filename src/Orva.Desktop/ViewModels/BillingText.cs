using System;
using System.Globalization;
using System.Windows;

namespace Orva.ViewModels
{
    public static class BillingText
    {
        public static string Get(string key, string fallback)
        {
            try
            {
                var application = Application.Current;
                if (application != null)
                {
                    var value = application.TryFindResource(key) as string;
                    if (!string.IsNullOrEmpty(value)) return value;
                }
            }
            catch
            {
                // A missing resource during startup/shutdown must not break billing.
            }

            return fallback ?? string.Empty;
        }

        public static string Format(string key, string fallback, params object[] arguments)
        {
            var format = Get(key, fallback);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, arguments ?? new object[0]);
            }
            catch (FormatException)
            {
                return fallback ?? format;
            }
        }
    }
}