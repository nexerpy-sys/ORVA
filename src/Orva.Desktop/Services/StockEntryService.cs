using System;
using Orva.Domain;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class StockEntryService
    {
        private readonly InventoryRepository _inventory;
        private readonly StockSnapshotWriter _snapshotWriter;

        public StockEntryService(InventoryRepository inventory, StockSnapshotWriter snapshotWriter)
        {
            _inventory = inventory;
            _snapshotWriter = snapshotWriter;
        }

        public StockEntryResult ReceiveStock(StockEntryInput input)
        {
            // InventoryRepository.SaveStockEntry atomically increments stock_snapshot_generation
            // and sets stock_snapshot_dirty = '1' inside the same SQLite transaction.
            _inventory.SaveStockEntry(input);

            var result = new StockEntryResult
            {
                MedicineName = input.MedicineName.Trim(),
                BatchNumber = input.BatchNumber.Trim(),
                SnapshotUpdated = false
            };

            try
            {
                bool snapshotClean = _snapshotWriter.WriteCurrentSnapshot();
                result.SnapshotUpdated = snapshotClean;
            }
            catch (Exception ex)
            {
                // The database transaction is already committed and already marked dirty with an
                // incremented generation. Never report a committed stock entry as failed.
                System.Diagnostics.Trace.TraceError("Stock snapshot update failed after a committed stock receipt: " + ex);
                result.SnapshotWarning = "Stock was saved, but the portable stock file could not be refreshed. Use Refresh stock file in Inventory & Stock.";
            }
            return result;
        }
    }
}