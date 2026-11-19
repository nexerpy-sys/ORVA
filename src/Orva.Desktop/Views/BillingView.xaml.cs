using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Orva.Controls;
using Orva.Domain.Billing;
using Orva.ViewModels;

namespace Orva.Views
{
    /// <summary>
    /// New Billing workspace. The billing service arrives through constructor injection;
    /// this view never touches App.Services or any static service locator.
    ///
    /// Focus policy: the medicine search box is the normal focus target, so a keyboard-wedge
    /// USB barcode scanner can send the next barcode immediately after an add.
    ///
    /// Lifetime: one instance per navigation. Unloaded stops the view model's debounce timers,
    /// invalidates in-flight requests and closes the owned preview window.
    /// </summary>
    public partial class BillingView : UserControl
    {
        private BillingPreviewWindow _previewWindow;
        private bool _isUnloaded;

        public BillingView(IBillingService billingService)
            : this(billingService, null)
        {
        }

        public BillingView(IBillingService billingService, BillingShopHeader shopHeader)
        {
            if (billingService == null) throw new ArgumentNullException("billingService");

            ViewModel = new BillingViewModel(billingService, shopHeader);
            DataContext = ViewModel;
            InitializeComponent();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        /// <summary>Exposed so the integrator can subscribe to PrintRequested later.</summary>
        public BillingViewModel ViewModel { get; private set; }

        private void BillingView_Loaded(object sender, RoutedEventArgs e)
        {
            FocusSearch();
        }

        private void BillingView_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.Shutdown();
            ClosePreviewWindow();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isUnloaded) return;
            if (!string.Equals(e.PropertyName, "IsCommitted", StringComparison.Ordinal)) return;
            if (!ViewModel.IsCommitted) return;

