using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orva.Domain.Billing;
using Orva.ViewModels;

namespace Orva.Controls
{
    /// <summary>
    /// On-screen invoice paper preview for 58 mm, 80 mm, A4 and A5.
    ///
    /// All monetary values, quantities, batch numbers and expiries are bound directly from
    /// InvoicePreviewResult / BatchAllocationPreview. This control performs no pricing, tax,
    /// FEFO or rounding arithmetic. The only double arithmetic here is the visual zoom scale.
    ///
    /// Paper geometry is applied in code rather than through bindings on a Freezable, because
    /// RelativeSource bindings on a LayoutTransform Freezable are not reliable in WPF.
    /// </summary>
    public partial class InvoicePaperPreviewControl : UserControl
    {
        private readonly List<PaymentTenderViewModel> _watchedTenders = new List<PaymentTenderViewModel>();
        private INotifyCollectionChanged _watchedCollection;

        public InvoicePaperPreviewControl()
        {
            InitializeComponent();

            Loaded += Control_Loaded;
            Unloaded += Control_Unloaded;

            ApplyFormat();
            ApplyZoom();
            UpdatePreviewFlags();
            UpdateShopFlags();
            UpdateCustomerFlags();
            UpdatePaymentFlags();
            UpdateInvoiceIdentity();
        }

        // ---- Input dependency properties ---------------------------------------------

