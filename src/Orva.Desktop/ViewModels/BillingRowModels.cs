using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Orva.Domain.Billing;

namespace Orva.ViewModels
{
    public sealed class MedicineSearchHitViewModel
    {
        public MedicineSearchHitViewModel(MedicineLookupResult source)
        {
            Source = source ?? throw new ArgumentNullException("source");
            MedicineId = source.MedicineId;
            DisplayName = source.DisplayName ?? string.Empty;

            var detail = new List<string>();
            if (!string.IsNullOrWhiteSpace(source.Strength)) detail.Add(source.Strength.Trim());
            if (!string.IsNullOrWhiteSpace(source.DosageForm)) detail.Add(source.DosageForm.Trim());
            if (!string.IsNullOrWhiteSpace(source.GenericName)) detail.Add(source.GenericName.Trim());
            if (!string.IsNullOrWhiteSpace(source.BrandName)) detail.Add(source.BrandName.Trim());
            DetailText = string.Join(" · ", detail.Distinct(StringComparer.OrdinalIgnoreCase));

            AvailableText = BillingDisplay.FormatQuantity(source.AvailableQuantityMilli) +
                " " + (source.StockUnitName ?? string.Empty) + " available";

            CanSell = source.CanSell;
            StatusText = string.IsNullOrWhiteSpace(source.AvailabilityMessage)
                ? (CanSell ? string.Empty : "No eligible stock available")
                : source.AvailabilityMessage;
            ShowStatus = !CanSell || !string.IsNullOrWhiteSpace(StatusText);
            HasDetail = !string.IsNullOrWhiteSpace(DetailText);
        }

        public MedicineLookupResult Source { get; private set; }
        public string MedicineId { get; private set; }
        public string DisplayName { get; private set; }
        public string DetailText { get; private set; }
        public bool HasDetail { get; private set; }
        public string AvailableText { get; private set; }
        public bool CanSell { get; private set; }
        public string StatusText { get; private set; }
        public bool ShowStatus { get; private set; }
    }

    public sealed class CartLineViewModel : INotifyPropertyChanged
    {
        private long _quantityMilli;
        private string _quantityText;
        private string _quantityError;
        private string _lineTotalText = "—";
        private string _allocationNote = string.Empty;
        private bool _hasAllocationNote;
        private bool _hasError;
        private bool _canEdit = true;

