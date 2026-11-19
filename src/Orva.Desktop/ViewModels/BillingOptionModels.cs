using Orva.Domain.Billing;

namespace Orva.ViewModels
{
    public sealed class PaymentMethodOption
    {
        public PaymentMethodOption(InvoicePaymentMethod method, string displayName)
        {
            Method = method;
            DisplayName = displayName;
        }

        public InvoicePaymentMethod Method { get; private set; }
        public string DisplayName { get; private set; }
    }

    public sealed class PaperFormatOption
    {
        public PaperFormatOption(InvoicePaperFormat format, string displayName)
        {
            Format = format;
            DisplayName = displayName;
        }

        public InvoicePaperFormat Format { get; private set; }
        public string DisplayName { get; private set; }
    }

    public sealed class InvoicePrintRequestedEventArgs : System.EventArgs
    {
        public InvoicePrintRequestedEventArgs(
            InvoiceResult invoice,
            InvoicePreviewResult preview,
            InvoicePaperFormat paperFormat)
        {
            Invoice = invoice;
            Preview = preview;
            PaperFormat = paperFormat;
        }

        public InvoiceResult Invoice { get; private set; }
        public InvoicePreviewResult Preview { get; private set; }
        public InvoicePaperFormat PaperFormat { get; private set; }
    }
}