        public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
            nameof(Preview), typeof(InvoicePreviewResult), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnPreviewChanged));

        public InvoicePreviewResult Preview
        {
            get { return (InvoicePreviewResult)GetValue(PreviewProperty); }
            set { SetValue(PreviewProperty, value); }
        }

        public static readonly DependencyProperty CommittedInvoiceProperty = DependencyProperty.Register(
            nameof(CommittedInvoice), typeof(InvoiceResult), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnCommittedChanged));

        public InvoiceResult CommittedInvoice
        {
            get { return (InvoiceResult)GetValue(CommittedInvoiceProperty); }
            set { SetValue(CommittedInvoiceProperty, value); }
        }

        public static readonly DependencyProperty PaperFormatProperty = DependencyProperty.Register(
            nameof(PaperFormat), typeof(InvoicePaperFormat), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(InvoicePaperFormat.Thermal80, OnPaperFormatChanged));

        public InvoicePaperFormat PaperFormat
        {
            get { return (InvoicePaperFormat)GetValue(PaperFormatProperty); }
            set { SetValue(PaperFormatProperty, value); }
        }

        public static readonly DependencyProperty ShopProperty = DependencyProperty.Register(
            nameof(Shop), typeof(BillingShopHeader), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnShopChanged));

        public BillingShopHeader Shop
        {
            get { return (BillingShopHeader)GetValue(ShopProperty); }
            set { SetValue(ShopProperty, value); }
        }

        public static readonly DependencyProperty CustomerNameProperty = DependencyProperty.Register(
            nameof(CustomerName), typeof(string), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnCustomerChanged));

        public string CustomerName
        {
            get { return (string)GetValue(CustomerNameProperty); }
            set { SetValue(CustomerNameProperty, value); }
        }

        public static readonly DependencyProperty CustomerPhoneProperty = DependencyProperty.Register(
            nameof(CustomerPhone), typeof(string), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnCustomerChanged));

        public string CustomerPhone
        {
            get { return (string)GetValue(CustomerPhoneProperty); }
            set { SetValue(CustomerPhoneProperty, value); }
        }

        public static readonly DependencyProperty CustomerAddressProperty = DependencyProperty.Register(
            nameof(CustomerAddress), typeof(string), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnCustomerChanged));

        public string CustomerAddress
        {
            get { return (string)GetValue(CustomerAddressProperty); }
            set { SetValue(CustomerAddressProperty, value); }
        }

        public static readonly DependencyProperty CustomerGstinProperty = DependencyProperty.Register(
            nameof(CustomerGstin), typeof(string), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnCustomerChanged));

        public string CustomerGstin
        {
            get { return (string)GetValue(CustomerGstinProperty); }
            set { SetValue(CustomerGstinProperty, value); }
        }

        public static readonly DependencyProperty TenderLinesProperty = DependencyProperty.Register(
            nameof(TenderLines), typeof(IEnumerable), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(null, OnTenderLinesChanged));

        public IEnumerable TenderLines
        {
            get { return (IEnumerable)GetValue(TenderLinesProperty); }
            set { SetValue(TenderLinesProperty, value); }
        }

        public static readonly DependencyProperty ZoomPercentProperty = DependencyProperty.Register(
            nameof(ZoomPercent), typeof(double), typeof(InvoicePaperPreviewControl),
            new PropertyMetadata(100.0, OnZoomChanged));

        public double ZoomPercent
        {
            get { return (double)GetValue(ZoomPercentProperty); }
            set { SetValue(ZoomPercentProperty, value); }
        }

        // ---- Computed display properties ---------------------------------------------

        public static readonly DependencyProperty HasPreviewProperty = DependencyProperty.Register(
            nameof(HasPreview), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool HasPreview
        {
            get { return (bool)GetValue(HasPreviewProperty); }
            private set { SetValue(HasPreviewProperty, value); }
        }

        public static readonly DependencyProperty IsThermalProperty = DependencyProperty.Register(
            nameof(IsThermal), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(true));

        public bool IsThermal
        {
            get { return (bool)GetValue(IsThermalProperty); }
            private set { SetValue(IsThermalProperty, value); }
        }

        public static readonly DependencyProperty IsPageProperty = DependencyProperty.Register(
            nameof(IsPage), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool IsPage
        {
            get { return (bool)GetValue(IsPageProperty); }
            private set { SetValue(IsPageProperty, value); }
        }

        public static readonly DependencyProperty ShowShopProperty = DependencyProperty.Register(
            nameof(ShowShop), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowShop
        {
            get { return (bool)GetValue(ShowShopProperty); }
            private set { SetValue(ShowShopProperty, value); }
        }

        public static readonly DependencyProperty ShowCustomerProperty = DependencyProperty.Register(
            nameof(ShowCustomer), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowCustomer
        {
            get { return (bool)GetValue(ShowCustomerProperty); }
            private set { SetValue(ShowCustomerProperty, value); }
        }

        public static readonly DependencyProperty ShowCgstSgstProperty = DependencyProperty.Register(
            nameof(ShowCgstSgst), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowCgstSgst
        {
            get { return (bool)GetValue(ShowCgstSgstProperty); }
            private set { SetValue(ShowCgstSgstProperty, value); }
        }

        public static readonly DependencyProperty ShowIgstProperty = DependencyProperty.Register(
            nameof(ShowIgst), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowIgst
        {
            get { return (bool)GetValue(ShowIgstProperty); }
            private set { SetValue(ShowIgstProperty, value); }
        }

        public static readonly DependencyProperty ShowTaxableValueProperty = DependencyProperty.Register(
            nameof(ShowTaxableValue), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowTaxableValue
        {
            get { return (bool)GetValue(ShowTaxableValueProperty); }
            private set { SetValue(ShowTaxableValueProperty, value); }
        }

        public static readonly DependencyProperty ShowRoundOffProperty = DependencyProperty.Register(
            nameof(ShowRoundOff), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowRoundOff
        {
            get { return (bool)GetValue(ShowRoundOffProperty); }
            private set { SetValue(ShowRoundOffProperty, value); }
        }

        public static readonly DependencyProperty ShowRemainingProperty = DependencyProperty.Register(
            nameof(ShowRemaining), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowRemaining
        {
            get { return (bool)GetValue(ShowRemainingProperty); }
            private set { SetValue(ShowRemainingProperty, value); }
        }

        public static readonly DependencyProperty ShowPaymentsProperty = DependencyProperty.Register(
            nameof(ShowPayments), typeof(bool), typeof(InvoicePaperPreviewControl), new PropertyMetadata(false));

        public bool ShowPayments
        {
            get { return (bool)GetValue(ShowPaymentsProperty); }
            private set { SetValue(ShowPaymentsProperty, value); }
        }

        public static readonly DependencyProperty InvoiceNumberTextProperty = DependencyProperty.Register(
            nameof(InvoiceNumberText), typeof(string), typeof(InvoicePaperPreviewControl), new PropertyMetadata(string.Empty));

        public string InvoiceNumberText
        {
            get { return (string)GetValue(InvoiceNumberTextProperty); }
            private set { SetValue(InvoiceNumberTextProperty, value); }
        }

        public static readonly DependencyProperty InvoiceDateTextProperty = DependencyProperty.Register(
            nameof(InvoiceDateText), typeof(string), typeof(InvoicePaperPreviewControl), new PropertyMetadata(string.Empty));

        public string InvoiceDateText
        {
            get { return (string)GetValue(InvoiceDateTextProperty); }
            private set { SetValue(InvoiceDateTextProperty, value); }
        }

        public static readonly DependencyProperty SmallFontSizeProperty = DependencyProperty.Register(
            nameof(SmallFontSize), typeof(double), typeof(InvoicePaperPreviewControl), new PropertyMetadata(10.5));

        public double SmallFontSize
        {
            get { return (double)GetValue(SmallFontSizeProperty); }
            private set { SetValue(SmallFontSizeProperty, value); }
        }

        public static readonly DependencyProperty TinyFontSizeProperty = DependencyProperty.Register(
            nameof(TinyFontSize), typeof(double), typeof(InvoicePaperPreviewControl), new PropertyMetadata(9.5));

        public double TinyFontSize
        {
            get { return (double)GetValue(TinyFontSizeProperty); }
            private set { SetValue(TinyFontSizeProperty, value); }
        }

        // ---- Callbacks ---------------------------------------------------------------

        private static void OnPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (InvoicePaperPreviewControl)d;
            control.UpdatePreviewFlags();
            control.UpdateInvoiceIdentity();
        }

        private static void OnCommittedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InvoicePaperPreviewControl)d).UpdateInvoiceIdentity();
        }

        private static void OnPaperFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InvoicePaperPreviewControl)d).ApplyFormat();
        }

        private static void OnShopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InvoicePaperPreviewControl)d).UpdateShopFlags();
        }

        private static void OnCustomerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InvoicePaperPreviewControl)d).UpdateCustomerFlags();
        }

        private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((InvoicePaperPreviewControl)d).ApplyZoom();
        }

        private static void OnTenderLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (InvoicePaperPreviewControl)d;
            control.DetachTenders();
            control.AttachTenders(control.TenderLines);
            control.UpdatePaymentFlags();
        }

        private void Control_Loaded(object sender, RoutedEventArgs e)
        {
            DetachTenders();
            AttachTenders(TenderLines);
            UpdatePaymentFlags();
        }

        private void Control_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachTenders();
        }

        // ---- Layout ------------------------------------------------------------------

        private void ApplyFormat()
        {
            if (PaperBorder == null || PaperContent == null) return;

            var format = PaperFormat;
            var thermal = format == InvoicePaperFormat.Thermal58 || format == InvoicePaperFormat.Thermal80;
            IsThermal = thermal;
            IsPage = !thermal;

            // On-screen paper sizes at 96 device-independent pixels per inch.
            double width;
            double minHeight;
            double baseFont;
            double smallFont;
            double tinyFont;
            Thickness padding;

            switch (format)
            {
                case InvoicePaperFormat.Thermal58:
                    width = 220; minHeight = 380;
                    padding = new Thickness(10, 8, 10, 12);
                    baseFont = 10.5; smallFont = 9.5; tinyFont = 8.5;
                    break;
                case InvoicePaperFormat.Thermal80:
                    width = 304; minHeight = 420;
                    padding = new Thickness(12, 10, 12, 14);
                    baseFont = 11.5; smallFont = 10.5; tinyFont = 9.5;
                    break;
                case InvoicePaperFormat.A5:
                    width = 559; minHeight = 794;
                    padding = new Thickness(26, 22, 26, 26);
                    baseFont = 11.5; smallFont = 10.5; tinyFont = 9.5;
                    break;
                default:
                    width = 794; minHeight = 1123;
                    padding = new Thickness(38, 32, 38, 36);
                    baseFont = 12.5; smallFont = 11.0; tinyFont = 10.0;
                    break;
            }

            PaperBorder.Width = width;
            PaperBorder.MinHeight = minHeight;
            PaperContent.Padding = padding;
            System.Windows.Documents.TextElement.SetFontSize(ThermalPanel, baseFont);
            System.Windows.Documents.TextElement.SetFontSize(PagePanel, baseFont);
            SmallFontSize = smallFont;
            TinyFontSize = tinyFont;

            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (PaperBorder == null) return;

            var percent = ZoomPercent;
            if (percent <= 0) percent = 100.0;

            // Pure visual scale. This value never participates in money or stock arithmetic.
            var scale = percent / 100.0;
            var transform = new ScaleTransform(scale, scale);
            transform.Freeze();
            PaperBorder.LayoutTransform = transform;
        }

        // ---- Display flags -----------------------------------------------------------

        private void UpdatePreviewFlags()
        {
            var preview = Preview;
            HasPreview = preview != null;

            var cgstSgst = preview != null && (preview.CgstPaise != 0 || preview.SgstPaise != 0);
            var igst = preview != null && preview.IgstPaise != 0;

            ShowCgstSgst = cgstSgst;
            ShowIgst = igst;
            ShowTaxableValue = cgstSgst || igst;
            ShowRoundOff = preview != null && preview.RoundOffPaise != 0;
            ShowRemaining = preview != null && preview.AmountRemainingPaise != 0;
        }

        private void UpdateShopFlags()
        {
            var shop = Shop;
            ShowShop = shop != null && !string.IsNullOrWhiteSpace(shop.ShopName);
        }

        private void UpdateCustomerFlags()
        {
            ShowCustomer = !string.IsNullOrWhiteSpace(CustomerName)
                || !string.IsNullOrWhiteSpace(CustomerPhone)
                || !string.IsNullOrWhiteSpace(CustomerAddress)
                || !string.IsNullOrWhiteSpace(CustomerGstin);
        }

        private void UpdateInvoiceIdentity()
        {
            var committed = CommittedInvoice;
            if (committed != null && committed.Succeeded)
            {
                var number = string.IsNullOrWhiteSpace(committed.InvoiceNumber) ? "—" : committed.InvoiceNumber.Trim();
                InvoiceNumberText = BillingText.Get("BillInvoiceNoLabel", "Invoice no.") + ": " + number;
                InvoiceDateText = BillingText.Get("BillIssuedAtLabel", "Date & time") + ": " + FormatIssuedLocal(committed);
            }
            else
            {
                InvoiceNumberText = BillingText.Get("BillPreviewPendingNumber",
                    "Invoice number will be assigned after generation");
                InvoiceDateText = BillingText.Get("BillPreviewPendingDate",
                    "Date & time will be assigned after generation");
            }
        }

        private static string FormatIssuedLocal(InvoiceResult result)
        {
            if (!result.IssuedAtUtc.HasValue) return string.Empty;
            try
            {
                // Timestamp display only: the backend supplies the UTC instant and the offset.
                var local = result.IssuedAtUtc.Value.AddMinutes(result.IssuedLocalOffsetMinutes);
                return local.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return result.IssuedAtUtc.Value.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC";
            }
        }

        // ---- Tender watching (display of the payments block) -------------------------

        private void AttachTenders(IEnumerable source)
        {
            if (source == null) return;

            var collection = source as INotifyCollectionChanged;
            if (collection != null)
            {
                collection.CollectionChanged += TenderCollection_Changed;
                _watchedCollection = collection;
            }

            foreach (object item in source)
            {
                var tender = item as PaymentTenderViewModel;
                if (tender == null) continue;
                tender.PropertyChanged += Tender_PropertyChanged;
                _watchedTenders.Add(tender);
            }
        }

        private void DetachTenders()
        {
            if (_watchedCollection != null)
            {
                _watchedCollection.CollectionChanged -= TenderCollection_Changed;
                _watchedCollection = null;
            }
            for (var i = 0; i < _watchedTenders.Count; i++)
            {
                _watchedTenders[i].PropertyChanged -= Tender_PropertyChanged;
            }
            _watchedTenders.Clear();
        }

        private void TenderCollection_Changed(object sender, NotifyCollectionChangedEventArgs e)
        {
            DetachTenders();
            AttachTenders(TenderLines);
            UpdatePaymentFlags();
        }

        private void Tender_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            if (!string.Equals(name, "HasAmount", StringComparison.Ordinal)
                && !string.Equals(name, "AmountPaise", StringComparison.Ordinal)
                && !string.Equals(name, "HasError", StringComparison.Ordinal)
                && !string.Equals(name, "MethodName", StringComparison.Ordinal)) return;

            UpdatePaymentFlags();
        }

        private void UpdatePaymentFlags()
        {
            var any = false;
            var source = TenderLines;
            if (source != null)
            {
                foreach (object item in source)
                {
                    var tender = item as PaymentTenderViewModel;
                    if (tender == null) continue;
                    if (tender.HasAmount) { any = true; break; }
                }
            }
            ShowPayments = any;
        }
    }
}