using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Orva.ViewModels;

namespace Orva.Controls
{
    /// <summary>
    /// Non-modal owner window that hosts the live paper preview, the paper-format selector
    /// and the zoom slider. It binds to the same BillingViewModel as the billing workspace,
    /// so it updates live and never triggers a separate preview request.
    ///
    /// The toolbar is built in code so that the whole preview feature stays inside the
    /// permitted Controls/ViewModels file areas. Closing this window never affects the draft.
    /// </summary>
    public sealed class BillingPreviewWindow : Window
    {
        private readonly BillingViewModel _viewModel;

        public BillingPreviewWindow(BillingViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException("viewModel");
            _viewModel = viewModel;

            Title = BillingText.Get("BillPreviewWindowTitle", "ORVA — Live Invoice Preview");
            Background = ResolveBrush("Paper", Color.FromRgb(0xF3, 0xEA, 0xDA));
            FontFamily = (TryFindResource("UiBodyFont") as FontFamily) ?? new FontFamily("Noto Sans, Noto Sans Bengali");
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Width = Math.Min(900, SystemParameters.WorkArea.Width - 60);
            Height = Math.Min(820, SystemParameters.WorkArea.Height - 60);
            MinWidth = 640;
            MinHeight = 420;

            Content = BuildContent();
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private UIElement BuildContent()
        {
            var root = new DockPanel { LastChildFill = true };

            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var preview = new InvoicePaperPreviewControl();
            preview.SetBinding(InvoicePaperPreviewControl.PreviewProperty, Bind("CurrentPreview"));
            preview.SetBinding(InvoicePaperPreviewControl.CommittedInvoiceProperty, Bind("CommittedInvoice"));
            preview.SetBinding(InvoicePaperPreviewControl.PaperFormatProperty, Bind("SelectedPaperFormat"));
            preview.SetBinding(InvoicePaperPreviewControl.ShopProperty, Bind("ShopHeader"));
            preview.SetBinding(InvoicePaperPreviewControl.CustomerNameProperty, Bind("CustomerName"));
            preview.SetBinding(InvoicePaperPreviewControl.CustomerPhoneProperty, Bind("CustomerPhone"));
            preview.SetBinding(InvoicePaperPreviewControl.CustomerAddressProperty, Bind("CustomerAddress"));
            preview.SetBinding(InvoicePaperPreviewControl.CustomerGstinProperty, Bind("CustomerGstin"));
            preview.SetBinding(InvoicePaperPreviewControl.TenderLinesProperty, Bind("Tenders"));
            preview.SetBinding(InvoicePaperPreviewControl.ZoomPercentProperty, Bind("PreviewZoomPercent"));

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(24),
                Background = ResolveBrush("PaperRecessed", Color.FromRgb(0xE7, 0xDC, 0xC6)),
                Content = preview
            };
            root.Children.Add(scroll);

            return root;
        }

        private UIElement BuildToolbar()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var paperLabel = new TextBlock
            {
                Text = BillingText.Get("BillPaperFormatLabel", "Paper"),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5
            };
            Grid.SetColumn(paperLabel, 0);
            grid.Children.Add(paperLabel);

            var formatBox = new ComboBox
            {
                MinWidth = 165,
                Margin = new Thickness(10, 0, 0, 0),
                DisplayMemberPath = "DisplayName",
                VerticalAlignment = VerticalAlignment.Center
            };
            formatBox.SetBinding(ItemsControl.ItemsSourceProperty, Bind("PaperFormats"));
            formatBox.SetBinding(Selector.SelectedItemProperty, BindTwoWay("SelectedPaperFormatOption"));
            Grid.SetColumn(formatBox, 1);
            grid.Children.Add(formatBox);

            var updating = new TextBlock
            {
                Text = BillingText.Get("BillUpdatingTotals", "Updating totals…"),
                Foreground = ResolveBrush("Vermilion", Color.FromRgb(0xFF, 0x4B, 0x1F)),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            updating.SetBinding(VisibilityProperty, new Binding("IsPreviewPending")
            {
                Source = _viewModel,
                Converter = new BoolToVisibilityConverter()
            });
            Grid.SetColumn(updating, 2);
            grid.Children.Add(updating);

            var zoomLabel = new TextBlock
            {
                Text = BillingText.Get("BillZoom", "Zoom"),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Margin = new Thickness(16, 0, 0, 0)
            };
            Grid.SetColumn(zoomLabel, 4);
            grid.Children.Add(zoomLabel);

            var zoomSlider = new Slider
            {
                Minimum = 50,
                Maximum = 250,
                SmallChange = 5,
                LargeChange = 25,
                MinWidth = 170,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsSnapToTickEnabled = false
            };
            zoomSlider.SetBinding(RangeBase.ValueProperty, BindTwoWay("PreviewZoomPercent"));
            Grid.SetColumn(zoomSlider, 5);
            grid.Children.Add(zoomSlider);

            var zoomText = new TextBlock
            {
                MinWidth = 48,
                Margin = new Thickness(10, 0, 0, 0),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12.5
            };
            zoomText.SetBinding(TextBlock.TextProperty, new Binding("PreviewZoomPercent")
            {
                Source = _viewModel,
                StringFormat = "{0:0}%"
            });
            Grid.SetColumn(zoomText, 6);
            grid.Children.Add(zoomText);

            return new Border
            {
                Background = ResolveBrush("PaperRaised", Color.FromRgb(0xFB, 0xF7, 0xEF)),
                BorderBrush = ResolveBrush("Rule", Color.FromRgb(0xD5, 0xC9, 0xB4)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 12, 16, 12),
                Child = grid
            };
        }

        private Binding Bind(string path)
        {
            return new Binding(path) { Source = _viewModel, Mode = BindingMode.OneWay };
        }

        private Binding BindTwoWay(string path)
        {
            return new Binding(path) { Source = _viewModel, Mode = BindingMode.TwoWay };
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }

        private static Brush ResolveBrush(string key, Color fallback)
        {
            var app = Application.Current;
            var brush = app == null ? null : app.TryFindResource(key) as Brush;
            if (brush != null) return brush;

            var solid = new SolidColorBrush(fallback);
            solid.Freeze();
            return solid;
        }
    }
}