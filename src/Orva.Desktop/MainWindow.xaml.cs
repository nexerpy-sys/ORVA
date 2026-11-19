using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Orva.Controls;
using Orva.Services;
using Orva.ViewModels;
using Orva.Views;

namespace Orva
{
    public partial class MainWindow : Window
    {
        private const double DrawerExpandedWidth = 272.0;

        private static readonly Brush ObsidianBg = FreezeBrush(Color.FromRgb(0x08, 0x09, 0x0D));
        private static readonly Brush ObsidianSurface = FreezeBrush(Color.FromRgb(0x10, 0x12, 0x18));
        private static readonly Brush ObsidianActive = FreezeBrush(Color.FromRgb(0x18, 0x1B, 0x24));
        private static readonly Brush ObsidianRule = FreezeBrush(Color.FromRgb(0x2B, 0x2E, 0x38));
        private static readonly Brush ObsidianWhite = FreezeBrush(Color.FromRgb(0xF0, 0xF0, 0xEC));
        private static readonly Brush ObsidianMuted = FreezeBrush(Color.FromRgb(0x77, 0x7B, 0x86));
        private static readonly Brush ShowcaseElectric = FreezeBrush(Color.FromRgb(0x8B, 0x7C, 0xFF));

        private readonly DispatcherTimer _dailyBackupTimer;
        private bool _drawerOpen;
        private string _activeRoute = "home";

        private static Brush FreezeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public MainWindow()
        {
            InitializeComponent();

            RefreshShopHeader();
            ApplyLanguageSelection();
            ShowHome();
            Loaded += MainWindow_Loaded;

            _dailyBackupTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _dailyBackupTimer.Tick += DailyBackupTimer_Tick;
            _dailyBackupTimer.Start();
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UiMotion.FadeWindow(this);
        }

        public void RefreshSubscriptionNotice()
        {
            if (PageHost.Content is ModuleNoticeView && App.Services != null && App.Services.SubscriptionService.EvaluateLocalStatus().AllowNewBilling)
                ShowBilling();
        }

        private void RefreshShopHeader()
        {
            var profile = App.Services.ShopRepository.GetProfile();
            ShopNameText.Text = profile == null ? "Medical store" : profile.ShopName;
            CurrentDateText.Text = DateTime.Now.ToString("dd MMM yyyy").ToUpperInvariant();
        }

