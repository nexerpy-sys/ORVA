using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Orva.Services;

namespace Orva.Views
{
    public partial class AboutUsView : UserControl
    {
        public event EventHandler HomeRequested;
        public event EventHandler StartBillingRequested;

        private bool _webViewReady;
        private bool _isLoading;
        private bool _isDisposed;

        public AboutUsView()
        {
            InitializeComponent();
            Loaded += AboutUsView_Loaded;
            Unloaded += AboutUsView_Unloaded;
        }

        private async void AboutUsView_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAboutAsync();
        }

        private void AboutUsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                if (AboutWebView != null)
                {
                    if (AboutWebView.CoreWebView2 != null)
                    {
                        AboutWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                        AboutWebView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                        AboutWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                        AboutWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    }
                    AboutWebView.Dispose();
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        private async Task LoadAboutAsync()
        {
            if (_isLoading || _isDisposed) return;
            _isLoading = true;
            try
            {
                var data = GatherAboutData();
                ApplyOfflineFallbackCopy(data);

                if (!_webViewReady)
                {
                    string userDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Rindle Works", "ORVA", "WebView2Cache");
                    Directory.CreateDirectory(userDataDir);

                    var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                    if (_isDisposed || !IsLoaded) return;

                    await AboutWebView.EnsureCoreWebView2Async(env);
                    if (_isDisposed || !IsLoaded || AboutWebView.CoreWebView2 == null)
                    {
                        if (!_isDisposed && IsLoaded) ShowOfflineFallback();
                        return;
                    }

                    var settings = AboutWebView.CoreWebView2.Settings;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreDevToolsEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.AreHostObjectsAllowed = false;

                    AboutWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    AboutWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                    AboutWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    AboutWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    _webViewReady = true;
                }

                AboutWebView.NavigateToString(ShowcaseHtmlBuilder.BuildAboutHtml(data));
            }
            catch (Exception ex)
            {
                App.LogFatal("AboutUsView.LoadAboutAsync", ex);
                if (!_isDisposed && IsLoaded) ShowOfflineFallback();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private AboutShowcaseData GatherAboutData()
        {
            string phone = (App.Services.AppSettings.Get(SubscriptionService.SupportPhonesKey, string.Empty) ?? string.Empty).Trim();
            string email = (App.Services.AppSettings.Get("support.email", string.Empty) ?? string.Empty).Trim();

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            string verText = ver == null ? "4.0.0" : string.Format("{0}.{1}.{2}", ver.Major, ver.Minor, ver.Build);
            string buildText = ver == null ? "0" : ver.Revision.ToString();

            return new AboutShowcaseData
            {
                Language = LocalizationManager.CurrentLanguage,
                SupportPhone = phone,
                SupportEmail = email,
                AppVersion = verText,
                BuildNumber = buildText,
                ReducedMotion = !UiMotion.Enabled
            };
        }

        private void ApplyOfflineFallbackCopy(AboutShowcaseData data)
        {
            bool bn = string.Equals(data.Language, "bn", StringComparison.OrdinalIgnoreCase);

            FallbackKickerLeft.Text = bn ? "ORVA — আমাদের কথা" : "ORVA — ABOUT US";
            FallbackKickerRight.Text = "RINDLE WORKS · V" + data.AppVersion;
            FallbackHeroText.Text = bn
                ? "কাউন্টারের প্রতিদিনের কাজের জন্য তৈরি।"
                : "Made for the work behind the counter.";
            FallbackLeadText.Text = bn
                ? "পরিষ্কার বিল। সতর্ক স্টক ব্যবস্থাপনা। আপনার রেকর্ড, আপনার কাছেই।"
                : "Clear bills. Careful stock. Your records, kept close.";

            FallbackStoryKicker.Text = bn ? "আমাদের কথা" : "OUR STORY";
            FallbackStoryP1.Text = bn
                ? "ORVA-এর পেছনের নির্মাণ-পরিচয় Rindle Works। স্বাধীন ফার্মেসির প্রতিদিনের বাস্তব প্রয়োজন—দ্রুত বিলিং, ব্যাচ ও মেয়াদের সঠিক হিসাব এবং নির্ভরযোগ্য ব্যবসায়িক রেকর্ড—মাথায় রেখেই ORVA তৈরি হচ্ছে।"
                : "Rindle Works is the working studio name behind ORVA. We are building ORVA around the everyday realities of independent pharmacies: quick billing, careful batch and expiry management, and reliable business records.";
            FallbackStoryP2.Text = bn
                ? "আমাদের উদ্দেশ্য সহজ: প্রয়োজনীয় দোকানের কাজগুলোকে আরও গুছিয়ে দেওয়া, সফটওয়্যারকে জটিল না করে। ORVA দৈনন্দিন কাজকে স্থানীয়ভাবে পরিচালিত, ব্যবহারিক এবং দোকানের নিজস্ব নিয়ন্ত্রণে রাখতে সাহায্য করে।"
                : "Our aim is simple: make essential shop operations easier to manage, without making the software itself complicated. ORVA keeps everyday work local, practical and under the shop's control.";

            FallbackPrinKicker.Text = bn ? "ORVA-এর পাঁচটি মূলনীতি" : "THE PRINCIPLES BEHIND ORVA";
            Prin1Title.Text = bn ? "০১ — স্থানীয় ব্যবস্থায়, ইন্টারনেট ছাড়াও" : "01 — LOCAL BY DESIGN";
            Prin1Body.Text = bn ? "দৈনন্দিন বিলিং ও স্টকের কাজ ইন্টারনেট সংযোগ ছাড়াই চালিয়ে যাওয়ার জন্য ORVA তৈরি।" : "Everyday billing and inventory operations are designed to work without an internet connection.";
            Prin2Title.Text = bn ? "০২ — পয়সা পর্যন্ত নির্ভুল হিসাব" : "02 — ACCURATE TO THE PAISE";
            Prin2Body.Text = bn ? "বিলের পরিমাণ ও GST-এর হিসাব পূর্ণ পয়সার এককে করা হয়, যাতে আর্থিক হিসাব নির্ভুল থাকে।" : "Invoice amounts and GST calculations use integer-paise precision for careful financial handling.";
            Prin3Title.Text = bn ? "০৩ — প্রতিটি ব্যাচ গুরুত্বপূর্ণ" : "03 — EVERY BATCH MATTERS";
            Prin3Body.Text = bn ? "ব্যাচ নম্বর, মেয়াদ এবং আগে মেয়াদ শেষ হওয়া ব্যাচ আগে বাছাইয়ের মাধ্যমে মেয়াদোত্তীর্ণ বা অনুপযুক্ত স্টক সাধারণ বিক্রি থেকে বাদ রাখা হয়।" : "Batch numbers, expiry dates and earliest-expiry-first stock selection help ensure that unsuitable stock is excluded from ordinary sales.";
            Prin4Title.Text = bn ? "০৪ — আপনার রেকর্ড, আপনার নিয়ন্ত্রণে" : "04 — YOUR RECORDS, YOUR CONTROL";
            Prin4Body.Text = bn ? "দোকানের তথ্য স্থানীয় স্টোরেজে থাকে। .shopbackup ফাইলের মাধ্যমে দোকানের মালিক নিজেই ব্যাকআপ, পুনরুদ্ধার ও অন্য কম্পিউটারে তথ্য স্থানান্তর করতে পারেন।" : "Shop data is stored locally. The portable .shopbackup file gives the shop owner control over backup, recovery and migration.";
            Prin5Title.Text = bn ? "০৫ — ফার্মেসির বাস্তব কাজের কথা ভেবে" : "05 — MADE FOR REAL COUNTERS";
            Prin5Body.Text = bn ? "কিবোর্ডের মাধ্যমে দ্রুত বিলিং, বারকোড স্ক্যানিং এবং বাংলা–ইংরেজি ভাষার সুবিধা প্রতিদিনের কাজকে সহজ ও গতিশীল রাখার জন্য তৈরি।" : "Keyboard-friendly billing, barcode scanning and English–Bengali support are designed around the pace of everyday pharmacy work.";

            FallbackSupKicker.Text = bn ? "সহায়তা ও যোগাযোগ" : "SUPPORT & CONTACT";
            var supportLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(data.SupportPhone))
            {
                supportLines.Add((bn ? "ফোন: " : "Phone: ") + data.SupportPhone);
            }
            if (!string.IsNullOrWhiteSpace(data.SupportEmail))
            {
                supportLines.Add((bn ? "ইমেল: " : "Email: ") + data.SupportEmail);
            }
            if (supportLines.Count == 0)
            {
                supportLines.Add(bn
                    ? "সহায়তার যোগাযোগের তথ্য এখনো সেটিংসে যুক্ত করা হয়নি।"
                    : "Support contact details are not configured yet. Add them in Settings.");
            }
            FallbackSupportText.Text = string.Join("\n", supportLines.ToArray());

            FallbackProdKicker.Text = bn ? "পণ্য ও পরিচিতি" : "PRODUCT & OWNERSHIP";
            FallbackProductText.Text = "ORVA — " + (bn ? "ফার্মেসি বিলিং ও ইনভেন্টরি" : "Pharmacy Billing & Inventory") + "\n"
                + (bn ? "নির্মাতা: Rindle Works" : "Developed by Rindle Works") + "\n"
                + (bn ? "স্টুডিওর নাম: আপাতত ব্যবহৃত; ভারতে ট্রেডমার্ক যাচাই ও অনুমোদন বাকি।" : "Studio name: Provisional; Indian trademark clearance pending.") + "\n"
                + (bn ? "সংস্করণ: " : "Version: ") + data.AppVersion + "  |  " + (bn ? "বিল্ড: " : "Build: ") + data.BuildNumber + "\n"
                + (bn ? "আপনার দোকানের ব্যবসায়িক রেকর্ড স্থানীয় স্টোরেজে থাকে এবং তার নিয়ন্ত্রণ আপনার কাছেই থাকে।" : "Your shop's business records remain in local storage under your control.");
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
            OfflineAboutPanel.Visibility = Visibility.Collapsed;
            AboutWebView.Visibility = Visibility.Visible;
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
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
                if (string.Equals(action, "nav_home", StringComparison.OrdinalIgnoreCase))
                {
                    if (HomeRequested != null) HomeRequested(this, EventArgs.Empty);
                }
                else if (string.Equals(action, "start_billing", StringComparison.OrdinalIgnoreCase))
                {
                    if (StartBillingRequested != null) StartBillingRequested(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                App.LogFatal("AboutUsView.WebMessageReceived", ex);
            }
        }

        private void ShowOfflineFallback()
        {
            LoadingVeil.Visibility = Visibility.Collapsed;
            AboutWebView.Visibility = Visibility.Collapsed;
            OfflineAboutPanel.Visibility = Visibility.Visible;
        }
    }
}
