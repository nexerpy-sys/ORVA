using System;
using System.Collections.Generic;

namespace Orva.Services.Billing
{
    /// <summary>
    /// Deterministic First-Expired-First-Out (FEFO) batch allocator.
    /// Excludes expired, quarantined, blocked, and non-positive batches.
    /// </summary>
    public sealed class FefoAllocator
    {
        public static bool IsBatchEligible(BillingBatchRecord batch, int currentExpiryYearMonth)
        {
            if (batch == null) return false;
            if (!string.Equals(batch.BatchStatus, "ACTIVE", StringComparison.Ordinal)) return false;
            if (batch.QuantityMilli <= 0) return false;
            if (batch.ExpiryYyyyMm < currentExpiryYearMonth) return false;
            return true;
        }

        public FefoAllocationOutcome Allocate(
            IList<BillingBatchRecord> candidateBatches,
            long requestedQuantityMilli,
            int currentExpiryYearMonth)
        {
            if (requestedQuantityMilli <= 0)
                throw new ArgumentOutOfRangeException("requestedQuantityMilli", "Requested quantity must be greater than zero.");

            var eligible = new List<BillingBatchRecord>();
            if (candidateBatches != null)
            {
                for (int i = 0; i < candidateBatches.Count; i++)
                {
                    var batch = candidateBatches[i];
                    if (IsBatchEligible(batch, currentExpiryYearMonth))
                    {
                        eligible.Add(batch);
                    }
                }
            }

            eligible.Sort(CompareFefoOrder);

            long totalAvailable = 0;
            checked
            {
                for (int i = 0; i < eligible.Count; i++)
                {
                    totalAvailable += eligible[i].QuantityMilli;
                }
            }

            var outcome = new FefoAllocationOutcome
            {
                RequestedQuantityMilli = requestedQuantityMilli,
                AvailableQuantityMilli = totalAvailable,
                Succeeded = false
            };

            if (totalAvailable < requestedQuantityMilli)
            {
                return outcome;
            }

            long remaining = requestedQuantityMilli;
            checked
            {
                for (int i = 0; i < eligible.Count && remaining > 0; i++)
                {
                    var batch = eligible[i];
                    long take = batch.QuantityMilli < remaining ? batch.QuantityMilli : remaining;
                    if (take <= 0) continue;

                    outcome.Allocations.Add(new FefoBatchAllocation
                    {
                        BatchId = batch.BatchId,
                        BatchNumber = batch.BatchNumber,
                        ExpiryYyyyMm = batch.ExpiryYyyyMm,
                        QuantityMilli = take,
                        UnitPricePaise = batch.MrpPaise,
                        StockUnitName = batch.StockUnitName,
                        PreviousQuantityMilli = batch.QuantityMilli
                    });

                    remaining -= take;
                }
            }

            outcome.Succeeded = (remaining == 0);
            return outcome;
        }

        private static int CompareFefoOrder(BillingBatchRecord left, BillingBatchRecord right)
        {
            int expiryCompare = left.ExpiryYyyyMm.CompareTo(right.ExpiryYyyyMm);
            if (expiryCompare != 0) return expiryCompare;

            string leftNumber = (left.BatchNumber ?? string.Empty).Trim();
            string rightNumber = (right.BatchNumber ?? string.Empty).Trim();
            int numberCompare = StringComparer.OrdinalIgnoreCase.Compare(leftNumber, rightNumber);
            if (numberCompare != 0) return numberCompare;

            return StringComparer.Ordinal.Compare(left.BatchId ?? string.Empty, right.BatchId ?? string.Empty);
        }
    }
}