        public CartLineViewModel(MedicineLookupResult source, long quantityMilli)
        {
            if (source == null) throw new ArgumentNullException("source");
            Source = source;
            MedicineId = source.MedicineId;
            MedicineName = source.DisplayName ?? string.Empty;
            StockUnitName = source.StockUnitName ?? string.Empty;
            SetQuantitySilently(quantityMilli);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public MedicineLookupResult Source { get; private set; }
        public string MedicineId { get; private set; }
        public string MedicineName { get; private set; }
        public string StockUnitName { get; private set; }

        public long QuantityMilli
        {
            get { return _quantityMilli; }
            private set
            {
                if (_quantityMilli == value) return;
                _quantityMilli = value;
                Raise("QuantityMilli");
            }
        }

        public string QuantityText
        {
            get { return _quantityText; }
            set
            {
                if (!_canEdit) return;
                var text = value ?? string.Empty;
                if (string.Equals(_quantityText, text, StringComparison.Ordinal)) return;
                _quantityText = text;
                Raise("QuantityText");

                long parsed;
                QuantityParseStatus status;
                if (BillingDisplay.TryParseQuantity(text, out parsed, out status))
                {
                    Quantity = parsed;
                    QuantityError = string.Empty;
                    HasError = false;
                }
                else
                {
                    QuantityError = BillingDisplay.DescribeQuantityError(status);
                    HasError = true;
                }
            }
        }

        public string QuantityError
        {
            get { return _quantityError; }
            private set
            {
                if (string.Equals(_quantityError, value, StringComparison.Ordinal)) return;
                _quantityError = value;
                Raise("QuantityError");
            }
        }

        public bool HasError
        {
            get { return _hasError; }
            private set
            {
                if (_hasError == value) return;
                _hasError = value;
                Raise("HasError");
            }
        }

        public bool CanEdit
        {
            get { return _canEdit; }
            set
            {
                if (_canEdit == value) return;
                _canEdit = value;
                Raise("CanEdit");
            }
        }

        public string LineTotalText
        {
            get { return _lineTotalText; }
            private set
            {
                if (string.Equals(_lineTotalText, value, StringComparison.Ordinal)) return;
                _lineTotalText = value;
                Raise("LineTotalText");
            }
        }

        public string AllocationNote
        {
            get { return _allocationNote; }
            private set
            {
                if (string.Equals(_allocationNote, value, StringComparison.Ordinal)) return;
                _allocationNote = value;
                Raise("AllocationNote");
            }
        }

        public bool HasAllocationNote
        {
            get { return _hasAllocationNote; }
            private set
            {
                if (_hasAllocationNote == value) return;
                _hasAllocationNote = value;
                Raise("HasAllocationNote");
            }
        }

        public void SetQuantitySilently(long quantityMilli)
        {
            _quantityMilli = quantityMilli;
            _quantityText = BillingDisplay.FormatQuantity(quantityMilli);
            _quantityError = string.Empty;
            _hasError = false;
            Raise("QuantityMilli");
            Raise("QuantityText");
            Raise("QuantityError");
            Raise("HasError");
        }

        public void CommitQuantityText()
        {
            long parsed;
            QuantityParseStatus status;
            if (BillingDisplay.TryParseQuantity(_quantityText, out parsed, out status))
            {
                Quantity = parsed;
                QuantityError = string.Empty;
                HasError = false;
                _quantityText = BillingDisplay.FormatQuantity(parsed);
                Raise("QuantityText");
            }
            else
            {
                QuantityError = BillingDisplay.DescribeQuantityError(status);
                HasError = true;
            }
        }

        public void ApplyPreviewLine(InvoiceLinePreview line)
        {
            if (line == null)
            {
                ClearPreviewTotals();
                return;
            }

            LineTotalText = BillingDisplay.FormatRupees(line.LineTotalPaise);
            var count = line.BatchAllocations == null ? 0 : line.BatchAllocations.Count;
            AllocationNote = count > 1 ? "Split across " + count.ToString() + " batches" : string.Empty;
            HasAllocationNote = count > 1;
        }

        public void ClearPreviewTotals()
        {
            LineTotalText = "—";
            AllocationNote = string.Empty;
            HasAllocationNote = false;
        }

        private long Quantity
        {
            set
            {
                if (_quantityMilli == value) return;
                _quantityMilli = value;
                Raise("QuantityMilli");
            }
        }

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class PaymentTenderViewModel : INotifyPropertyChanged
    {
        private PaymentMethodOption _selectedMethod;
        private long _amountPaise;
        private string _amountText;
        private string _referenceText = string.Empty;
        private string _amountError = string.Empty;
        private bool _hasError;
        private bool _canEdit = true;

        public PaymentTenderViewModel(
            IList<PaymentMethodOption> methods,
            PaymentMethodOption selectedMethod,
            long initialAmountPaise)
        {
            MethodOptions = methods ?? new List<PaymentMethodOption>();
            _selectedMethod = selectedMethod;
            SetAmountSilently(initialAmountPaise);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IList<PaymentMethodOption> MethodOptions { get; private set; }

        public PaymentMethodOption SelectedMethod
        {
            get { return _selectedMethod; }
            set
            {
                if (!_canEdit || ReferenceEquals(_selectedMethod, value)) return;
                _selectedMethod = value;
                Raise("SelectedMethod");
                Raise("IsNonCash");
                Raise("Method");
                Raise("MethodName");
            }
        }

        public InvoicePaymentMethod Method
        {
            get { return _selectedMethod == null ? InvoicePaymentMethod.Cash : _selectedMethod.Method; }
        }

        public string MethodName
        {
            get { return _selectedMethod == null ? string.Empty : _selectedMethod.DisplayName; }
        }

        public bool HasAmount
        {
            get { return !_hasError && _amountPaise > 0; }
        }

        public bool IsNonCash
        {
            get { return Method != InvoicePaymentMethod.Cash; }
        }

        public long AmountPaise { get { return _amountPaise; } }

        public string AmountText
        {
            get { return _amountText; }
            set
            {
                if (!_canEdit) return;
                var text = value ?? string.Empty;
                if (string.Equals(_amountText, text, StringComparison.Ordinal)) return;
                _amountText = text;
                Raise("AmountText");

                long parsed;
                MoneyParseStatus status;
                if (BillingDisplay.TryParseMoney(text, out parsed, out status))
                {
                    _amountPaise = parsed;
                    AmountError = string.Empty;
                    HasError = false;
                    Raise("AmountPaise");
                    Raise("HasAmount");
                }
                else
                {
                    AmountError = BillingDisplay.DescribeMoneyError(status);
                    HasError = true;
                }
            }
        }

        public string ReferenceText
        {
            get { return _referenceText; }
            set
            {
                if (!_canEdit) return;
                var text = value ?? string.Empty;
                if (string.Equals(_referenceText, text, StringComparison.Ordinal)) return;
                _referenceText = text;
                Raise("ReferenceText");
            }
        }

        public string AmountError
        {
            get { return _amountError; }
            private set
            {
                if (string.Equals(_amountError, value, StringComparison.Ordinal)) return;
                _amountError = value;
                Raise("AmountError");
            }
        }

        public bool HasError
        {
            get { return _hasError; }
            private set
            {
                if (_hasError == value) return;
                _hasError = value;
                Raise("HasError");
                Raise("HasAmount");
            }
        }

        public bool CanEdit
        {
            get { return _canEdit; }
            set
            {
                if (_canEdit == value) return;
                _canEdit = value;
                Raise("CanEdit");
            }
        }

        public void CommitAmountText()
        {
            long parsed;
            MoneyParseStatus status;
            if (BillingDisplay.TryParseMoney(_amountText, out parsed, out status))
            {
                SetAmountSilently(parsed);
                AmountError = string.Empty;
                HasError = false;
            }
            else
            {
                AmountError = BillingDisplay.DescribeMoneyError(status);
                HasError = true;
            }
        }

        public void SetAmountSilently(long amountPaise)
        {
            _amountPaise = amountPaise;
            _amountText = BillingDisplay.FormatPaise(amountPaise);
            _amountError = string.Empty;
            _hasError = false;
            Raise("AmountPaise");
            Raise("AmountText");
            Raise("AmountError");
            Raise("HasError");
            Raise("HasAmount");
        }

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}