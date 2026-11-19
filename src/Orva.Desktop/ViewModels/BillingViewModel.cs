using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Threading;
using Orva.Domain.Billing;

namespace Orva.ViewModels
{
    /// <summary>
    /// Interaction state for the New Billing workspace.
    ///
    /// Ownership rules honoured here:
    /// - The only business dependency is the injected, frozen IBillingService contract.
    /// - All money is long paise and all quantity is long thousandths. decimal appears only
    ///   inside BillingDisplay at the parse/format boundary. No double/float arithmetic is
    ///   used for money or stock (double is used solely for the visual zoom factor).
    /// - Tax, FEFO allocation, rounding, prices and stock state are never computed here.
    ///   Every displayed total is copied from InvoicePreviewResult / InvoiceResult.
    ///
    /// Threading model: constructed on the WPF UI thread. Service calls run through Task.Run;
    /// continuations resume on the captured Dispatcher synchronization context. Monotonic
    /// request versions plus a draft revision counter guarantee that a stale response can
    /// never overwrite newer state. Both search and preview are single-flight with a queued
    /// re-run flag, so rapid typing cannot spawn an unbounded number of tasks.
    ///
    /// async void is used only for the three internal, event-driven operations below; each
    /// one wraps its whole body in try/catch and logs technical detail locally.
    /// </summary>
    public sealed class BillingViewModel : ObservableObject
    {
        private const int SearchPageSize = 20;
        private const long DefaultAddQuantityMilli = 1000;
        private const long MaxQuantityMilli = 999999999000L;
        private const long MaxAmountPaise = 99999999900L;
        private const double MinZoomPercent = 50.0;
        private const double MaxZoomPercent = 250.0;
        private const int DebounceMilliseconds = 250;

        private readonly IBillingService _billingService;
        private readonly DispatcherTimer _searchTimer;
        private readonly DispatcherTimer _previewTimer;

        private readonly List<CartLineViewModel> _watchedCartLines = new List<CartLineViewModel>();
        private readonly List<PaymentTenderViewModel> _watchedTenders = new List<PaymentTenderViewModel>();

        // Request/version state. All increments and comparisons happen on the UI thread.
        private long _searchVersion;
        private long _previewVersion;
        private long _draftRevision;
        private bool _searchRunning;
        private bool _searchQueued;
        private bool _previewRunning;
        private bool _previewQueued;
        private bool _acceptWhenSearchCompletes;
        private string _resultsQuery;
        private bool _suppressDraftChanges;
        private bool _isShutdown;

        private string _searchText = string.Empty;
        private bool _isSearching;
        private MedicineSearchHitViewModel _selectedSearchResult;
        private CartLineViewModel _selectedCartLine;

        private string _customerName = string.Empty;
        private string _customerPhone = string.Empty;
        private string _customerAddress = string.Empty;
        private string _customerGstin = string.Empty;
        private bool _isCustomerExpanded;

        private InvoicePreviewResult _currentPreview;
        private InvoicePreviewResult _committedPreview;
        private InvoiceResult _committedInvoice;
        private bool _isPreviewCurrent;
        private bool _isPreviewPending;
        private bool _canFinalize;
        private bool _isBusy;
        private bool _isCommitted;
        private bool _hasInputErrors;

        private string _subtotalText = "0.00";
        private string _cgstText = "0.00";
        private string _sgstText = "0.00";
        private string _igstText = "0.00";
        private string _roundOffText = "0.00";
        private string _grandTotalText = "0.00";
        private string _tenderedText = "0.00";
        private string _remainingText = "0.00";
        private bool _showCgstSgst;
        private bool _showIgst;
        private bool _showRoundOff;
        private bool _isFullyPaid;
        private bool _isOverTendered;

        private string _statusMessage = string.Empty;
        private bool _statusIsError;

        private string _lastInvoiceNumber = string.Empty;
        private string _lastInvoiceIssuedText = string.Empty;
        private string _lastInvoiceTotalText = string.Empty;

        private PaperFormatOption _selectedPaperFormatOption;
        private double _previewZoomPercent = 100.0;

        public BillingViewModel(IBillingService billingService)
            : this(billingService, null)
        {
        }

