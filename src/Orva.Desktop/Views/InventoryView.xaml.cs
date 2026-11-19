using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using Orva.Domain;
using Orva.Infrastructure;

namespace Orva.Views
{
    public partial class InventoryView : UserControl
    {
        private string _activeFilter = "ALL";
        private readonly DispatcherTimer _searchDebounce;

        public InventoryView()
        {
            InitializeComponent();
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += (s, e) => { _searchDebounce.Stop(); RefreshInventory(); };
            Unloaded += (s, e) => _searchDebounce.Stop();
            RefreshInventory();
        }

        private void RefreshInventory()
        {
            string query = SearchBox == null ? string.Empty : SearchBox.Text;
            InventoryGrid.ItemsSource = App.Services.InventoryRepository.GetInventoryRows(query, _activeFilter, DateTime.Now);
            MedicineCountText.Text = App.Services.InventoryRepository.GetMedicineCount().ToString("N0");
            BatchCountText.Text = App.Services.InventoryRepository.GetBatchCount().ToString("N0");
            LowStockCountText.Text = App.Services.InventoryRepository.GetLowStockAlerts(DateTime.Now).Count.ToString("N0");
            ExpiryAlertCountText.Text = App.Services.InventoryRepository.GetExpiryAlerts(DateTime.Now).Count.ToString("N0");

            UpdateRowActionButtons();
            UpdateFilterStyles();

            try
            {
                var path = App.Services.SnapshotWriter.GetSnapshotPath();
                var dirty = App.Services.SnapshotWriter.IsDirty();
                if (dirty)
                {
                    SnapshotStatusText.Text = "Needs refresh";
                    SnapshotStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
                }
                else
                {
                    SnapshotStatusText.Text = File.Exists(path) ? "Updated (v2)" : "Not created yet";
                    SnapshotStatusText.Foreground = File.Exists(path) ? (System.Windows.Media.Brush)FindResource("Success") : (System.Windows.Media.Brush)FindResource("Muted");
                }
            }
            catch
            {
                SnapshotStatusText.Text = "Not available";
            }
        }

        private void UpdateFilterStyles()
        {
            var primary = (Style)FindResource("PrimaryButton");
            var secondary = (Style)FindResource("SecondaryButton");
            FilterAllButton.Style = _activeFilter == "ALL" ? primary : secondary;
            FilterLowStockButton.Style = _activeFilter == "LOW_STOCK" ? primary : secondary;
            FilterExpiringButton.Style = _activeFilter == "EXPIRING" ? primary : secondary;
            FilterExpiredButton.Style = _activeFilter == "EXPIRED" ? primary : secondary;
        }

        private void UpdateRowActionButtons()
        {
            var selected = InventoryGrid.SelectedItem as InventoryRow;
            bool hasSelection = selected != null;
            EditMedicineButton.IsEnabled = hasSelection;
            AdjustStockButton.IsEnabled = hasSelection;
        }

        private void InventoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRowActionButtons();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void FilterAll_Click(object sender, RoutedEventArgs e) { _activeFilter = "ALL"; RefreshInventory(); }
        private void FilterLowStock_Click(object sender, RoutedEventArgs e) { _activeFilter = "LOW_STOCK"; RefreshInventory(); }
        private void FilterExpiring_Click(object sender, RoutedEventArgs e) { _activeFilter = "EXPIRING"; RefreshInventory(); }
        private void FilterExpired_Click(object sender, RoutedEventArgs e) { _activeFilter = "EXPIRED"; RefreshInventory(); }

        private void AddStock_Click(object sender, RoutedEventArgs e)
        {
            var selected = InventoryGrid.SelectedItem as InventoryRow;
            var dialog = new AddStockWindow(selected != null ? selected.MedicineName : null);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) RefreshInventory();
        }

        private void AdjustStock_Click(object sender, RoutedEventArgs e)
        {
            var selected = InventoryGrid.SelectedItem as InventoryRow;
            if (selected == null) return;
            var dialog = new StockAdjustmentWindow(selected);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) RefreshInventory();
        }

        private void EditMedicine_Click(object sender, RoutedEventArgs e)
        {
            var selected = InventoryGrid.SelectedItem as InventoryRow;
            if (selected == null) return;
            var dialog = new EditMedicineWindow(selected.MedicineId);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) RefreshInventory();
        }

        private void ImportCatalogue_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ImportCatalogueWindow();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) RefreshInventory();
        }

        private void ImportStock_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ImportStockWindow();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) RefreshInventory();
        }

        private void RefreshSnapshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Services.SnapshotWriter.WriteCurrentSnapshot();
                RefreshInventory();
                MessageBox.Show("The portable stock file (StockData.json v2) has been updated.", "ORVA", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The stock file could not be updated. Your saved inventory has not been changed. " + ex.Message,
                    "ORVA — Stock file", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var shop = App.Services.ShopRepository.GetProfile();
                var folder = AppPaths.GetShopFolder(shop != null ? shop.ShopName : "ORVA Shop");
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("The shop folder could not be opened. " + ex.Message, "ORVA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}