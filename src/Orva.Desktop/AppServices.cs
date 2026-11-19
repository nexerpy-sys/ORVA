using Orva.Domain.Billing;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;
using Orva.Services;

namespace Orva
{
    public sealed class AppServices
    {
        public SqliteDatabase Database { get; private set; }
        public ShopRepository ShopRepository { get; private set; }
        public InventoryRepository InventoryRepository { get; private set; }
        public TaxSettingsRepository TaxSettingsRepository { get; private set; }
        public StockEntryService StockEntryService { get; private set; }
        public StockAdjustmentService StockAdjustmentService { get; private set; }
        public StockSnapshotWriter SnapshotWriter { get; private set; }
        public StockDataImporter StockDataImporter { get; private set; }
        public MedicineCatalogueImporter MedicineCatalogueImporter { get; private set; }
        public ShopBackupService ShopBackupService { get; private set; }
        public AutomaticBackupCoordinator AutomaticBackupCoordinator { get; private set; }
        public SalesReturnService SalesReturnService { get; private set; }
        public InvoiceCancellationService InvoiceCancellationService { get; private set; }
        public ReportingService ReportingService { get; private set; }
        public InvoicePrintService InvoicePrintService { get; private set; }
        public SubscriptionService SubscriptionService { get; private set; }
        public AppSettingsRepository AppSettings { get; private set; }
        public IBillingService BillingService { get; private set; }

        public AppServices(
            SqliteDatabase database,
            ShopRepository shopRepository,
            InventoryRepository inventoryRepository,
            TaxSettingsRepository taxSettingsRepository,
            StockEntryService stockEntryService,
            StockAdjustmentService stockAdjustmentService,
            StockSnapshotWriter snapshotWriter,
            StockDataImporter stockDataImporter,
            MedicineCatalogueImporter medicineCatalogueImporter,
            ShopBackupService shopBackupService,
            AutomaticBackupCoordinator automaticBackupCoordinator,
            SalesReturnService salesReturnService,
            InvoiceCancellationService invoiceCancellationService,
            ReportingService reportingService,
            InvoicePrintService invoicePrintService,
            SubscriptionService subscriptionService,
            AppSettingsRepository appSettings,
            IBillingService billingService)
        {
            Database = database;
            ShopRepository = shopRepository;
            InventoryRepository = inventoryRepository;
            TaxSettingsRepository = taxSettingsRepository;
            StockEntryService = stockEntryService;
            StockAdjustmentService = stockAdjustmentService;
            SnapshotWriter = snapshotWriter;
            StockDataImporter = stockDataImporter;
            MedicineCatalogueImporter = medicineCatalogueImporter;
            ShopBackupService = shopBackupService;
            AutomaticBackupCoordinator = automaticBackupCoordinator;
            SalesReturnService = salesReturnService;
            InvoiceCancellationService = invoiceCancellationService;
            ReportingService = reportingService;
            InvoicePrintService = invoicePrintService;
            SubscriptionService = subscriptionService;
            AppSettings = appSettings;
            BillingService = billingService;
        }
    }
}