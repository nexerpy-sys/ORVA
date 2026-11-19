using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Orva.Domain;
using Orva.Services;

namespace Orva.Views
{
    public partial class HomeView : UserControl
    {
        public event EventHandler StartBillingRequested;
        public event EventHandler AddStockRequested;
        public event EventHandler<string> NavigateRequested;

        private bool _webViewReady;
        private bool _isLoading;
        private bool _isDisposed;
        private HomeShowcaseData _latestData;

        public HomeView()
        {
            InitializeComponent();
            Loaded += HomeView_Loaded;
            Unloaded += HomeView_Unloaded;
        }

        private async void HomeView_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadShowcaseAsync();
        }

        private void HomeView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                if (HomeWebView != null)
                {
                    if (HomeWebView.CoreWebView2 != null)
                    {
                        HomeWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                        HomeWebView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                        HomeWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                        HomeWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    }
                    HomeWebView.Dispose();
                }
            }
            catch
            {
                // Best-effort WebView2 cleanup on view unload
            }
        }

        private async Task LoadShowcaseAsync()
        {
            if (_isLoading || _isDisposed) return;
            _isLoading = true;
            try
            {
                _latestData = await Task.Run(delegate { return GatherShowcaseData(); });
                if (_isDisposed || !IsLoaded) return;

                ApplyFallbackMetrics(_latestData);
                await InitializeAndRenderWebViewAsync(_latestData);
            }
            catch (Exception ex)
            {
                App.LogFatal("HomeView.LoadShowcaseAsync", ex);
                if (!_isDisposed && IsLoaded) ShowOfflineFallback();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private HomeShowcaseData GatherShowcaseData()
        {
            var profile = App.Services.ShopRepository.GetProfile();
            var rows = App.Services.InventoryRepository.GetInventoryRows(string.Empty);
            var backup = App.Services.AutomaticBackupCoordinator.GetStatus();
            var sub = App.Services.SubscriptionService.EvaluateLocalStatus();

            var seenAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenUsable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenLow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int expiringBatches = 0;

            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    string medKey = string.IsNullOrWhiteSpace(r.MedicineId) ? (r.MedicineName ?? string.Empty) : r.MedicineId;
                    if (!string.IsNullOrEmpty(medKey))
                    {
                        seenAll.Add(medKey);
                        if (r.UsableMedicineTotalMilli > 0) seenUsable.Add(medKey);
                        if (r.IsLowStock) seenLow.Add(medKey);
                    }
                    if (r.ExpiryLevel == ExpiryAlertLevel.UrgentWarning30Days || r.ExpiryLevel == ExpiryAlertLevel.Expired)
                    {
                        expiringBatches++;
                    }
                }
            }

            int total = seenAll.Count;
            int usable = seenUsable.Count;
            int low = seenLow.Count;
            int outOfStock = Math.Max(0, total - usable);

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            string verText = ver == null ? "4.0.0" : string.Format("{0}.{1}.{2}", ver.Major, ver.Minor, ver.Build);

            string subHeadline = "LOCAL MODE";
            if (sub != null)
            {
                if (!string.IsNullOrWhiteSpace(sub.Status)) subHeadline = sub.Status;
                else if (sub.IsActive) subHeadline = "SUBSCRIPTION ACTIVE";
            }

            return new HomeShowcaseData
            {
                ShopName = profile == null ? "ORVA Pharmacy" : profile.ShopName,
                DateText = DateTime.Now.ToString("dd MMM yyyy").ToUpperInvariant(),
                Language = LocalizationManager.CurrentLanguage,
                TotalMedicines = total,
                UsableMedicines = usable,
                ExpiringSoonCount = expiringBatches,
                LowStockCount = low,
                OutOfStockCount = outOfStock,
                LastBackupText = backup == null || string.IsNullOrWhiteSpace(backup.LastSuccessLocalDate) ? "Not yet" : backup.LastSuccessLocalDate,
                BackupFresh = backup != null && !backup.IsDueToday && !string.IsNullOrWhiteSpace(backup.LastSuccessLocalDate),
                SubscriptionHeadline = subHeadline,
                BillingAllowed = sub == null || sub.AllowNewBilling,
                AppVersion = verText,
                ReducedMotion = !UiMotion.Enabled
            };
        }

        private void ApplyFallbackMetrics(HomeShowcaseData data)
        {
            if (data == null) return;
            bool bn = string.Equals(data.Language, "bn", StringComparison.OrdinalIgnoreCase);

            string shop = string.IsNullOrWhiteSpace(data.ShopName) ? "ORVA Pharmacy" : data.ShopName;
            FallbackEyebrow.Text = shop.ToUpperInvariant() + "  ·  " + (data.DateText ?? string.Empty);
            FallbackShopTitle.Text = bn
                ? "কাউন্টারের প্রতিদিনের গতির জন্য তৈরি।"
                : "Built for the pace behind the counter.";
            FallbackSubtitle.Text = bn
                ? "দ্রুত বিলিং, ব্যাচ ও মেয়াদের সতর্ক হিসাব এবং আপনার কম্পিউটারেই সুরক্ষিত দোকানের রেকর্ড।"
                : "Fast counter billing, careful batch and expiry checks, and local records that stay under your roof.";

            string backupLabel = data.BackupFresh
                ? (bn ? "সুরক্ষিত" : "Saved")
                : (string.IsNullOrWhiteSpace(data.LastBackupText) || string.Equals(data.LastBackupText, "Not yet", StringComparison.OrdinalIgnoreCase)
                    ? (bn ? "এখনো হয়নি" : "Not yet backed up")
                    : (bn ? "বকেয়া" : "Due today"));

            FallbackReadinessText.Text = bn
                ? string.Format("{0}  ·  {1}টি ওষুধ প্রস্তুত  ·  ৩০ দিনের মধ্যে মেয়াদ শেষ: {2}  ·  ব্যাকআপ: {3}", shop, data.UsableMedicines, data.ExpiringSoonCount, backupLabel)
                : string.Format("{0}  ·  {1} medicines ready  ·  {2} expiring ≤30 days  ·  Backup: {3}", shop, data.UsableMedicines, data.ExpiringSoonCount, backupLabel);

            FallbackCard1Meta.Text = bn ? "০১ · কাউন্টার" : "01 · COUNTER";
            FallbackCard1Title.Text = bn ? "দ্রুত বিলিং" : "Quick Billing";
            FallbackCard1Body.Text = bn ? "দ্রুত বিল তৈরি করুন এবং পরিষ্কার রসিদ প্রিন্ট করুন।" : "Create bills quickly and print clean customer receipts.";

            FallbackCard2Meta.Text = bn ? "০২ · মেয়াদ তদারকি" : "02 · EXPIRY WATCH";
            FallbackCard2Title.Text = bn ? "মেয়াদ ও স্টক" : "Expiry & Stock";
            FallbackCard2Body.Text = bn ? "মেয়াদোত্তীর্ণ ওষুধ সাধারণ বিক্রি থেকে স্বয়ংক্রিয়ভাবে আলাদা রাখে।" : "Keeps expired stock out of regular counter sales automatically.";

            FallbackCard3Meta.Text = bn ? "০৩ · RINDLE WORKS" : "03 · RINDLE WORKS";
            FallbackCard3Title.Text = bn ? "ORVA সম্পর্কে" : "About ORVA";
            FallbackCard3Body.Text = bn ? "আমাদের গল্প, মূলনীতি, সংস্করণ ও সহায়তার তথ্য।" : "Story, principles, version, and local support details.";

            RetryBtn.Content = bn ? "আবার চেষ্টা করুন" : "Retry Showcase";
            LoadingVeilSub.Text = bn ? "ওয়ার্কস্পেস প্রস্তুত হচ্ছে" : "INITIALIZING WORKSPACE";
        }

        private async Task InitializeAndRenderWebViewAsync(HomeShowcaseData data)
        {
            try
            {
                if (!_webViewReady)
                {
                    string userDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Rindle Works", "ORVA", "WebView2Cache");
                    Directory.CreateDirectory(userDataDir);

                    var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                    if (_isDisposed || !IsLoaded) return;

                    await HomeWebView.EnsureCoreWebView2Async(env);
                    if (_isDisposed || !IsLoaded || HomeWebView.CoreWebView2 == null)
                    {
                        if (!_isDisposed && IsLoaded) ShowOfflineFallback();
                        return;
                    }

                    var settings = HomeWebView.CoreWebView2.Settings;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreDevToolsEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.AreHostObjectsAllowed = false;

                    HomeWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    HomeWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                    HomeWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    HomeWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    _webViewReady = true;
                }

                string html = ShowcaseHtmlBuilder.BuildHomeHtml(data);
                HomeWebView.NavigateToString(html);
            }
            catch
            {
                if (!_isDisposed && IsLoaded) ShowOfflineFallback();
            }
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string uri = e.Uri ?? string.Empty;
            if (!string.IsNullOrEmpty(uri)
                && !uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isDisposed || !IsLoaded) return;
            if (e != null && !e.IsSuccess)
            {
                ShowOfflineFallback();
                return;
            }
            LoadingVeil.Visibility = Visibility.Collapsed;
            OfflineFallbackPanel.Visibility = Visibility.Collapsed;
            HomeWebView.Visibility = Visibility.Visible;
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void ShowOfflineFallback()
        {
            LoadingVeil.Visibility = Visibility.Collapsed;
            HomeWebView.Visibility = Visibility.Collapsed;
            OfflineFallbackPanel.Visibility = Visibility.Visible;
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                if (_isDisposed) return;
                string raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(raw)) return;
                var serializer = new JavaScriptSerializer();
                var map = serializer.Deserialize<Dictionary<string, string>>(raw);
                if (map == null || !map.ContainsKey("action")) return;

                string action = map["action"];
                if (string.Equals(action, "start_billing", StringComparison.OrdinalIgnoreCase))
                {
                    if (StartBillingRequested != null) StartBillingRequested(this, EventArgs.Empty);
                }
                else if (string.Equals(action, "add_stock", StringComparison.OrdinalIgnoreCase))
                {
                    if (AddStockRequested != null) AddStockRequested(this, EventArgs.Empty);
                }
                else if (string.Equals(action, "nav_inventory", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(action, "nav_about", StringComparison.OrdinalIgnoreCase))
                {
                    if (NavigateRequested != null) NavigateRequested(this, action);
                }
            }
            catch (Exception ex)
            {
                App.LogFatal("HomeView.WebMessageReceived", ex);
            }
        }

        private void StartBilling_Click(object sender, RoutedEventArgs e)
        {
            if (StartBillingRequested != null) StartBillingRequested(this, EventArgs.Empty);
        }

        private void AddStock_Click(object sender, RoutedEventArgs e)
        {
            if (AddStockRequested != null) AddStockRequested(this, EventArgs.Empty);
        }

        private void FallbackBillingCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (StartBillingRequested != null) StartBillingRequested(this, EventArgs.Empty);
        }

        private void FallbackInventoryCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (NavigateRequested != null) NavigateRequested(this, "nav_inventory");
        }

        private void FallbackAboutCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (NavigateRequested != null) NavigateRequested(this, "nav_about");
        }

        private async void RetryOnline_Click(object sender, RoutedEventArgs e)
        {
            await LoadShowcaseAsync();
        }
    }
}
