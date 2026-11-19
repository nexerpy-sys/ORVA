using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orva.Controls;
using Orva.Services;

namespace Orva.Views
{
    public partial class SubscriptionView : UserControl
    {
        // Commercial pricing is configurable via AppSettings; never hardcode unapproved customer prices.
        private static readonly Dictionary<string, string> PlanPriceSettingKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Monthly", "subscription.price_monthly" },
            { "Quarterly", "subscription.price_quarterly" },
            { "Annual", "subscription.price_annual" },
            { "One-Time", "subscription.price_onetime" }
        };

        private string _selectedPlan = "Monthly";

        public SubscriptionView()
        {
            InitializeComponent();
            LoadState();
        }

        private void LoadState()
        {
            var settings = App.Services.AppSettings;
            UsernameBox.Text = settings.Get(SubscriptionService.UsernameKey, string.Empty);
            ActivationCodeBox.Text = settings.Get(SubscriptionService.ActivationCodeKey, string.Empty);
            RegistryUrlBox.Text = settings.Get(SubscriptionService.RegistryUrlKey, string.Empty);

            string phones = settings.Get(SubscriptionService.SupportPhonesKey, "Configure support phone in Settings");
            string upiId = settings.Get(SubscriptionService.UpiIdKey, "Configure merchant UPI ID in Settings");
            string payee = settings.Get(SubscriptionService.UpiPayeeKey, string.Empty);
            string upiDisplay = string.IsNullOrWhiteSpace(payee) ? upiId : (upiId + " · Payee: " + payee);
            SupportContactText.Text = "1. Call Subscription Support: " + phones + "\n2. Pay via UPI (" + upiDisplay + ") — verified manually by Rindle Works admins.";

            if (string.IsNullOrWhiteSpace(AmountBox.Text))
                AmountBox.Text = GetDefaultAmountForPlan(_selectedPlan);

            UpdateUpiQr();
            RenderStatus(App.Services.SubscriptionService.EvaluateLocalStatus());
        }

        private void RenderStatus(SubscriptionStatus status)
        {
            PlanTitleText.Text = string.IsNullOrWhiteSpace(status.Plan) ? status.Status : status.Plan + " — " + status.Status;
            PlanDetailText.Text = status.Message;
            PlanDetailText.Foreground = (Brush)FindResource(status.IsRenewalReminderDue || !status.AllowNewBilling ? "Vermilion" : "Muted");
        }

        private void SelectPlan_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            _selectedPlan = btn.Tag.ToString();
            SelectedPlanLabel.Text = "Selected plan: " + _selectedPlan;
            AmountBox.Text = GetDefaultAmountForPlan(_selectedPlan);
            UpdateUpiQr();
        }

        private void AmountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) UpdateUpiQr();
        }

        private void UpdateUpiQr()
        {
            if (App.Services == null || UpiQrPanel == null) return;
            string upiId = App.Services.AppSettings.Get(SubscriptionService.UpiIdKey, string.Empty).Trim();
            string payee = App.Services.AppSettings.Get(SubscriptionService.UpiPayeeKey, "Rindle Works").Trim();
            if (string.IsNullOrEmpty(upiId))
            {
                UpiQrPanel.Visibility = Visibility.Collapsed;
                UpiQrImage.Source = null;
                return;
            }

            string amt = (AmountBox.Text ?? string.Empty).Trim();
            string note = "ORVA " + _selectedPlan + " Licence";
            string uri = SubscriptionService.BuildUpiPaymentUri(upiId, payee, amt, note);
            try
            {
                UpiQrImage.Source = QrCodeHelper.RenderToDrawingImage(uri);
                UpiQrCaption.Text = string.IsNullOrEmpty(amt)
                    ? "Scan to pay · " + payee
                    : "Scan to pay Rs. " + amt + "\n" + payee;
                UpiQrPanel.Visibility = Visibility.Visible;
            }
            catch
            {
                UpiQrPanel.Visibility = Visibility.Collapsed;
            }
        }

        private static string GetDefaultAmountForPlan(string plan)
        {
            if (App.Services == null || App.Services.AppSettings == null) return string.Empty;
            string key;
            if (!PlanPriceSettingKeys.TryGetValue(plan ?? string.Empty, out key)) return string.Empty;
            return App.Services.AppSettings.Get(key, string.Empty).Trim();
        }

        private void NotifyWhatsapp_Click(object sender, RoutedEventArgs e)
        {
            var profile = App.Services.ShopRepository.GetProfile();
            string shopName = profile != null ? profile.ShopName : "Medical Shop";
            string waDigits = App.Services.AppSettings.Get(SubscriptionService.SupportWhatsappKey, string.Empty);

            string url = SubscriptionService.BuildWhatsAppClickToChatUrl(
                waDigits,
                shopName,
                UsernameBox.Text,
                _selectedPlan,
                AmountBox.Text,
                UtrBox.Text);

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SaveStatusText.Foreground = (Brush)FindResource("Danger");
                SaveStatusText.Text = "Could not open WhatsApp link: " + ex.Message;
            }
        }

        private void SaveCredentials_Click(object sender, RoutedEventArgs e)
        {
            App.Services.SubscriptionService.SaveCredentials(UsernameBox.Text, ActivationCodeBox.Text, RegistryUrlBox.Text);
            SaveStatusText.Foreground = (Brush)FindResource("Success");
            SaveStatusText.Text = "Subscription credentials saved. Click Refresh Subscription to verify online.";
            RenderStatus(App.Services.SubscriptionService.EvaluateLocalStatus());
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshSubButton.IsEnabled = false;
            try
            {
                App.Services.SubscriptionService.SaveCredentials(UsernameBox.Text, ActivationCodeBox.Text, RegistryUrlBox.Text);
                var status = await App.Services.SubscriptionService.RefreshFromRegistryAsync();
                if (!IsLoaded) return;
                RenderStatus(status);
                var mw = Window.GetWindow(this) as MainWindow;
                if (mw != null) mw.RefreshSubscriptionNotice();
            }
            catch (Exception ex)
            {
                if (IsLoaded)
                {
                    SaveStatusText.Foreground = (Brush)FindResource("Danger");
                    SaveStatusText.Text = "Refresh failed: " + ex.Message;
                }
            }
            finally
            {
                if (IsLoaded) RefreshSubButton.IsEnabled = true;
            }
        }

        private void CopySupportPhone_Click(object sender, RoutedEventArgs e)
        {
            string val = App.Services.AppSettings.Get(SubscriptionService.SupportPhonesKey, string.Empty).Trim();
            if (string.IsNullOrEmpty(val)) { CopyFeedbackText.Text = "No support phone configured in Settings."; return; }
            try { Clipboard.SetText(val); CopyFeedbackText.Text = "Support phone copied."; }
            catch { CopyFeedbackText.Text = "Could not access clipboard."; }
        }

        private void CopyUpiId_Click(object sender, RoutedEventArgs e)
        {
            string val = App.Services.AppSettings.Get(SubscriptionService.UpiIdKey, string.Empty).Trim();
            if (string.IsNullOrEmpty(val)) { CopyFeedbackText.Text = "No UPI ID configured in Settings."; return; }
            try { Clipboard.SetText(val); CopyFeedbackText.Text = "UPI ID copied."; }
            catch { CopyFeedbackText.Text = "Could not access clipboard."; }
        }
    }
}