        public BillingViewModel(IBillingService billingService, BillingShopHeader shopHeader)
        {
            if (billingService == null) throw new ArgumentNullException("billingService");
            _billingService = billingService;

            ShopHeader = shopHeader ?? new BillingShopHeader();

            SearchResults = new ObservableCollection<MedicineSearchHitViewModel>();
            CartLines = new ObservableCollection<CartLineViewModel>();
            Tenders = new ObservableCollection<PaymentTenderViewModel>();
            Issues = new ObservableCollection<BillingIssue>();

            PaymentMethods = new List<PaymentMethodOption>
            {
                new PaymentMethodOption(InvoicePaymentMethod.Cash, BillingText.Get("BillMethodCash", "Cash")),
                new PaymentMethodOption(InvoicePaymentMethod.Upi, BillingText.Get("BillMethodUpi", "UPI")),
                new PaymentMethodOption(InvoicePaymentMethod.DebitCard, BillingText.Get("BillMethodDebitCard", "Debit Card")),
                new PaymentMethodOption(InvoicePaymentMethod.CreditCard, BillingText.Get("BillMethodCreditCard", "Credit Card"))
            };

            PaperFormats = new List<PaperFormatOption>
            {
                new PaperFormatOption(InvoicePaperFormat.Thermal58, BillingText.Get("BillPaper58", "58 mm receipt")),
                new PaperFormatOption(InvoicePaperFormat.Thermal80, BillingText.Get("BillPaper80", "80 mm receipt")),
                new PaperFormatOption(InvoicePaperFormat.A4, BillingText.Get("BillPaperA4", "A4 page")),
                new PaperFormatOption(InvoicePaperFormat.A5, BillingText.Get("BillPaperA5", "A5 page"))
            };
            _selectedPaperFormatOption = PaperFormats[1];

            AddMedicineCommand = new RelayCommand(
                delegate(object parameter) { AddMedicine(parameter as MedicineSearchHitViewModel); },
                delegate(object parameter)
                {
                    var hit = parameter as MedicineSearchHitViewModel;
                    return hit != null && hit.CanSell && !_isCommitted && !_isBusy && !_isShutdown;
                });

            RemoveLineCommand = new RelayCommand(
                delegate(object parameter) { RemoveLine(parameter as CartLineViewModel); },
                delegate(object parameter) { return parameter is CartLineViewModel && !_isCommitted && !_isBusy && !_isShutdown; });

            AddTenderCommand = new RelayCommand(
                () => { ExecuteAddTender(); },
                () => { return !_isCommitted && !_isBusy && !_isShutdown; });

            RemoveTenderCommand = new RelayCommand(
                delegate(object parameter) { RemoveTender(parameter as PaymentTenderViewModel); },
                delegate(object parameter) { return parameter is PaymentTenderViewModel && !_isCommitted && !_isBusy && !_isShutdown; });

            UseRemainingCommand = new RelayCommand(
                delegate(object parameter) { ExecuteUseRemaining(parameter as PaymentTenderViewModel); },
                delegate(object parameter) { return parameter is PaymentTenderViewModel && !_isCommitted && !_isBusy && !_isShutdown; });

            FinalizeCommand = new RelayCommand(
                () => { ExecuteFinalize(); },
                () => { return ComputeGenerateEnabled(); });

            NewBillCommand = new RelayCommand(() => { ClearDraft(); }, () => { return !_isBusy && !_isShutdown; });

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds) };
            _searchTimer.Tick += SearchTimer_Tick;

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds) };
            _previewTimer.Tick += PreviewTimer_Tick;

            SearchResults.CollectionChanged += SearchResults_CollectionChanged;
            CartLines.CollectionChanged += CartLines_CollectionChanged;
            Tenders.CollectionChanged += Tenders_CollectionChanged;
            Issues.CollectionChanged += Issues_CollectionChanged;

            ResetPreviewToEmpty();
        }

        /// <summary>Raised when staff press Print on a committed invoice. Printing is integrated separately.</summary>
        public event EventHandler<InvoicePrintRequestedEventArgs> PrintRequested;

        // ---- Collections and options -------------------------------------------------

        public ObservableCollection<MedicineSearchHitViewModel> SearchResults { get; private set; }
        public ObservableCollection<CartLineViewModel> CartLines { get; private set; }
        public ObservableCollection<PaymentTenderViewModel> Tenders { get; private set; }
        public ObservableCollection<BillingIssue> Issues { get; private set; }
        public IList<PaymentMethodOption> PaymentMethods { get; private set; }
        public IList<PaperFormatOption> PaperFormats { get; private set; }
        public BillingShopHeader ShopHeader { get; private set; }

        // ---- Commands ----------------------------------------------------------------

        public RelayCommand AddMedicineCommand { get; private set; }
        public RelayCommand RemoveLineCommand { get; private set; }
        public RelayCommand AddTenderCommand { get; private set; }
        public RelayCommand RemoveTenderCommand { get; private set; }
        public RelayCommand UseRemainingCommand { get; private set; }
        public RelayCommand FinalizeCommand { get; private set; }
        public RelayCommand NewBillCommand { get; private set; }

        // ---- Search ------------------------------------------------------------------

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (_isBusy || _isCommitted || _isShutdown) return;
                var text = value ?? string.Empty;
                if (string.Equals(_searchText, text, StringComparison.Ordinal)) return;

                _searchText = text;
                RaisePropertyChanged("SearchText");

                // DEFECT FIX 1: a changed query must never leave an old result selected.
                // The list, the selection and the query stamp are cleared synchronously,
                // so a fast scanner Enter cannot commit the previously highlighted medicine.
                InvalidateSearchResults();

                if (text.Trim().Length == 0)
                {
                    IsSearching = false;
                    return;
                }

                IsSearching = true;
                _searchTimer.Start();
            }
        }

        public bool IsSearching
        {
            get { return _isSearching; }
            private set
            {
                if (!SetProperty(ref _isSearching, value, "IsSearching")) return;
                RaisePropertyChanged("ShowNoSearchResults");
                RaisePropertyChanged("ShowSearchOverlay");
            }
        }

        public bool HasSearchResults { get { return SearchResults.Count > 0; } }

        public bool ShowNoSearchResults
        {
            get { return !_isSearching && _resultsQuery != null && SearchResults.Count == 0 && !string.IsNullOrWhiteSpace(_searchText); }
        }

        public bool ShowSearchOverlay { get { return _isSearching || SearchResults.Count > 0 || ShowNoSearchResults; } }

        public MedicineSearchHitViewModel SelectedSearchResult
        {
            get { return _selectedSearchResult; }
            set { SetProperty(ref _selectedSearchResult, value, "SelectedSearchResult"); }
        }

        private void InvalidateSearchResults()
        {
            _searchTimer.Stop();
            _searchVersion++;
            _acceptWhenSearchCompletes = false;
            _resultsQuery = null;

            // Clear the selection before the list so the ListBox cannot push a stale item back.
            SelectedSearchResult = null;
            SearchResults.Clear();
            RaisePropertyChanged("ShowNoSearchResults");
            RaisePropertyChanged("ShowSearchOverlay");
        }

        private void SearchTimer_Tick(object sender, EventArgs e)
        {
            _searchTimer.Stop();
            RunSearchNow();
        }

        /// <summary>
        /// Enter in the search box or the result list. Accepts only a selection that belongs
        /// to the currently typed query; otherwise performs an immediate versioned search so
        /// keyboard-wedge scanners that send Enter before the debounce still behave correctly.
        /// </summary>
        public void AcceptSearchInput()
        {
            if (_isShutdown || _isCommitted || _isBusy) return;

            var query = (_searchText ?? string.Empty).Trim();
            if (query.Length == 0) return;

            var resultsMatchQuery = _resultsQuery != null
                && string.Equals(_resultsQuery, query, StringComparison.Ordinal);

            if (resultsMatchQuery)
            {
                var selected = _selectedSearchResult;
                if (selected != null && SearchResults.Contains(selected))
                {
                    AddMedicine(selected);
                    return;
                }
                if (SearchResults.Count == 1)
                {
                    AddMedicine(SearchResults[0]);
                    return;
                }
                if (SearchResults.Count > 1)
                {
                    SelectedSearchResult = SearchResults[0];
                    SetStatus(BillingText.Get("BillStatusSelectResult",
                        "Press Enter again to add the highlighted medicine."), false);
                    return;
                }

                SetStatus(BillingText.Get("BillStatusNotFound", "No matching medicine was found."), false);
                return;
            }

            // The visible results (if any) are not for this text. Never accept them.
            _searchTimer.Stop();
            _acceptWhenSearchCompletes = true;
            RunSearchNow();
        }

        private async void RunSearchNow()
        {
            if (_isShutdown) return;

            var query = (_searchText ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                InvalidateSearchResults();
                IsSearching = false;
                return;
            }

            if (_searchRunning)
            {
                _searchQueued = true;
                return;
            }

            var version = ++_searchVersion;
            _searchRunning = true;
            IsSearching = true;

            try
            {
                var results = await Task.Run(delegate
                {
                    return _billingService.SearchMedicines(query, SearchPageSize);
                });

                if (_isShutdown || version != _searchVersion) return;
                ApplySearchResults(query, results);
            }
            catch (Exception ex)
            {
                if (_isShutdown || version != _searchVersion) return;
                BillingDiagnostics.Log("SearchMedicines", ex);
                _resultsQuery = null;
                SelectedSearchResult = null;
                SearchResults.Clear();
                SetStatus(BillingText.Get("BillErrorServiceUnavailable",
                    "Billing is temporarily unavailable. Please try again. If this continues, contact support."), true);
            }
            finally
            {
                _searchRunning = false;
                if (version == _searchVersion) IsSearching = false;

                if (_searchQueued && !_isShutdown)
                {
                    _searchQueued = false;
                    RunSearchNow();
                }
            }
        }

        private void ApplySearchResults(string query, IList<MedicineLookupResult> results)
        {
            SelectedSearchResult = null;
            SearchResults.Clear();

            if (results != null)
            {
                for (var i = 0; i < results.Count && i < SearchPageSize; i++)
                {
                    var item = results[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.MedicineId)) continue;
                    SearchResults.Add(new MedicineSearchHitViewModel(item));
                }
            }

            // Stamp the exact query these rows belong to.
            _resultsQuery = query;
            RaisePropertyChanged("ShowNoSearchResults");
            RaisePropertyChanged("ShowSearchOverlay");

            if (!_acceptWhenSearchCompletes) return;
            _acceptWhenSearchCompletes = false;

            // Auto-accept only if the text has not moved on since the request was issued.
            if (!string.Equals(query, (_searchText ?? string.Empty).Trim(), StringComparison.Ordinal)) return;

            if (SearchResults.Count == 1)
            {
                AddMedicine(SearchResults[0]);
            }
            else if (SearchResults.Count > 1)
            {
                SelectedSearchResult = SearchResults[0];
                SetStatus(BillingText.Get("BillStatusSelectResult",
                    "Press Enter again to add the highlighted medicine."), false);
            }
            else
            {
                SetStatus(BillingText.Get("BillStatusNotFound", "No matching medicine was found."), false);
            }
        }

        private void SearchResults_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged("HasSearchResults");
            RaisePropertyChanged("ShowNoSearchResults");
            RaisePropertyChanged("ShowSearchOverlay");
        }

        // ---- Cart --------------------------------------------------------------------

        public CartLineViewModel SelectedCartLine
        {
            get { return _selectedCartLine; }
            set { SetProperty(ref _selectedCartLine, value, "SelectedCartLine"); }
        }

        public bool HasCartItems { get { return CartLines.Count > 0; } }

        public string CartCountText
        {
            get
            {
                var count = CartLines.Count;
                if (count == 0) return string.Empty;
                return count == 1
                    ? BillingText.Get("BillCartCountOne", "1 item")
                    : BillingText.Format("BillCartCountFormat", "{0} items", count);
            }
        }

        private void AddMedicine(MedicineSearchHitViewModel hit)
        {
            if (hit == null || _isShutdown || _isCommitted || _isBusy) return;

            if (!hit.CanSell)
            {
                SetStatus(hit.StatusText, true);
                return;
            }

            var existing = FindCartLine(hit.MedicineId);
            if (existing != null)
            {
                // One visible row per medicine: merge instead of creating a duplicate row.
                var merged = existing.QuantityMilli + DefaultAddQuantityMilli;
                if (merged > MaxQuantityMilli)
                {
                    SetStatus(BillingDisplay.DescribeQuantityError(QuantityParseStatus.TooLarge), true);
                    return;
                }
                existing.SetQuantitySilently(merged);
                SelectedCartLine = existing;
            }
            else
            {
                var line = new CartLineViewModel(hit.Source, DefaultAddQuantityMilli) { CanEdit = CanEditDraft };
                CartLines.Add(line);
                SelectedCartLine = line;
            }

            ClearSearchAfterAdd();
            SetStatus(BillingText.Format("BillStatusAddedFormat", "Added {0}.", hit.DisplayName), false);
            MarkDraftChanged();
        }

        private CartLineViewModel FindCartLine(string medicineId)
        {
            if (string.IsNullOrEmpty(medicineId)) return null;
            for (var i = 0; i < CartLines.Count; i++)
            {
                if (string.Equals(CartLines[i].MedicineId, medicineId, StringComparison.Ordinal)) return CartLines[i];
            }
            return null;
        }

        private void ClearSearchAfterAdd()
        {
            _searchTimer.Stop();
            _searchVersion++;
            _acceptWhenSearchCompletes = false;
            _resultsQuery = null;
            SelectedSearchResult = null;
            SearchResults.Clear();

            _searchText = string.Empty;
            RaisePropertyChanged("SearchText");
            IsSearching = false;
        }

        private void RemoveLine(CartLineViewModel line)
        {
            if (line == null || _isShutdown || _isCommitted || _isBusy) return;

            CartLines.Remove(line);
            if (ReferenceEquals(_selectedCartLine, line)) SelectedCartLine = null;
            MarkDraftChanged();
        }

        private void CartLines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                for (var i = 0; i < _watchedCartLines.Count; i++)
                {
                    _watchedCartLines[i].PropertyChanged -= CartLine_PropertyChanged;
                }
                _watchedCartLines.Clear();
                for (var i = 0; i < CartLines.Count; i++)
                {
                    CartLines[i].PropertyChanged += CartLine_PropertyChanged;
                    _watchedCartLines.Add(CartLines[i]);
                }
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (object item in e.OldItems)
                    {
                        var row = item as CartLineViewModel;
                        if (row == null) continue;
                        row.PropertyChanged -= CartLine_PropertyChanged;
                        _watchedCartLines.Remove(row);
                    }
                }
                if (e.NewItems != null)
                {
                    foreach (object item in e.NewItems)
                    {
                        var row = item as CartLineViewModel;
                        if (row == null) continue;
                        row.PropertyChanged += CartLine_PropertyChanged;
                        _watchedCartLines.Add(row);
                    }
                }
            }

            RaisePropertyChanged("HasCartItems");
            RaisePropertyChanged("CartCountText");
            RaisePropertyChanged("CanClearBill");
            RefreshInputErrorState();
        }

        private void CartLine_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isShutdown || _suppressDraftChanges || _isCommitted) return;
            if (!string.Equals(e.PropertyName, "QuantityMilli", StringComparison.Ordinal)
                && !string.Equals(e.PropertyName, "HasError", StringComparison.Ordinal)) return;

            RefreshInputErrorState();
            MarkDraftChanged();
        }

        // ---- Payments ----------------------------------------------------------------

        public bool HasTenders { get { return Tenders.Count > 0; } }

        private void ExecuteAddTender()
        {
            if (_isShutdown || _isCommitted || _isBusy) return;

            // Pre-fill the outstanding amount so a normal single-tender sale needs no typing.
            var remaining = _currentPreview == null ? 0L : _currentPreview.AmountRemainingPaise;
            var initial = remaining > 0 ? remaining : 0L;

            var tender = new PaymentTenderViewModel(PaymentMethods, PaymentMethods[0], initial) { CanEdit = CanEditDraft };
            Tenders.Add(tender);
            MarkDraftChanged();
        }

        private void RemoveTender(PaymentTenderViewModel tender)
        {
            if (tender == null || _isShutdown || _isCommitted || _isBusy) return;
            Tenders.Remove(tender);
            MarkDraftChanged();
        }

        private void ExecuteUseRemaining(PaymentTenderViewModel tender)
        {
            if (tender == null || _isShutdown || _isCommitted || _isBusy) return;

            // Tender arithmetic assistance only. This is not a tax, price or rounding rule:
            // the outstanding amount already excludes this row's current amount.
            if (!_isPreviewCurrent) return;
            var remaining = _currentPreview == null ? 0L : _currentPreview.AmountRemainingPaise;
            long contributed = tender.HasError ? 0L : tender.AmountPaise;
            var target = remaining + contributed;

            if (target <= 0)
            {
                SetStatus(BillingDisplay.DescribeMoneyError(MoneyParseStatus.NotPositive), true);
                return;
            }
            if (target > MaxAmountPaise)
            {
                SetStatus(BillingDisplay.DescribeMoneyError(MoneyParseStatus.TooLarge), true);
                return;
            }

            tender.SetAmountSilently(target);
        }

        private void Tenders_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                for (var i = 0; i < _watchedTenders.Count; i++)
                {
                    _watchedTenders[i].PropertyChanged -= Tender_PropertyChanged;
                }
                _watchedTenders.Clear();
                for (var i = 0; i < Tenders.Count; i++)
                {
                    Tenders[i].PropertyChanged += Tender_PropertyChanged;
                    _watchedTenders.Add(Tenders[i]);
                }
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (object item in e.OldItems)
                    {
                        var row = item as PaymentTenderViewModel;
                        if (row == null) continue;
                        row.PropertyChanged -= Tender_PropertyChanged;
                        _watchedTenders.Remove(row);
                    }
                }
                if (e.NewItems != null)
                {
                    foreach (object item in e.NewItems)
                    {
                        var row = item as PaymentTenderViewModel;
                        if (row == null) continue;
                        row.PropertyChanged += Tender_PropertyChanged;
                        _watchedTenders.Add(row);
                    }
                }
            }

            RaisePropertyChanged("HasTenders");
            RaisePropertyChanged("CanClearBill");
            RefreshInputErrorState();
        }

        private void Tender_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isShutdown || _suppressDraftChanges || _isCommitted) return;

            var name = e.PropertyName;
            if (!string.Equals(name, "AmountPaise", StringComparison.Ordinal)
                && !string.Equals(name, "HasError", StringComparison.Ordinal)
                && !string.Equals(name, "Method", StringComparison.Ordinal)
                && !string.Equals(name, "ReferenceText", StringComparison.Ordinal)) return;

            RefreshInputErrorState();
            MarkDraftChanged();
        }

        // ---- Optional customer -------------------------------------------------------

        public string CustomerName
        {
            get { return _customerName; }
            set
            {
                if (_isBusy || _isCommitted || _isShutdown) return;
                if (!SetProperty(ref _customerName, value ?? string.Empty, "CustomerName")) return;
                RaisePropertyChanged("ShowHighValueB2cReminder");
                MarkDraftChanged();
            }
        }

        public string CustomerPhone
        {
            get { return _customerPhone; }
            set
            {
                if (_isBusy || _isCommitted || _isShutdown) return;
                if (!SetProperty(ref _customerPhone, value ?? string.Empty, "CustomerPhone")) return;
                MarkDraftChanged();
            }
        }

        public string CustomerAddress
        {
            get { return _customerAddress; }
            set
            {
                if (_isBusy || _isCommitted || _isShutdown) return;
                if (!SetProperty(ref _customerAddress, value ?? string.Empty, "CustomerAddress")) return;
                RaisePropertyChanged("ShowHighValueB2cReminder");
                MarkDraftChanged();
            }
        }

        public string CustomerGstin
        {
            get { return _customerGstin; }
            set
            {
                if (_isBusy || _isCommitted || _isShutdown) return;
                if (!SetProperty(ref _customerGstin, value ?? string.Empty, "CustomerGstin")) return;
                RaisePropertyChanged("ShowHighValueB2cReminder");
                MarkDraftChanged();
            }
        }

        public bool IsCustomerExpanded
        {
            get { return _isCustomerExpanded; }
            set { SetProperty(ref _isCustomerExpanded, value, "IsCustomerExpanded"); }
        }

        // ---- Preview state -----------------------------------------------------------

        public InvoicePreviewResult CurrentPreview
        {
            get { return _currentPreview; }
            private set
            {
                if (!SetProperty(ref _currentPreview, value, "CurrentPreview")) return;
                RaisePropertyChanged("HasPreview");
            }
        }

        public bool HasPreview { get { return _currentPreview != null; } }

        /// <summary>True only while the displayed totals belong to the current draft.</summary>
        public bool IsPreviewCurrent
        {
            get { return _isPreviewCurrent; }
            private set
            {
                if (!SetProperty(ref _isPreviewCurrent, value, "IsPreviewCurrent")) return;
                RefreshGenerateState();
            }
        }

        /// <summary>True from the instant the draft changes until a matching preview is applied.</summary>
        public bool IsPreviewPending
        {
            get { return _isPreviewPending; }
            private set { SetProperty(ref _isPreviewPending, value, "IsPreviewPending"); }
        }

        public bool CanFinalize
        {
            get { return _canFinalize; }
            private set
            {
                if (!SetProperty(ref _canFinalize, value, "CanFinalize")) return;
                RefreshGenerateState();
            }
        }

        public bool CanEditDraft { get { return !_isBusy && !_isCommitted && !_isShutdown; } }

        public bool CanClearBill { get { return CanEditDraft && (HasCartItems || HasTenders); } }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (!SetProperty(ref _isBusy, value, "IsBusy")) return;
                SyncRowEditability();
                RaisePropertyChanged("CanEditDraft");
                RaisePropertyChanged("CanClearBill");
                RefreshGenerateState();
                RaiseCommandStates();
            }
        }

        public bool IsCommitted
        {
            get { return _isCommitted; }
            private set
            {
                if (!SetProperty(ref _isCommitted, value, "IsCommitted")) return;
                SyncRowEditability();
                RaisePropertyChanged("CanEditDraft");
                RaisePropertyChanged("CanClearBill");
                RefreshGenerateState();
                RaiseCommandStates();
            }
        }

        private void SyncRowEditability()
        {
            bool editable = CanEditDraft;
            for (var i = 0; i < CartLines.Count; i++) CartLines[i].CanEdit = editable;
            for (var i = 0; i < Tenders.Count; i++) Tenders[i].CanEdit = editable;
        }

        public bool HasInputErrors
        {
            get { return _hasInputErrors; }
            private set
            {
                if (!SetProperty(ref _hasInputErrors, value, "HasInputErrors")) return;
                RefreshGenerateState();
            }
        }

        public bool GenerateEnabled { get { return ComputeGenerateEnabled(); } }

        public string SubtotalText { get { return _subtotalText; } }
        public string CgstText { get { return _cgstText; } }
        public string SgstText { get { return _sgstText; } }
        public string IgstText { get { return _igstText; } }
        public string RoundOffText { get { return _roundOffText; } }
        public string GrandTotalText { get { return _grandTotalText; } }
        public string TenderedText { get { return _tenderedText; } }
        public string RemainingText { get { return _remainingText; } }
        public bool ShowCgstSgst { get { return _showCgstSgst; } }
        public bool ShowIgst { get { return _showIgst; } }
        public bool ShowRoundOff { get { return _showRoundOff; } }
        public bool IsFullyPaid { get { return _isFullyPaid; } }
        public bool IsOverTendered { get { return _isOverTendered; } }

        public bool HasIssues { get { return Issues.Count > 0; } }

        private void Issues_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged("HasIssues");
        }

        /// <summary>
        /// DEFECT FIX 2: every meaningful draft edit invalidates the previous preview
        /// immediately, before any debounce or background work, so Generate Invoice can
        /// never remain armed against totals that no longer describe the bill.
        /// </summary>
        private void MarkDraftChanged()
        {
            if (_isShutdown || _suppressDraftChanges || _isCommitted) return;

            _draftRevision++;
            _previewVersion++;

            IsPreviewCurrent = false;
            CanFinalize = false;
            IsPreviewPending = true;
            RefreshGenerateState();

            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            RunPreviewNow();
        }

        private async void RunPreviewNow()
        {
            if (_isShutdown || _isCommitted) return;

            if (CartLines.Count == 0)
            {
                ResetPreviewToEmpty();
                return;
            }

            if (_previewRunning)
            {
                _previewQueued = true;
                return;
            }

            var version = ++_previewVersion;
            var revision = _draftRevision;

            _previewRunning = true;
            IsPreviewPending = true;

            try
            {
                var draft = BuildDraft();
                var result = await Task.Run(delegate
                {
                    return _billingService.PreviewInvoice(draft);
                });

                if (_isShutdown || _isCommitted) return;
                if (version != _previewVersion || revision != _draftRevision) return;

                ApplyPreview(result);
            }
            catch (Exception ex)
            {
                if (_isShutdown || version != _previewVersion || revision != _draftRevision) return;
                BillingDiagnostics.Log("PreviewInvoice", ex);

                CanFinalize = false;
                IsPreviewCurrent = false;
                IsPreviewPending = false;
                SetStatus(BillingText.Get("BillErrorServiceUnavailable",
                    "Billing is temporarily unavailable. Please try again. If this continues, contact support."), true);
                RefreshGenerateState();
            }
            finally
            {
                _previewRunning = false;
                if (_previewQueued && !_isShutdown && !_isCommitted)
                {
                    _previewQueued = false;
                    _previewTimer.Stop();
                    _previewTimer.Start();
                }
            }
        }

        private void ApplyPreview(InvoicePreviewResult result)
        {
            if (result == null)
            {
                CanFinalize = false;
                IsPreviewCurrent = false;
                IsPreviewPending = false;
                SetStatus(BillingText.Get("BillErrorServiceUnavailable",
                    "Billing is temporarily unavailable. Please try again. If this continues, contact support."), true);
                RefreshGenerateState();
                return;
            }

            CurrentPreview = result;

            _subtotalText = BillingDisplay.FormatPaise(result.SubtotalPaise);
            _cgstText = BillingDisplay.FormatPaise(result.CgstPaise);
            _sgstText = BillingDisplay.FormatPaise(result.SgstPaise);
            _igstText = BillingDisplay.FormatPaise(result.IgstPaise);
            _roundOffText = BillingDisplay.FormatPaise(result.RoundOffPaise);
            _grandTotalText = BillingDisplay.FormatPaise(result.GrandTotalPaise);
            _tenderedText = BillingDisplay.FormatPaise(result.TenderTotalPaise);
            _remainingText = BillingDisplay.FormatPaise(result.AmountRemainingPaise);
            RaiseSummaryProperties();

            SetFlag(ref _showCgstSgst, result.CgstPaise != 0 || result.SgstPaise != 0, "ShowCgstSgst");
            SetFlag(ref _showIgst, result.IgstPaise != 0, "ShowIgst");
            SetFlag(ref _showRoundOff, result.RoundOffPaise != 0, "ShowRoundOff");
            SetFlag(ref _isFullyPaid, result.IsFullyPaid, "IsFullyPaid");
            SetFlag(ref _isOverTendered, result.AmountRemainingPaise < 0, "IsOverTendered");

            Issues.Clear();
            if (result.Issues != null)
            {
                for (var i = 0; i < result.Issues.Count; i++)
                {
                    var issue = result.Issues[i];
                    if (issue == null || string.IsNullOrWhiteSpace(issue.Message)) continue;
                    Issues.Add(issue);
                }
            }

            for (var i = 0; i < CartLines.Count; i++)
            {
                var line = CartLines[i];
                var match = FindPreviewLine(result, line.MedicineId);
                if (match != null) line.ApplyPreviewLine(match);
                else line.ClearPreviewTotals();
            }

            CanFinalize = result.CanFinalize;
            IsPreviewCurrent = true;
            IsPreviewPending = false;
            RefreshInputErrorState();
        }

        private static InvoiceLinePreview FindPreviewLine(InvoicePreviewResult result, string medicineId)
        {
            if (result.Lines == null || string.IsNullOrEmpty(medicineId)) return null;
            for (var i = 0; i < result.Lines.Count; i++)
            {
                var line = result.Lines[i];
                if (line == null) continue;
                if (string.Equals(line.MedicineId, medicineId, StringComparison.Ordinal)) return line;
            }
            return null;
        }

        private void ResetPreviewToEmpty()
        {
            _previewVersion++;
            _previewTimer.Stop();

            CurrentPreview = null;

            _subtotalText = BillingDisplay.FormatPaise(0);
            _cgstText = BillingDisplay.FormatPaise(0);
            _sgstText = BillingDisplay.FormatPaise(0);
            _igstText = BillingDisplay.FormatPaise(0);
            _roundOffText = BillingDisplay.FormatPaise(0);
            _grandTotalText = BillingDisplay.FormatPaise(0);
            _tenderedText = BillingDisplay.FormatPaise(0);
            _remainingText = BillingDisplay.FormatPaise(0);
            RaiseSummaryProperties();

            SetFlag(ref _showCgstSgst, false, "ShowCgstSgst");
            SetFlag(ref _showIgst, false, "ShowIgst");
            SetFlag(ref _showRoundOff, false, "ShowRoundOff");
            SetFlag(ref _isFullyPaid, false, "IsFullyPaid");
            SetFlag(ref _isOverTendered, false, "IsOverTendered");

            Issues.Clear();

            for (var i = 0; i < CartLines.Count; i++) CartLines[i].ClearPreviewTotals();

            CanFinalize = false;
            IsPreviewCurrent = false;
            IsPreviewPending = false;
            RefreshInputErrorState();
        }

        private void RaiseSummaryProperties()
        {
            RaisePropertyChanged("SubtotalText");
            RaisePropertyChanged("CgstText");
            RaisePropertyChanged("SgstText");
            RaisePropertyChanged("IgstText");
            RaisePropertyChanged("RoundOffText");
            RaisePropertyChanged("GrandTotalText");
            RaisePropertyChanged("ShowHighValueB2cReminder");
            RaisePropertyChanged("TenderedText");
            RaisePropertyChanged("RemainingText");
        }

        private void SetFlag(ref bool field, bool value, string propertyName)
        {
            if (field == value) return;
            field = value;
            RaisePropertyChanged(propertyName);
        }

        // ---- Generate invoice --------------------------------------------------------

        private bool ComputeGenerateEnabled()
        {
            return _canFinalize
                && _isPreviewCurrent
                && !_hasInputErrors
                && CartLines.Count > 0
                && !_isBusy
                && !_isCommitted
                && !_isShutdown;
        }

        private void RefreshInputErrorState()
        {
            var value = false;
            for (var i = 0; i < CartLines.Count; i++)
            {
                if (CartLines[i].HasError) { value = true; break; }
            }
            if (!value)
            {
                for (var i = 0; i < Tenders.Count; i++)
                {
                    if (Tenders[i].HasError) { value = true; break; }
                }
            }
            HasInputErrors = value;
            RefreshGenerateState();
        }

        private void RefreshGenerateState()
        {
            RaisePropertyChanged("GenerateEnabled");
            if (FinalizeCommand != null) FinalizeCommand.RaiseCanExecuteChanged();
        }

        private void RaiseCommandStates()
        {
            if (AddMedicineCommand != null) AddMedicineCommand.RaiseCanExecuteChanged();
            if (RemoveLineCommand != null) RemoveLineCommand.RaiseCanExecuteChanged();
            if (AddTenderCommand != null) AddTenderCommand.RaiseCanExecuteChanged();
            if (RemoveTenderCommand != null) RemoveTenderCommand.RaiseCanExecuteChanged();
            if (UseRemainingCommand != null) UseRemainingCommand.RaiseCanExecuteChanged();
            if (NewBillCommand != null) NewBillCommand.RaiseCanExecuteChanged();
        }

        private async void ExecuteFinalize()
        {
            if (_isShutdown || _isBusy || _isCommitted) return;

            // Re-check at execute time: a stale preview or a local input error must block
            // finalisation even if the command was invoked from a keyboard shortcut.
            if (!ComputeGenerateEnabled()) return;

            _previewTimer.Stop();
            _previewVersion++;

            IsBusy = true;
            SetStatus(BillingText.Get("BillGenerating", "Saving invoice…"), false);

            try
            {
                var draft = BuildDraft();
                var previewSnapshot = _currentPreview;
                var result = await Task.Run(delegate
                {
                    return _billingService.FinalizeInvoice(draft);
                });

                if (_isShutdown) return;

                if (result == null)
                {
                    SetStatus(BillingText.Get("BillErrorFinalizeFailed",
                        "The invoice could not be saved. Nothing was deducted. Please check the messages and try again."), true);
                    MarkDraftChanged();
                    return;
                }

                if (result.Succeeded)
                {
                    ApplyCommittedInvoice(result, previewSnapshot);
                }
                else
                {
                    ApplyFinalizeIssues(result);
                    SetStatus(BillingText.Get("BillErrorFinalizeFailed",
                        "The invoice could not be saved. Nothing was deducted. Please check the messages and try again."), true);
                    MarkDraftChanged();
                }
            }
            catch (Exception ex)
            {
                if (_isShutdown) return;
                BillingDiagnostics.Log("FinalizeInvoice", ex);
                SetStatus(BillingText.Get("BillErrorFinalizeFailed",
                    "The invoice could not be saved. Nothing was deducted. Please check the messages and try again."), true);
                MarkDraftChanged();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyCommittedInvoice(InvoiceResult result, InvoicePreviewResult previewSnapshot)
        {
            _committedPreview = previewSnapshot ?? _currentPreview;

            // Publish the committed number, local time and total BEFORE switching the panel on.
            LastInvoiceNumber = string.IsNullOrWhiteSpace(result.InvoiceNumber) ? "—" : result.InvoiceNumber.Trim();
            LastInvoiceIssuedText = FormatIssuedLocal(result);
            LastInvoiceTotalText = BillingDisplay.FormatRupees(result.GrandTotalPaise);

            ApplyFinalizeIssues(result);

            CommittedInvoice = result;
            CanFinalize = false;
            IsPreviewCurrent = true;
            IsPreviewPending = false;
            IsCommitted = true;

            SetStatus(BillingText.Format("BillStatusCommittedFormat", "Invoice {0} is saved.", LastInvoiceNumber), false);
            RefreshGenerateState();
        }

        private void ApplyFinalizeIssues(InvoiceResult result)
        {
            Issues.Clear();

            if (result.Issues != null)
            {
                for (var i = 0; i < result.Issues.Count; i++)
                {
                    var issue = result.Issues[i];
                    if (issue == null || string.IsNullOrWhiteSpace(issue.Message)) continue;
                    Issues.Add(issue);
                }
            }

            // A post-commit snapshot refresh failure is a warning, never a false failure.
            if (result.Warnings != null)
            {
                for (var i = 0; i < result.Warnings.Count; i++)
                {
                    var warning = result.Warnings[i];
                    if (string.IsNullOrWhiteSpace(warning)) continue;
                    Issues.Add(new BillingIssue
                    {
                        Severity = BillingIssueSeverity.Warning,
                        Code = "POST_COMMIT_WARNING",
                        Message = warning
                    });
                }
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

        public InvoiceResult CommittedInvoice
        {
            get { return _committedInvoice; }
            private set { SetProperty(ref _committedInvoice, value, "CommittedInvoice"); }
        }

        public string LastInvoiceNumber
        {
            get { return _lastInvoiceNumber; }
            private set { SetProperty(ref _lastInvoiceNumber, value ?? string.Empty, "LastInvoiceNumber"); }
        }

        public string LastInvoiceIssuedText
        {
            get { return _lastInvoiceIssuedText; }
            private set { SetProperty(ref _lastInvoiceIssuedText, value ?? string.Empty, "LastInvoiceIssuedText"); }
        }

        public string LastInvoiceTotalText
        {
            get { return _lastInvoiceTotalText; }
            private set { SetProperty(ref _lastInvoiceTotalText, value ?? string.Empty, "LastInvoiceTotalText"); }
        }

        // ---- Draft lifecycle ---------------------------------------------------------

        /// <summary>
        /// Clears the working draft. After a committed invoice this is the "Start new bill"
        /// action: the invoice is already saved and its number has already been displayed.
        /// </summary>
        public void ClearDraft()
        {
            if (_isShutdown || _isBusy) return;

            _suppressDraftChanges = true;
            try
            {
                _searchTimer.Stop();
                _previewTimer.Stop();
                _draftRevision++;

                ClearSearchAfterAdd();

                while (CartLines.Count > 0) CartLines.RemoveAt(CartLines.Count - 1);
                while (Tenders.Count > 0) Tenders.RemoveAt(Tenders.Count - 1);
                SelectedCartLine = null;

                _customerName = string.Empty;
                _customerPhone = string.Empty;
                _customerAddress = string.Empty;
                _customerGstin = string.Empty;
                RaisePropertyChanged("CustomerName");
                RaisePropertyChanged("CustomerPhone");
                RaisePropertyChanged("CustomerAddress");
                RaisePropertyChanged("CustomerGstin");
                IsCustomerExpanded = false;

                CommittedInvoice = null;
                _committedPreview = null;
                LastInvoiceNumber = string.Empty;
                LastInvoiceIssuedText = string.Empty;
                LastInvoiceTotalText = string.Empty;
                IsCommitted = false;

                ResetPreviewToEmpty();
                SetStatus(string.Empty, false);
            }
            finally
            {
                _suppressDraftChanges = false;
            }

            RaisePropertyChanged("HasCartItems");
            RaisePropertyChanged("CartCountText");
            RaisePropertyChanged("HasTenders");
            RefreshInputErrorState();
            RaiseCommandStates();
        }

        /// <summary>Builds the contract input. Only IDs, quantities, customer text and tenders are sent.</summary>
        private InvoiceDraft BuildDraft()
        {
            var draft = new InvoiceDraft();
            draft.Items = new List<InvoiceItemDraft>();
            draft.Tenders = new List<PaymentTenderDraft>();
            draft.Customer = new InvoiceCustomerDraft
            {
                Name = NullIfBlank(_customerName),
                Phone = NullIfBlank(_customerPhone),
                Address = NullIfBlank(_customerAddress),
                Gstin = NullIfBlank(_customerGstin)
            };

            for (var i = 0; i < CartLines.Count; i++)
            {
                var line = CartLines[i];
                // QuantityMilli is only ever set from a successful parse, so it is always a
                // valid positive quantity. A row with a typing error keeps its last valid
                // quantity in the draft while HasInputErrors blocks finalisation.
                if (line.QuantityMilli <= 0) continue;
                draft.Items.Add(new InvoiceItemDraft
                {
                    MedicineId = line.MedicineId,
                    QuantityMilli = line.QuantityMilli
                });
            }

            for (var i = 0; i < Tenders.Count; i++)
            {
                var tender = Tenders[i];
                if (tender.HasError || tender.AmountPaise <= 0) continue;
                draft.Tenders.Add(new PaymentTenderDraft
                {
                    Method = tender.Method,
                    AmountPaise = tender.AmountPaise,
                    ReferenceText = NullIfBlank(tender.ReferenceText)
                });
            }

            return draft;
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        // ---- Paper format and zoom (layout only) -------------------------------------

        public PaperFormatOption SelectedPaperFormatOption
        {
            get { return _selectedPaperFormatOption; }
            set
            {
                if (value == null || ReferenceEquals(value, _selectedPaperFormatOption)) return;
                _selectedPaperFormatOption = value;
                RaisePropertyChanged("SelectedPaperFormatOption");
                RaisePropertyChanged("SelectedPaperFormat");
                // Paper format never touches the draft, the preview request or any total.
            }
        }

        public InvoicePaperFormat SelectedPaperFormat
        {
            get { return _selectedPaperFormatOption.Format; }
            set
            {
                for (var i = 0; i < PaperFormats.Count; i++)
                {
                    if (PaperFormats[i].Format == value)
                    {
                        SelectedPaperFormatOption = PaperFormats[i];
                        return;
                    }
                }
            }
        }

        /// <summary>Visual zoom percentage for the on-screen paper preview. Never used in arithmetic on money or stock.</summary>
        public double PreviewZoomPercent
        {
            get { return _previewZoomPercent; }
            set
            {
                var clamped = value;
                if (clamped < MinZoomPercent) clamped = MinZoomPercent;
                if (clamped > MaxZoomPercent) clamped = MaxZoomPercent;
                SetProperty(ref _previewZoomPercent, clamped, "PreviewZoomPercent");
            }
        }

        // ---- Printing boundary -------------------------------------------------------

        /// <summary>
        /// Raises PrintRequested for a committed invoice. Returns false when no integrator
        /// handler is attached (or nothing is committed), so the view can show a safe message.
        /// </summary>
        public bool RaisePrintRequest()
        {
            var handler = PrintRequested;
            if (handler == null) return false;

            var invoice = _committedInvoice;
            if (invoice == null || !invoice.Succeeded) return false;

            handler(this, new InvoicePrintRequestedEventArgs(
                invoice,
                _committedPreview ?? _currentPreview,
                SelectedPaperFormat));
            return true;
        }

        // ---- Status ------------------------------------------------------------------

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set
            {
                if (!SetProperty(ref _statusMessage, value ?? string.Empty, "StatusMessage")) return;
                RaisePropertyChanged("HasStatus");
            }
        }

        public bool StatusIsError
        {
            get { return _statusIsError; }
            private set { SetProperty(ref _statusIsError, value, "StatusIsError"); }
        }

        public bool HasStatus { get { return !string.IsNullOrWhiteSpace(_statusMessage); } }

        private void SetStatus(string message, bool isError)
        {
            StatusIsError = isError;
            StatusMessage = message;
        }

        // ---- Lifetime ----------------------------------------------------------------

        /// <summary>
        /// Called by the view when it unloads. Stops timers, invalidates every in-flight
        /// request and detaches row subscriptions. The view model is single-use per navigation.
        /// </summary>
        public void Shutdown()
        {
            if (_isShutdown) return;
            _isShutdown = true;

            _searchTimer.Stop();
            _previewTimer.Stop();
            _searchVersion++;
            _previewVersion++;
            _draftRevision++;

            for (var i = 0; i < _watchedCartLines.Count; i++)
            {
                _watchedCartLines[i].PropertyChanged -= CartLine_PropertyChanged;
            }
            _watchedCartLines.Clear();

            for (var i = 0; i < _watchedTenders.Count; i++)
            {
                _watchedTenders[i].PropertyChanged -= Tender_PropertyChanged;
            }
            _watchedTenders.Clear();

            SyncRowEditability();
            RaisePropertyChanged("CanEditDraft");
            RaisePropertyChanged("CanClearBill");
            RefreshGenerateState();
            RaiseCommandStates();
        }

        public bool ShowHighValueB2cReminder
        {
            get
            {
                return _currentPreview != null
                    && _currentPreview.GrandTotalPaise >= 5000000L
                    && string.IsNullOrWhiteSpace(CustomerGstin)
                    && (string.IsNullOrWhiteSpace(CustomerName) || string.IsNullOrWhiteSpace(CustomerAddress));
            }
        }

        public void ApplyExactPayment(InvoicePaymentMethod method)
        {
            if (IsBusy || IsCommitted || _isShutdown) return;
            if (_currentPreview == null || !_isPreviewCurrent || _currentPreview.GrandTotalPaise <= 0L) return;

            PaymentMethodOption option = PaymentMethods[0];
            for (int i = 0; i < PaymentMethods.Count; i++)
            {
                if (PaymentMethods[i].Method == method) { option = PaymentMethods[i]; break; }
            }

            while (Tenders.Count > 0)
            {
                Tenders.RemoveAt(0);
            }

            var tender = new PaymentTenderViewModel(PaymentMethods, option, _currentPreview.GrandTotalPaise)
            {
                CanEdit = CanEditDraft
            };
            Tenders.Add(tender);
            MarkDraftChanged();
        }

        public void StepCartLineQuantity(CartLineViewModel line, decimal delta)
        {
            if (IsBusy || IsCommitted || _isShutdown || line == null) return;
            long deltaMilli = checked((long)Math.Round(delta * 1000m, 0, MidpointRounding.AwayFromZero));
            long nextMilli = line.QuantityMilli + deltaMilli;
            if (nextMilli < 1000L) nextMilli = 1000L;
            line.SetQuantitySilently(nextMilli);
            MarkDraftChanged();
        }
    }
}