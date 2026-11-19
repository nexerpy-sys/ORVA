using System;
using Orva.Domain;
using Orva.Infrastructure.Repositories;

namespace Orva.Services
{
    public sealed class StockAdjustmentService
    {
        private readonly InventoryRepository _inventory;
        private readonly StockSnapshotWriter _snapshotWriter;

        public StockAdjustmentService(InventoryRepository inventory, StockSnapshotWriter snapshotWriter)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            _inventory = inventory;
            _snapshotWriter = snapshotWriter;
        }

        public StockAdjustmentResult AdjustBatchQuantity(StockAdjustmentInput input)
        {
            var result = _inventory.AdjustBatchStock(input);

            if (_snapshotWriter != null)
            {
                try
                {
                    bool snapshotClean = _snapshotWriter.WriteCurrentSnapshot();
                    result.SnapshotUpdated = snapshotClean;
                    if (!snapshotClean)
                        result.SnapshotWarning = "Stock adjustment was saved, but newer stock changes occurred while refreshing StockData.json. It remains queued for refresh.";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError("Stock snapshot update failed after committed stock adjustment: " + ex);
                    result.SnapshotUpdated = false;
                    result.SnapshotWarning = "Stock adjustment was saved, but the portable stock file could not be refreshed. Use Refresh stock file in Inventory & Stock.";
                }
            }

            return result;
        }
    }
}
