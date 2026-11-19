using System;
using System.Windows;
using System.Windows.Controls;
using Orva.Domain;
using Orva.Infrastructure.Repositories;
using Orva.Services.Billing;

namespace Orva.Views
{
    public partial class ShopSetupWindow : Window
    {
        private readonly ShopRepository _repository;

        public ShopSetupWindow(ShopRepository repository)
        {
            InitializeComponent();
            Height = Math.Min(700, Math.Max(460, SystemParameters.WorkArea.Height - 32));
            _repository = repository;
            StateBox.IsEditable = false;
            StateBox.DisplayMemberPath = "DisplayLabel";
            StateBox.ItemsSource = IndianGstStateCatalog.AllStates;
            StateBox.SelectionChanged += StateBox_SelectionChanged;
            StateCodeBox.IsReadOnly = true;
        }

        private void StateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = StateBox.SelectedItem as IndianGstState;
            StateCodeBox.Text = selected == null ? string.Empty : selected.Code;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(ShopNameBox.Text))
            {
                ErrorText.Text = "Enter the shop name to continue.";
                ShopNameBox.Focus();
                return;
            }

            var selectedState = StateBox.SelectedItem as IndianGstState;
            if (selectedState == null)
            {
                ErrorText.Text = "Select your State so the 2-digit GST State Code is recorded accurately.";
                StateBox.Focus();
                return;
            }

            string gstin = (GstinBox.Text ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(gstin))
            {
                var validation = GstinValidator.Validate(gstin);
                if (!validation.IsValid)
                {
                    ErrorText.Text = validation.ErrorMessage;
                    GstinBox.Focus();
                    return;
                }
                if (!string.Equals(validation.StateCode, selectedState.Code, StringComparison.Ordinal))
                {
                    ErrorText.Text = "The first two digits of the GSTIN (" + validation.StateCode + ") do not match the selected state (" + selectedState.Code + ").";
                    GstinBox.Focus();
                    return;
                }
            }

            try
            {
                _repository.SaveProfile(new ShopProfile
                {
                    ShopId = Guid.NewGuid().ToString("N"),
                    ShopName = ShopNameBox.Text.Trim(),
                    LegalName = LegalNameBox.Text.Trim(),
                    Address = AddressBox.Text.Trim(),
                    Phone = PhoneBox.Text.Trim(),
                    Email = EmailBox.Text.Trim(),
                    StateName = selectedState.Name,
                    StateCode = selectedState.Code,
                    Gstin = gstin,
                    DrugLicenceNumber = DrugLicenceBox.Text.Trim(),
                    InvoicePrefix = "INV"
                });
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Shop details could not be saved. " + ex.Message;
            }
        }
    }
}