            // Show the committed invoice number on the paper preview as an immediate next
            // action. The draft stays on screen until staff start a new bill.
            OpenPreviewWindow();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!_isUnloaded) NewBillButton.Focus();
            }), DispatcherPriority.Input);
        }

        // ---- Global shortcuts -------------------------------------------------------

        private void BillingView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                FocusSearch();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F8 && !ViewModel.IsBusy && !ViewModel.IsCommitted)
            {
                ViewModel.ApplyExactPayment(InvoicePaymentMethod.Cash);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F9 && !ViewModel.IsBusy && !ViewModel.IsCommitted)
            {
                ViewModel.ApplyExactPayment(InvoicePaymentMethod.Upi);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && ViewModel.IsCommitted)
            {
                Print_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && ViewModel.IsCommitted)
            {
                NewBill_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (ViewModel.FinalizeCommand.CanExecute(null))
                {
                    ViewModel.FinalizeCommand.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Delete)
            {
                // Never steal Delete from normal text entry, search results, customer fields or payment controls.
                if (Keyboard.FocusedElement is TextBox) return;
                if (Keyboard.FocusedElement is ComboBox) return;
                if (ViewModel.IsBusy || ViewModel.IsCommitted) return;
                if (CartList == null || !CartList.IsKeyboardFocusWithin) return;

                var line = ViewModel.SelectedCartLine;
                if (line == null) return;
                if (ViewModel.RemoveLineCommand.CanExecute(line))
                {
                    ViewModel.RemoveLineCommand.Execute(line);
                    e.Handled = true;
                }
            }
        }

        // ---- Search -----------------------------------------------------------------

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // The view model decides whether the current selection is valid for the
                // currently typed query; a stale selection can never be accepted here.
                ViewModel.AcceptSearchInput();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveFocusToResults();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel.SearchText = string.Empty;
                e.Handled = true;
            }
        }

        private void MoveFocusToResults()
        {
            if (ResultsList.Items.Count == 0) return;
            if (ResultsList.SelectedIndex < 0) ResultsList.SelectedIndex = 0;

            var container = ResultsList.ItemContainerGenerator.ContainerFromIndex(ResultsList.SelectedIndex) as ListBoxItem;
            if (container != null)
            {
                container.Focus();
            }
            else
            {
                ResultsList.Focus();
            }
        }

        private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.AcceptSearchInput();
                FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel.SearchText = string.Empty;
                FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ResultsList.SelectedIndex <= 0)
            {
                FocusSearch();
                e.Handled = true;
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var hit = ResultsList.SelectedItem as MedicineSearchHitViewModel;
            if (hit == null || !hit.CanSell || ViewModel.IsCommitted || ViewModel.IsBusy) return;

            ViewModel.AddMedicineCommand.Execute(hit);
            FocusSearch();
            e.Handled = true;
        }

        private void AddResult_Click(object sender, RoutedEventArgs e)
        {
            // The command has already run through its binding; only restore the scanner focus.
            FocusSearch();
        }

        // ---- Cart quantity ----------------------------------------------------------

        private void QuantityBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            var line = box.DataContext as CartLineViewModel;
            if (line != null) CartList.SelectedItem = line;

            // Select the existing quantity so staff can overtype it without clearing.
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (box.IsKeyboardFocusWithin) box.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void QuantityBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            var line = box.DataContext as CartLineViewModel;
            if (line == null) return;

            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                line.CommitQuantityText();
                FocusSearch();
                e.Handled = true;
            }
        }

        private void QuantityBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            var line = box.DataContext as CartLineViewModel;
            if (line != null) line.CommitQuantityText();
        }

        // ---- Payment amount ---------------------------------------------------------

        private void AmountBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            var tender = box.DataContext as PaymentTenderViewModel;
            if (tender == null) return;

            if (e.Key == Key.Enter)
            {
                tender.CommitAmountText();
                e.Handled = true;
            }
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            var tender = box.DataContext as PaymentTenderViewModel;
            if (tender != null) tender.CommitAmountText();
        }

        // ---- Commands with a view-side step -----------------------------------------

        private void ClearBill_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy) return;
            if (!ViewModel.HasCartItems && !ViewModel.HasTenders)
            {
                ViewModel.ClearDraft();
                FocusSearch();
                return;
            }

            var answer = MessageBox.Show(
                Window.GetWindow(this),
                BillingText.Get("BillClearConfirmMessage",
                    "All medicines and payments entered for this bill will be removed. Saved invoices are not affected."),
                BillingText.Get("BillClearConfirmTitle", "Clear this bill?"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer == MessageBoxResult.Yes)
            {
                ViewModel.ClearDraft();
                FocusSearch();
            }
        }

        private void NewBill_Click(object sender, RoutedEventArgs e)
        {
            FocusSearch();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            // Printing is a separate integration concern. Until the integrator subscribes to
            // ViewModel.PrintRequested, show a safe, honest message instead of failing silently.
            if (!ViewModel.RaisePrintRequest())
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    BillingText.Get("BillPrintNotReady",
                        "Printing is not set up yet. The invoice is saved safely."),
                    "ORVA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ---- Live paper preview window ----------------------------------------------

        private void PreviewToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_previewWindow == null)
            {
                OpenPreviewWindow();
            }
            else
            {
                ClosePreviewWindow();
                FocusSearch();
            }
        }

        private void OpenPreviewWindow()
        {
            if (_isUnloaded) return;

            if (_previewWindow != null)
            {
                _previewWindow.Activate();
                return;
            }

            var window = new BillingPreviewWindow(ViewModel);
            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            window.Closed += PreviewWindow_Closed;
            _previewWindow = window;
            window.Show();
            window.Activate();
        }

        private void PreviewWindow_Closed(object sender, EventArgs e)
        {
            var window = sender as BillingPreviewWindow;
            if (window != null) window.Closed -= PreviewWindow_Closed;
            _previewWindow = null;
        }

        private void ClosePreviewWindow()
        {
            if (_previewWindow == null) return;

            var window = _previewWindow;
            _previewWindow = null;
            window.Closed -= PreviewWindow_Closed;
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
                // The window was already closing (for example, the owner window is shutting down).
            }
        }

        // ---- Focus helper -----------------------------------------------------------

        private void FocusSearch()
        {
            if (_isUnloaded) return;
            if (SearchBox == null) return;

            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            SearchBox.SelectAll();
        }

        private void ExactCash_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ApplyExactPayment(InvoicePaymentMethod.Cash);
        }

        private void ExactUpi_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ApplyExactPayment(InvoicePaymentMethod.Upi);
        }

        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var line = btn != null ? btn.DataContext as CartLineViewModel : null;
            ViewModel.StepCartLineQuantity(line, -1m);
        }

        private void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var line = btn != null ? btn.DataContext as CartLineViewModel : null;
            ViewModel.StepCartLineQuantity(line, 1m);
        }
    }
}