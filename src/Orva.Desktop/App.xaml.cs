using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Orva.Infrastructure;
using Orva.Infrastructure.Database;
using Orva.Infrastructure.Repositories;
using Orva.Services;
using Orva.Services.Billing;
using Orva.Views;

namespace Orva
{
    public partial class App : Application
    {
        public static AppServices Services { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                            DispatcherUnhandledException += (s, args) =>
            {
                LogFatal("UI", args.Exception);
                MessageBox.Show("An unexpected error occurred: " + args.Exception.Message + "\nDetails were written to the ORVA log folder.", "ORVA", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogFatal("AppDomain", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, args) => { LogFatal("Task", args.Exception); args.SetObserved(); };

            AppPaths.EnsureDirectories();

                var database = new SqliteDatabase(AppPaths.DatabasePath);
                new DatabaseMigrator(database).Migrate();

                var shopRepository = new ShopRepository(database);
                var inventoryRepository = new InventoryRepository(database);
                var taxSettingsRepository = new TaxSettingsRepository(database);
                var snapshotWriter = new StockSnapshotWriter(database, shopRepository);
                var stockEntryService = new StockEntryService(inventoryRepository, snapshotWriter);
                var stockAdjustmentService = new StockAdjustmentService(inventoryRepository, snapshotWriter);
                var appSettings = new AppSettingsRepository(database);
                var shopBackupService = new ShopBackupService(database, shopRepository, snapshotWriter);
                var automaticBackupCoordinator = new AutomaticBackupCoordinator(shopBackupService, appSettings);
                var stockDataImporter = new StockDataImporter(
                    database,
                    shopRepository,
                    snapshotWriter,
                    reason => shopBackupService.CreateSafetyBackup(reason));
                var medicineCatalogueImporter = new MedicineCatalogueImporter(database, snapshotWriter);
                var salesReturnService = new SalesReturnService(database, snapshotWriter);
                var invoiceCancellationService = new InvoiceCancellationService(database, snapshotWriter);
                var reportingService = new ReportingService(database);
                var invoicePrintService = new InvoicePrintService(appSettings);
                var subscriptionService = new SubscriptionService(appSettings);
                var billingService = new BillingService(database, snapshotWriter);

                Services = new AppServices(
                    database,
                    shopRepository,
                    inventoryRepository,
                    taxSettingsRepository,
                    stockEntryService,
                    stockAdjustmentService,
                    snapshotWriter,
                    stockDataImporter,
                    medicineCatalogueImporter,
                    shopBackupService,
                    automaticBackupCoordinator,
                    salesReturnService,
                    invoiceCancellationService,
                    reportingService,
                    invoicePrintService,
                    subscriptionService,
                    appSettings,
                    billingService);

                LocalizationManager.SetLanguage(appSettings.Get("display_language", "en"));

                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                bool firstRunCompleted = false;
                if (shopRepository.GetProfile() == null)
                {
                    var setup = new ShopSetupWindow(shopRepository);
                    if (setup.ShowDialog() != true)
                    {
                        Shutdown();
                        return;
                    }
                    firstRunCompleted = true;
                }

                try
                {
                    Services.SnapshotWriter.WriteCurrentSnapshot();
                }
                catch
                {
                    // A snapshot failure must not prevent normal offline billing.
                }

                // Trigger initial/daily backup and optional subscription check asynchronously without blocking startup UI.
                Task.Run(async delegate
                {
                    try
                    {
                        if (firstRunCompleted)
                            Services.AutomaticBackupCoordinator.RunBackupNow();
                        else
                            Services.AutomaticBackupCoordinator.RunDailyBackupIfDue();
                    }
                    catch (Exception bgEx) { LogFatal("Background", bgEx); }

                    try
                    {
                        var sub = Services.SubscriptionService.EvaluateLocalStatus();
                        if (sub.IsConfigured && (!sub.LastVerifiedUtc.HasValue || (DateTime.UtcNow - sub.LastVerifiedUtc.Value).TotalHours >= SubscriptionService.RefreshIntervalHours))
                        {
                            await Services.SubscriptionService.RefreshFromRegistryAsync();
                        }
                    }
                    catch (Exception bgEx) { LogFatal("Background", bgEx); }
                });

                var main = new MainWindow();
                MainWindow = main;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                main.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ORVA could not start because its local data could not be opened.\n\n" + ex.Message +
                    "\n\nYour existing business data has not been intentionally deleted. Please contact support if this continues.",
                    "ORVA — Startup problem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        internal static void LogFatal(string category, Exception ex)
        {
            if (ex == null) return;
            try
            {
                AppPaths.EnsureDirectories();
                string logFile = Path.Combine(AppPaths.LogsDirectory, "orva_errors_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                string entry = "[" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "] [" + category + "] " + ex + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(logFile, entry);
            }
            catch
            {
                // Never throw from crash logger
            }
        }
    }
}