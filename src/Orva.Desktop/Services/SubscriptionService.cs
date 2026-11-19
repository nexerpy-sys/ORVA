using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class SubscriptionStatus
    {
        public string Username { get; set; }
        public string Plan { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Status { get; set; }
        public DateTime? LastVerifiedUtc { get; set; }
        public bool IsConfigured { get; set; }
        public bool IsActive { get; set; }
        public bool IsInGracePeriod { get; set; }
        public int GraceDaysRemaining { get; set; }
        public bool IsRenewalReminderDue { get; set; }
        public int? DaysUntilExpiry { get; set; }
        public bool AllowNewBilling { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Commercial licensing & subscription service following the agreed ORVA V1 design:
    /// - Fetches raw HTTPS public licenses.csv (never GitHub REST API, never embedded tokens).
    /// - Header-mapped CSV parsing (handles any column order cleanly).
    /// - Verifies one-time activation code against SHA-256 hash stored in licenses.csv.
    /// - Caches last verified state locally; supports 14-day offline grace period, 7-day & 3-day renewal reminders,
    ///   and monotonic last-seen UTC clock-rollback detection.
    /// - After grace expiry, restricts ONLY new invoice creation (never locks historical records/backups).
    /// </summary>
    public sealed class SubscriptionService
    {
        public const int OfflineGraceDays = 14;
        public const int RefreshIntervalHours = 12;

        public const string RegistryUrlKey = "subscription.registry_csv_url";
        public const string UsernameKey = "subscription.username";
        public const string ActivationCodeKey = "subscription.activation_code";
        public const string CachedPlanKey = "subscription.cached_plan";
        public const string CachedStartKey = "subscription.cached_start";
        public const string CachedExpiryKey = "subscription.cached_expiry";
        public const string CachedStatusKey = "subscription.cached_status";
        public const string LastVerifiedUtcKey = "subscription.last_verified_utc";
        public const string LastSeenUtcKey = "subscription.last_seen_utc";
        public const string SupportPhonesKey = "subscription.support_phones";
        public const string SupportWhatsappKey = "subscription.support_whatsapp";
        public const string UpiIdKey = "subscription.upi_id";
        public const string UpiPayeeKey = "subscription.upi_payee_name";

        private readonly AppSettingsRepository _settings;
        private readonly Func<string, Task<string>> _httpFetcher;

        public SubscriptionService(AppSettingsRepository settings, Func<string, Task<string>> httpFetcher = null)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
            _httpFetcher = httpFetcher ?? DefaultFetchHttpsAsync;
        }

        public void SaveCredentials(string username, string activationCode, string registryUrl)
        {
            _settings.Set(UsernameKey, (username ?? string.Empty).Trim());
            _settings.Set(ActivationCodeKey, (activationCode ?? string.Empty).Trim());
            if (!string.IsNullOrWhiteSpace(registryUrl))
                _settings.Set(RegistryUrlKey, registryUrl.Trim());
        }

        public void SaveSupportAndUpiSettings(string supportPhones, string supportWhatsapp, string upiId, string upiPayeeName)
        {
            _settings.Set(SupportPhonesKey, (supportPhones ?? string.Empty).Trim());
            _settings.Set(SupportWhatsappKey, (supportWhatsapp ?? string.Empty).Trim());
            _settings.Set(UpiIdKey, (upiId ?? string.Empty).Trim());
            _settings.Set(UpiPayeeKey, (upiPayeeName ?? string.Empty).Trim());
        }

        public SubscriptionStatus EvaluateLocalStatus(DateTime? nowUtcOverride = null)
        {
            DateTime nowUtc = nowUtcOverride ?? DateTime.UtcNow;
            string username = _settings.Get(UsernameKey, string.Empty).Trim();
            string registryUrl = _settings.Get(RegistryUrlKey, string.Empty).Trim();

            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(registryUrl))
            {
                return new SubscriptionStatus
                {
                    IsConfigured = false,
                    Status = "Not configured",
                    AllowNewBilling = true,
                    Message = "Subscription check is not configured on this installation."
                };
            }

            // Monotonic clock-rollback protection (only when using live system clock or advancing time)
            DateTime? lastSeen = ParseIsoUtc(_settings.Get(LastSeenUtcKey, string.Empty));
            if (lastSeen.HasValue && nowUtc < lastSeen.Value.AddHours(-24))
            {
                return new SubscriptionStatus
                {
                    IsConfigured = true,
                    Username = username,
                    Status = "ClockMismatch",
                    IsActive = false,
                    AllowNewBilling = false,
                    Message = "System date appears to have been moved backwards. Check Windows date/time and click Refresh Subscription. Historical invoices and backups remain accessible."
                };
            }
            if (!lastSeen.HasValue || nowUtc > lastSeen.Value)
            {
                _settings.Set(LastSeenUtcKey, nowUtc.ToString("o", CultureInfo.InvariantCulture));
            }

            string plan = _settings.Get(CachedPlanKey, string.Empty);
            string cachedStatus = _settings.Get(CachedStatusKey, "Unverified");
            DateTime? start = ParseDate(_settings.Get(CachedStartKey, string.Empty));
            DateTime? expiry = ParseDate(_settings.Get(CachedExpiryKey, string.Empty));
            DateTime? lastVerified = ParseIsoUtc(_settings.Get(LastVerifiedUtcKey, string.Empty));

            var result = new SubscriptionStatus
            {
                IsConfigured = true,
                Username = username,
                Plan = plan,
                StartDate = start,
                ExpiryDate = expiry,
                Status = cachedStatus,
                LastVerifiedUtc = lastVerified
            };

            if (!string.Equals(cachedStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                result.IsActive = false;
                result.AllowNewBilling = false;
                result.Message = "Subscription is not active (" + cachedStatus + "). Renew or refresh in Subscription & Support. Historical invoices and backups remain accessible.";
                return result;
            }

            if (string.Equals(plan, "One-Time", StringComparison.OrdinalIgnoreCase) && !expiry.HasValue)
            {
                result.IsActive = true;
                result.AllowNewBilling = true;
                result.Message = "One-Time licence active.";
                return result;
            }

            DateTime today = nowUtc.Date;
            if (expiry.HasValue && today > expiry.Value.Date)
            {
                int daysPast = (today - expiry.Value.Date).Days;
                if (daysPast <= OfflineGraceDays)
                {
                    result.IsActive = false;
                    result.IsInGracePeriod = true;
                    result.GraceDaysRemaining = OfflineGraceDays - daysPast;
                    result.AllowNewBilling = true;
                    result.Message = "Subscription expired. " + result.GraceDaysRemaining + " day(s) of grace period remaining. Please renew soon.";
                    return result;
                }

                result.IsActive = false;
                result.AllowNewBilling = false;
                result.Message = "Subscription grace period has ended. New invoice creation is paused until renewal. Historical invoices, records, and backups remain accessible.";
                return result;
            }

            // Check offline verification staleness (within 1..14 days -> offline grace; > 14 days -> pause new billing)
            if (lastVerified.HasValue)
            {
                int daysSinceVerify = (int)Math.Floor((nowUtc - lastVerified.Value).TotalDays);
                if (daysSinceVerify > OfflineGraceDays)
                {
                    result.IsActive = false;
                    result.IsInGracePeriod = false;
                    result.GraceDaysRemaining = 0;
                    result.AllowNewBilling = false;
                    result.Message = "Connect briefly to the internet and click Refresh Subscription (offline grace period of 14 days exceeded). Historical invoices and backups remain accessible.";
                    return result;
                }

                if (daysSinceVerify >= 1)
                {
                    result.IsActive = true;
                    result.IsInGracePeriod = true;
                    result.GraceDaysRemaining = OfflineGraceDays - daysSinceVerify;
                    result.AllowNewBilling = true;
                    if (expiry.HasValue)
                    {
                        int daysLeft = (expiry.Value.Date - today).Days;
                        result.DaysUntilExpiry = daysLeft;
                        result.IsRenewalReminderDue = daysLeft <= 7;
                    }
                    result.Message = "Active (" + plan + ") — offline mode (" + result.GraceDaysRemaining + " day(s) left before online check is needed).";
                    return result;
                }
            }

            result.IsActive = true;
            result.AllowNewBilling = true;
            if (expiry.HasValue)
            {
                int daysLeft = (expiry.Value.Date - today).Days;
                result.DaysUntilExpiry = daysLeft;
                result.IsRenewalReminderDue = daysLeft <= 7;
                if (daysLeft <= 3)
                {
                    result.Message = "Urgent renewal reminder: Active (" + plan + "), expires in " + daysLeft + " day(s).";
                }
                else if (daysLeft <= 7)
                {
                    result.Message = "Renewal reminder: Active (" + plan + "), expires in " + daysLeft + " day(s).";
                }
                else
                {
                    result.Message = "Active (" + plan + "), valid until " + expiry.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) + ".";
                }
            }
            else
            {
                result.Message = "Active (" + plan + ").";
            }
            return result;
        }

        public async Task<SubscriptionStatus> RefreshFromRegistryAsync(DateTime? nowUtcOverride = null)
        {
            DateTime nowUtc = nowUtcOverride ?? DateTime.UtcNow;
            string url = _settings.Get(RegistryUrlKey, string.Empty).Trim();
            string username = _settings.Get(UsernameKey, string.Empty).Trim();
            string activationCode = _settings.Get(ActivationCodeKey, string.Empty).Trim();

            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var local = EvaluateLocalStatus(nowUtc);
                local.Message = "Configure a valid HTTPS licenses.csv URL to refresh subscription.";
                return local;
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(activationCode))
            {
                var local = EvaluateLocalStatus(nowUtc);
                local.Message = "Enter your licence Username and Activation Code first.";
                return local;
            }

            try
            {
                string csv = await _httpFetcher(url);
                return ApplyRegistryCsv(csv, username, activationCode, nowUtc);
            }
            catch
            {
                var cached = EvaluateLocalStatus(nowUtc);
                cached.Message = "Could not reach licence registry (offline). " + cached.Message;
                return cached;
            }
        }

        public SubscriptionStatus ApplyRegistryCsv(string csvContent, string username, string activationCode, DateTime nowUtc)
        {
            string expectedHash = ComputeSha256Hex(activationCode);
            string[] lines = (csvContent ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                _settings.Set(CachedStatusKey, "NotFound");
                return EvaluateLocalStatus(nowUtc);
            }

            // Build header index map so column order in licenses.csv is flexible and unambiguous
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] headers = SplitCsvLine(lines[0]);
            for (int h = 0; h < headers.Length; h++)
            {
                string key = headers[h].Trim().Trim('\uFEFF').ToLowerInvariant().Replace(" ", "_");
                if (!headerMap.ContainsKey(key)) headerMap[key] = h;
            }

            int idxUser = FindCol(headerMap, new[] { "username", "user", "licence_id" }, 0);
            int idxPlan = FindCol(headerMap, new[] { "plan", "subscription_plan" }, 1);
            int idxStart = FindCol(headerMap, new[] { "start_date", "start" }, 2);
            int idxExpiry = FindCol(headerMap, new[] { "expiry_date", "expiry", "end_date" }, 3);
            int idxStatus = FindCol(headerMap, new[] { "status" }, 4);
            int idxHash = FindCol(headerMap, new[] { "activation_code_sha256", "activation_code_sha256_hash", "activation_hash", "hash" }, 5);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = SplitCsvLine(lines[i]);
                if (cols.Length < 6) continue;

                string rowUser = SafeGet(cols, idxUser);
                if (!string.Equals(rowUser, (username ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) continue;

                string rowPlan = SafeGet(cols, idxPlan);
                string rowStart = SafeGet(cols, idxStart);
                string rowExpiry = SafeGet(cols, idxExpiry);
                string rowStatus = SafeGet(cols, idxStatus);
                string rowHash = SafeGet(cols, idxHash);

                if (!string.Equals(rowHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.Set(CachedStatusKey, "InvalidActivationCode");
                    return EvaluateLocalStatus(nowUtc);
                }

                _settings.Set(CachedPlanKey, rowPlan);
                _settings.Set(CachedStartKey, rowStart);
                _settings.Set(CachedExpiryKey, rowExpiry);
                _settings.Set(CachedStatusKey, rowStatus);
                _settings.Set(LastVerifiedUtcKey, nowUtc.ToString("o", CultureInfo.InvariantCulture));
                _settings.Set(LastSeenUtcKey, nowUtc.ToString("o", CultureInfo.InvariantCulture));
                return EvaluateLocalStatus(nowUtc);
            }

            _settings.Set(CachedStatusKey, "NotFound");
            return EvaluateLocalStatus(nowUtc);
        }

        public static string BuildUpiPaymentUri(string upiId, string payeeName, string amountRupees, string note)
        {
            if (string.IsNullOrWhiteSpace(upiId)) return string.Empty;
            var sb = new StringBuilder();
            sb.Append("upi://pay?pa=").Append(Uri.EscapeDataString(upiId.Trim()));
            if (!string.IsNullOrWhiteSpace(payeeName))
                sb.Append("&pn=").Append(Uri.EscapeDataString(payeeName.Trim()));
            if (!string.IsNullOrWhiteSpace(amountRupees))
                sb.Append("&am=").Append(Uri.EscapeDataString(amountRupees.Trim()));
            sb.Append("&cu=INR");
            if (!string.IsNullOrWhiteSpace(note))
                sb.Append("&tn=").Append(Uri.EscapeDataString(note.Trim()));
            return sb.ToString();
        }

        public static string BuildWhatsAppClickToChatUrl(
            string supportWhatsappDigits,
            string shopName,
            string username,
            string selectedPlan,
            string amountDisplay,
            string utrReference)
        {
            string requestId = "REQ-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string message =
                "Hello, I have made a subscription payment for ORVA.\n\n" +
                "Shop details\n" +
                "Shop name: " + (shopName ?? string.Empty) + "\n" +
                "Licence ID / Username: " + (username ?? string.Empty) + "\n\n" +
                "Payment details\n" +
                "Selected plan: " + (selectedPlan ?? string.Empty) + "\n" +
                "Amount paid: Rs. " + (amountDisplay ?? string.Empty) + "\n" +
                "Payment date: " + DateTime.Now.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture) + "\n" +
                "Payment request ID: " + requestId + "\n" +
                "UPI transaction reference / UTR: " + (string.IsNullOrWhiteSpace(utrReference) ? "Not entered" : utrReference.Trim()) + "\n\n" +
                "Please verify the payment in your receiving account and activate or renew my ORVA licence.\nThank you.";

            string digits = ExtractDigits(supportWhatsappDigits);
            string encoded = Uri.EscapeDataString(message);
            return string.IsNullOrEmpty(digits)
                ? "https://wa.me/?text=" + encoded
                : "https://wa.me/" + digits + "?text=" + encoded;
        }

        public static string ComputeSha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes((input ?? string.Empty).Trim());
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static int FindCol(Dictionary<string, int> map, string[] names, int fallback)
        {
            foreach (string n in names)
            {
                int idx;
                if (map.TryGetValue(n, out idx)) return idx;
            }
            return fallback;
        }

        private static string SafeGet(string[] cols, int index)
        {
            return (index >= 0 && index < cols.Length) ? cols[index].Trim().Trim('"') : string.Empty;
        }

        private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

        private static HttpClient CreateSharedHttpClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            return new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        private static async Task<string> DefaultFetchHttpsAsync(string url)
        {
            return await SharedHttpClient.GetStringAsync(url);
        }

        private static DateTime? ParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            DateTime dt;
            var formats = new[] { "dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy" };
            if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt.Date;
            if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt.Date;
            return null;
        }

        private static DateTime? ParseIsoUtc(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            DateTime dt;
            if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                return dt.ToUniversalTime();
            return null;
        }

        private static string ExtractDigits(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++) if (char.IsDigit(raw[i])) sb.Append(raw[i]);
            return sb.ToString();
        }

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null) return result.ToArray();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Length = 0;
                }
                else sb.Append(ch);
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}