        private void DailyBackupTimer_Tick(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                try
                {
                    App.Services.AutomaticBackupCoordinator.RunDailyBackupIfDue();
                }
                catch (Exception ex) { App.LogFatal("DailyBackupTimer", ex); }
            });
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _dailyBackupTimer.Stop();
            OrvaSpring.Cancel(NavDrawer);
        }

        private void ApplyLanguageSelection()
        {
            bool isEn = LocalizationManager.CurrentLanguage == "en";
            EnglishButton.Background = isEn ? ObsidianWhite : Brushes.Transparent;
            EnglishButton.Foreground = isEn ? ObsidianBg : ObsidianWhite;

            BengaliButton.Background = !isEn ? ObsidianWhite : Brushes.Transparent;
            BengaliButton.Foreground = !isEn ? ObsidianBg : ObsidianWhite;
        }

        private void OpenDrawer()
        {
            if (_drawerOpen) return;
            _drawerOpen = true;

            OrvaSpring.Animate(
                NavDrawer,
                NavDrawer.Width,
                DrawerExpandedWidth,
                v => { NavDrawer.Width = Math.Max(0.0, v); },
                320.0,
                32.0);
            CascadeDrawerItems();
        }

        private void CascadeDrawerItems()
        {
            if (!UiMotion.Enabled) return;
            var items = new Button[] { NavHomeBtn, NavBillingBtn, NavInventoryBtn, NavInvoicesBtn, NavReturnsBtn, NavRecordsBtn, NavSettingsBtn, NavSubscriptionBtn, NavAboutBtn };
            for (int i = 0; i < items.Length; i++)
            {
                var btn = items[i];
                if (btn == null) continue;
                var tt = btn.RenderTransform as TranslateTransform;
                if (tt == null)
                {
                    tt = new TranslateTransform();
                    btn.RenderTransform = tt;
                }
                double startX = -14.0 - (i * 2.5);
                tt.X = startX;
                OrvaSpring.Animate(tt, startX, 0.0, v => { tt.X = v; }, 300.0 + (i * 12.0), 28.0);
            }
        }

        private void CloseDrawer()
        {
            if (!_drawerOpen) return;
            _drawerOpen = false;

            OrvaSpring.Animate(
                NavDrawer,
                NavDrawer.Width,
                0.0,
                v => { NavDrawer.Width = Math.Max(0.0, v); },
                360.0,
                34.0);
        }

        private void HighlightActiveNav(string route)
        {
            _activeRoute = route;
            bool isShowcase = string.Equals(route, "home", StringComparison.OrdinalIgnoreCase)
                || string.Equals(route, "about", StringComparison.OrdinalIgnoreCase);
            var activeAccent = isShowcase ? ShowcaseElectric : (Brush)FindResource("Vermilion");
            var buttons = new Tuple<string, Button>[]
            {
                Tuple.Create("home", NavHomeBtn),
                Tuple.Create("billing", NavBillingBtn),
                Tuple.Create("inventory", NavInventoryBtn),
                Tuple.Create("invoices", NavInvoicesBtn),
                Tuple.Create("returns", NavReturnsBtn),
                Tuple.Create("records", NavRecordsBtn),
                Tuple.Create("settings", NavSettingsBtn),
                Tuple.Create("subscription", NavSubscriptionBtn),
                Tuple.Create("about", NavAboutBtn)
            };

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Item2 == null) continue;
                bool isActive = string.Equals(buttons[i].Item1, route, StringComparison.OrdinalIgnoreCase);
                buttons[i].Item2.Background = isActive ? ObsidianActive : Brushes.Transparent;
                buttons[i].Item2.BorderBrush = isActive ? activeAccent : Brushes.Transparent;
                buttons[i].Item2.BorderThickness = isActive ? new Thickness(3, 0, 0, 0) : new Thickness(0);
            }
        }

        private bool ConfirmLeaveUnsavedBill()
        {
            var bv = PageHost.Content as BillingView;
            if (bv == null || bv.ViewModel == null || !bv.ViewModel.HasCartItems || bv.ViewModel.IsCommitted) return true;
            return OrvaConfirmDialog.Show(this, "ORVA — Unsaved Bill", "You have items in the current bill that have not been saved yet. Discard this draft and continue?", true, "Discard Draft", "Stay on Bill");
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ConfirmLeaveUnsavedBill()) e.Cancel = true;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _drawerOpen)
            {
                CloseDrawer();
                e.Handled = true;
            }
            else if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowBilling();
                e.Handled = true;
            }
        }

        private void SetPage(string route, string title, string eyebrow, FrameworkElement page, bool fullBleed)
        {
            if (PageHost.Content != null && !ConfirmLeaveUnsavedBill()) return;
            CloseDrawer();
            HighlightActiveNav(route);
            PageTitle.Text = title;
            PageEyebrow.Text = eyebrow;
            ApplyTopBarTheme(fullBleed);
            PageHostContainer.Margin = fullBleed ? new Thickness(0) : new Thickness(26, 20, 26, 20);
            OrvaNavigation.SetContent(PageHost, page);
        }

        private void ApplyTopBarTheme(bool darkShowcase)
        {
            var vermilion = (Brush)FindResource("Vermilion");
            if (darkShowcase)
            {
                TopBarBorder.Background = ObsidianBg;
                TopBarBorder.BorderBrush = ObsidianRule;
                TopDivider.Background = ObsidianRule;
                TopBrandText.Foreground = ObsidianWhite;
                TopBrandDot.Background = ShowcaseElectric;
                PageTitle.Foreground = ObsidianWhite;
                PageEyebrow.Foreground = ObsidianMuted;
                CurrentDateText.Foreground = ObsidianMuted;
                HamburgerButton.Background = ObsidianSurface;
                HamburgerButton.BorderBrush = ObsidianRule;
                HamLine1.Background = ObsidianWhite;
                HamLine2.Background = ObsidianWhite;
                HamLine3.Background = ShowcaseElectric;
                DrawerAccentBar.Background = ShowcaseElectric;
            }
            else
            {
                var paperRaised = (Brush)FindResource("PaperRaised");
                var paper = (Brush)FindResource("Paper");
                var rule = (Brush)FindResource("Rule");
                var teal = (Brush)FindResource("Teal");
                var muted = (Brush)FindResource("Muted");
                TopBarBorder.Background = paperRaised;
                TopBarBorder.BorderBrush = rule;
                TopDivider.Background = rule;
                TopBrandText.Foreground = teal;
                TopBrandDot.Background = vermilion;
                PageTitle.Foreground = teal;
                PageEyebrow.Foreground = muted;
                CurrentDateText.Foreground = muted;
                HamburgerButton.Background = paper;
                HamburgerButton.BorderBrush = rule;
                HamLine1.Background = teal;
                HamLine2.Background = teal;
                HamLine3.Background = vermilion;
                DrawerAccentBar.Background = vermilion;
            }
        }

        private void ShowHome()
        {
            RefreshShopHeader();
            var home = new HomeView();
            home.StartBillingRequested += (s, e) => ShowBilling();
            home.AddStockRequested += (s, e) => OpenQuickAddStockDialog();
            home.NavigateRequested += (s, target) =>
            {
                if (string.Equals(target, "nav_inventory", StringComparison.OrdinalIgnoreCase)) ShowInventory();
                else if (string.Equals(target, "nav_records", StringComparison.OrdinalIgnoreCase)) ShowRecords();
                else if (string.Equals(target, "nav_about", StringComparison.OrdinalIgnoreCase)) ShowAboutUs();
                else if (string.Equals(target, "nav_invoices", StringComparison.OrdinalIgnoreCase)) ShowInvoices();
                else if (string.Equals(target, "nav_settings", StringComparison.OrdinalIgnoreCase)) ShowSettings();
                else if (string.Equals(target, "nav_returns", StringComparison.OrdinalIgnoreCase)) ShowReturns();
                else if (string.Equals(target, "nav_subscription", StringComparison.OrdinalIgnoreCase)) ShowSubscription();
            };

            SetPage(
                "home",
                LocalizationManager.Get("PageHomeTitle"),
                LocalizationManager.Get("PageHomeEyebrow"),
                home,
                true);
        }

        private void OpenQuickAddStockDialog()
        {
            CloseDrawer();
            var dlg = new AddStockWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (_activeRoute == "home") ShowHome();
                else if (_activeRoute == "inventory") ShowInventory();
            }
        }

        private void ShowBilling()
        {
            RefreshShopHeader();

            var subStatus = App.Services.SubscriptionService.EvaluateLocalStatus();
            if (!subStatus.AllowNewBilling)
            {
                SetPage(
                    "billing",
                    LocalizationManager.Get("PageBillingTitle"),
                    LocalizationManager.Get("PageBillingEyebrow"),
                    new ModuleNoticeView(subStatus.Message, "Subscription Renewal Required"),
                    false);
                return;
            }

            var profile = App.Services.ShopRepository.GetProfile();
            var header = BillingShopHeader.FromProfile(profile);
            var billingView = new BillingView(App.Services.BillingService, header);
            billingView.ViewModel.SelectedPaperFormat = App.Services.InvoicePrintService.GetDefaultPaperFormat();
            billingView.ViewModel.PrintRequested += (s, args) =>
            {
                var printResult = App.Services.InvoicePrintService.PrintInvoice(
                    args.Invoice,
                    args.Preview,
                    args.PaperFormat,
                    billingView.ViewModel.ShopHeader,
                    billingView.ViewModel.CustomerName,
                    billingView.ViewModel.CustomerPhone,
                    billingView.ViewModel.CustomerAddress,
                    billingView.ViewModel.CustomerGstin,
                    billingView.ViewModel.Tenders);

                if (!printResult.Printed && !printResult.CancelledByUser && !string.IsNullOrWhiteSpace(printResult.ErrorMessage))
                {
                    OrvaConfirmDialog.Show(this, "ORVA — Printing", printResult.ErrorMessage, true, "OK");
                }
            };

            SetPage(
                "billing",
                LocalizationManager.Get("PageBillingTitle"),
                LocalizationManager.Get("PageBillingEyebrow"),
                billingView,
                false);
        }

        private void ShowInventory()
        {
            SetPage("inventory", LocalizationManager.Get("PageInventoryTitle"), LocalizationManager.Get("PageInventoryEyebrow"), new InventoryView(), false);
        }

        private void ShowInvoices()
        {
            SetPage("invoices", LocalizationManager.Get("PageInvoicesTitle"), LocalizationManager.Get("PageInvoicesEyebrow"), new InvoicesView(), false);
        }

        private void ShowReturns()
        {
            SetPage("returns", LocalizationManager.Get("PageReturnsTitle"), LocalizationManager.Get("PageReturnsEyebrow"), new SalesReturnsView(), false);
        }

        private void ShowRecords()
        {
            SetPage("records", LocalizationManager.Get("PageRecordsTitle"), LocalizationManager.Get("PageRecordsEyebrow"), new RecordsView(), false);
        }

        private void ShowSettings()
        {
            var settingsView = new SettingsView(App.Services.ShopRepository, App.Services.TaxSettingsRepository);
            settingsView.DatabaseRestored += SettingsView_DatabaseRestored;
            SetPage("settings", LocalizationManager.Get("PageSettingsTitle"), LocalizationManager.Get("PageSettingsEyebrow"), settingsView, false);
        }

        private void ShowSubscription()
        {
            SetPage("subscription", LocalizationManager.Get("PageSubscriptionTitle"), LocalizationManager.Get("PageSubscriptionEyebrow"), new SubscriptionView(), false);
        }

        private void ShowAboutUs()
        {
            var about = new AboutUsView();
            about.HomeRequested += (s, e) => ShowHome();
            about.StartBillingRequested += (s, e) => ShowBilling();
            SetPage("about", LocalizationManager.Get("PageAboutTitle"), LocalizationManager.Get("PageAboutEyebrow"), about, true);
        }

        private void ReloadCurrentRoute()
        {
            switch (_activeRoute)
            {
                case "home": ShowHome(); break;
                case "billing": ShowBilling(); break;
                case "inventory": ShowInventory(); break;
                case "invoices": ShowInvoices(); break;
                case "returns": ShowReturns(); break;
                case "records": ShowRecords(); break;
                case "settings": ShowSettings(); break;
                case "subscription": ShowSubscription(); break;
                case "about": ShowAboutUs(); break;
                default: ShowHome(); break;
            }
        }

        private void SettingsView_DatabaseRestored(object sender, EventArgs e)
        {
            LocalizationManager.SetLanguage(App.Services.AppSettings.Get("display_language", "en"));
            ApplyLanguageSelection();
            ShowHome();
        }

        private void Hamburger_Click(object sender, RoutedEventArgs e)
        {
            if (_drawerOpen) CloseDrawer();
            else OpenDrawer();
        }

        private void CloseDrawer_Click(object sender, RoutedEventArgs e) { CloseDrawer(); }

        private void Home_Click(object sender, RoutedEventArgs e) { ShowHome(); }
        private void Inventory_Click(object sender, RoutedEventArgs e) { ShowInventory(); }
        private void Billing_Click(object sender, RoutedEventArgs e) { ShowBilling(); }
        private void Invoices_Click(object sender, RoutedEventArgs e) { ShowInvoices(); }
        private void Returns_Click(object sender, RoutedEventArgs e) { ShowReturns(); }
        private void Records_Click(object sender, RoutedEventArgs e) { ShowRecords(); }
        private void Settings_Click(object sender, RoutedEventArgs e) { ShowSettings(); }
        private void Subscription_Click(object sender, RoutedEventArgs e) { ShowSubscription(); }
        private void AboutUs_Click(object sender, RoutedEventArgs e) { ShowAboutUs(); }

        private void English_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeaveUnsavedBill()) return;
            LocalizationManager.SetLanguage("en");
            App.Services.AppSettings.Set("display_language", "en");
            ApplyLanguageSelection();
            ReloadCurrentRoute();
        }

        private void Bengali_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeaveUnsavedBill()) return;
            LocalizationManager.SetLanguage("bn");
            App.Services.AppSettings.Set("display_language", "bn");
            ApplyLanguageSelection();
            ReloadCurrentRoute();
        }
    }
}