using System;
using System.Collections;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Orva.Controls;
using Orva.Domain.Billing;
using Orva.Infrastructure.Repositories;
using Orva.ViewModels;

namespace Orva.Services
{
    public sealed class InvoicePrintResult
    {
        public bool Printed { get; set; }
        public bool CancelledByUser { get; set; }
        public string PrinterName { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Native Windows WPF printing service supporting 58 mm, 80 mm, A4, and A5 formats.
    /// - Restores the shop's last-used Windows printer when available and disposes unused PrintQueue handles.
    /// - Reserves hardware margins BEFORE Measure/Arrange so right-aligned totals are never clipped.
    /// - Paginates multi-page A4/A5 bills by exact contentHeight (zero dropped lines between pages).
    /// </summary>
    public sealed class InvoicePrintService
    {
        public const string DefaultPaperFormatKey = "printing.default_paper_format";
        public const string LastPrinterNameKey = "printing.last_printer_name";

        private readonly AppSettingsRepository _settings;

        public InvoicePrintService(AppSettingsRepository settings)
        {
            _settings = settings;
        }

        public InvoicePaperFormat GetDefaultPaperFormat()
        {
            if (_settings == null) return InvoicePaperFormat.Thermal80;
            string raw = _settings.Get(DefaultPaperFormatKey, "Thermal80");
            InvoicePaperFormat parsed;
            return Enum.TryParse(raw, true, out parsed) ? parsed : InvoicePaperFormat.Thermal80;
        }

        public void SetDefaultPaperFormat(InvoicePaperFormat format)
        {
            if (_settings != null) _settings.Set(DefaultPaperFormatKey, format.ToString());
        }

        public string GetLastPrinterName()
        {
            return _settings == null ? string.Empty : _settings.Get(LastPrinterNameKey, string.Empty);
        }

        public InvoicePrintResult PrintInvoice(
            InvoiceResult committedInvoice,
            InvoicePreviewResult preview,
            InvoicePaperFormat paperFormat,
            BillingShopHeader shopHeader,
            string customerName,
            string customerPhone,
            string customerAddress,
            string customerGstin,
            IEnumerable tenderLines)
        {
            if (committedInvoice == null || !committedInvoice.Succeeded || preview == null)
            {
                return new InvoicePrintResult
                {
                    Printed = false,
                    ErrorMessage = "Only a saved invoice can be printed."
                };
            }

            try
            {
                var dialog = new PrintDialog();
                TryRestoreLastPrinter(dialog);

                bool? confirmed = dialog.ShowDialog();
                if (confirmed != true)
                {
                    return new InvoicePrintResult { Printed = false, CancelledByUser = true };
                }

                var visual = new InvoicePaperPreviewControl
                {
                    Preview = preview,
                    CommittedInvoice = committedInvoice,
                    PaperFormat = paperFormat,
                    Shop = shopHeader,
                    CustomerName = customerName,
                    CustomerPhone = customerPhone,
                    CustomerAddress = customerAddress,
                    CustomerGstin = customerGstin,
                    TenderLines = tenderLines,
                    ZoomPercent = 100.0
                };

                bool isThermal = paperFormat == InvoicePaperFormat.Thermal58 || paperFormat == InvoicePaperFormat.Thermal80;
                double margin = isThermal ? 4.0 : 18.0;
                Size pageSize = GetTargetDipSize(paperFormat, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
                double contentWidth = Math.Max(40.0, pageSize.Width - (2.0 * margin));

                visual.Measure(new Size(contentWidth, double.PositiveInfinity));
                double measuredHeight = Math.Max(40.0, visual.DesiredSize.Height);
                double contentHeight = isThermal ? measuredHeight : Math.Max(40.0, pageSize.Height - (2.0 * margin));
                double layoutHeight = isThermal ? measuredHeight : Math.Max(measuredHeight, contentHeight);

                visual.Arrange(new Rect(new Point(0, 0), new Size(contentWidth, layoutHeight)));
                visual.UpdateLayout();

                string jobTitle = "ORVA Invoice " + (committedInvoice.InvoiceNumber ?? string.Empty);
                if (isThermal)
                {
                    var host = new Border { Padding = new Thickness(margin), Child = visual };
                    host.Measure(new Size(pageSize.Width, double.PositiveInfinity));
                    host.Arrange(new Rect(new Point(0, 0), new Size(pageSize.Width, host.DesiredSize.Height)));
                    host.UpdateLayout();
                    dialog.PrintVisual(host, jobTitle);
                }
                else
                {
                    var paginator = new VisualSlicePaginator(visual, pageSize, margin, measuredHeight);
                    dialog.PrintDocument(paginator, jobTitle);
                }

                string printerName = dialog.PrintQueue != null ? dialog.PrintQueue.FullName : string.Empty;
                if (_settings != null && !string.IsNullOrWhiteSpace(printerName))
                {
                    _settings.Set(LastPrinterNameKey, printerName);
                    _settings.Set(DefaultPaperFormatKey, paperFormat.ToString());
                }

                return new InvoicePrintResult
                {
                    Printed = true,
                    PrinterName = printerName
                };
            }
            catch (Exception ex)
            {
                return new InvoicePrintResult
                {
                    Printed = false,
                    ErrorMessage = "Printing could not be completed. Your invoice is safely saved. (" + ex.Message + ")"
                };
            }
        }

        private void TryRestoreLastPrinter(PrintDialog dialog)
        {
            string savedName = GetLastPrinterName();
            if (string.IsNullOrWhiteSpace(savedName)) return;

            try
            {
                using (var server = new LocalPrintServer())
                {
                    foreach (PrintQueue queue in server.GetPrintQueues())
                    {
                        if (string.Equals(queue.FullName, savedName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(queue.Name, savedName, StringComparison.OrdinalIgnoreCase))
                        {
                            dialog.PrintQueue = queue;
                            break;
                        }
                        queue.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore if saved printer is disconnected or unavailable.
            }
        }

        public static Size GetTargetDipSize(InvoicePaperFormat format, double printableWidth, double printableHeight)
        {
            double width;
            double height;
            switch (format)
            {
                case InvoicePaperFormat.Thermal58:
                    width = 220.0; height = 0.0; break;
                case InvoicePaperFormat.Thermal80:
                    width = 304.0; height = 0.0; break;
                case InvoicePaperFormat.A5:
                    width = 559.0; height = 794.0; break;
                default:
                    width = 794.0; height = 1123.0; break;
            }

            if (printableWidth > 0 && printableWidth < width) width = printableWidth;
            if (height > 0 && printableHeight > 0 && printableHeight < height) height = printableHeight;
            return new Size(width, height);
        }

        private sealed class VisualSlicePaginator : DocumentPaginator
        {
            private readonly Visual _sourceVisual;
            private readonly double _margin;
            private readonly double _totalHeight;
            private Size _pageSize;

            public VisualSlicePaginator(Visual sourceVisual, Size pageSize, double margin, double totalHeight)
            {
                _sourceVisual = sourceVisual;
                _pageSize = pageSize;
                _margin = Math.Max(0.0, margin);
                _totalHeight = Math.Max(1.0, totalHeight);
            }

            private double ContentWidth { get { return Math.Max(1.0, _pageSize.Width - (2.0 * _margin)); } }
            private double ContentHeight { get { return Math.Max(1.0, _pageSize.Height - (2.0 * _margin)); } }

            public override bool IsPageCountValid { get { return true; } }

            public override int PageCount
            {
                get { return Math.Max(1, (int)Math.Ceiling(_totalHeight / ContentHeight)); }
            }

            public override Size PageSize
            {
                get { return _pageSize; }
                set { _pageSize = value; }
            }

            public override IDocumentPaginatorSource Source { get { return null; } }

            public override DocumentPage GetPage(int pageNumber)
            {
                double cw = ContentWidth;
                double ch = ContentHeight;
                double yOffset = pageNumber * ch;
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    var brush = new VisualBrush(_sourceVisual)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, yOffset, cw, ch)
                    };
                    dc.DrawRectangle(brush, null, new Rect(_margin, _margin, cw, ch));
                }
                return new DocumentPage(dv, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
            }
        }
    }
}
