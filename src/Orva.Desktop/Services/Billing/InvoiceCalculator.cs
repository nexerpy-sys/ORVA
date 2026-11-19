using System;
using System.Collections.Generic;
using System.Globalization;

namespace Orva.Services.Billing
{
    /// <summary>
    /// Pure integer money, tax, round-off, and financial-year calculator.
    /// Uses checked integer arithmetic and exact base-10 rounding (zero binary floating-point).
    /// </summary>
    public sealed class InvoiceCalculator
    {
        public const long QuantityScale = 1000L;
        public const long BasisPointsScale = 10000L;
        public const long PaisePerRupee = 100L;
        public const int MaxInvoiceNumberLength = 16;
        public const int MaxInvoicePrefixLength = 3;

        public CalculatedInvoice Calculate(
            IList<InvoiceLineCalculationInput> lineInputs,
            ResolvedTaxPolicy taxPolicy)
        {
            if (lineInputs == null) throw new ArgumentNullException("lineInputs");
            if (taxPolicy == null) throw new ArgumentNullException("taxPolicy");

            var invoice = new CalculatedInvoice();
            // Tracks odd-paise parity across intra-state batch allocations so CGST and SGST
            // remain balanced within +/- 1 paise both per line and across the entire invoice.
            bool nextOddPaiseToCgst = true;

            checked
            {
                for (int i = 0; i < lineInputs.Count; i++)
                {
                    var input = lineInputs[i];
                    var medicineLine = new CalculatedMedicineLine
                    {
                        LineGroupId = Guid.NewGuid().ToString("N"),
                        MedicineId = input.Medicine.MedicineId,
                        MedicineName = input.Medicine.DisplayName,
                        StockUnitName = input.Medicine.StockUnitName,
                        QuantityMilli = input.RequestedQuantityMilli
                    };

                    for (int j = 0; j < input.Allocations.Count; j++)
                    {
                        var alloc = input.Allocations[j];
                        var batchLine = CalculateBatchLine(
                            alloc,
                            input.Medicine.GstRateBasisPoints,
                            input.Medicine.HsnCode,
                            taxPolicy.PricingMode,
                            taxPolicy.SupplyType,
                            ref nextOddPaiseToCgst);

                        medicineLine.BatchLines.Add(batchLine);
                        medicineLine.SubtotalPaise += batchLine.SubtotalPaise;
                        medicineLine.DiscountPaise += batchLine.DiscountPaise;
                        medicineLine.TaxableValuePaise += batchLine.TaxableValuePaise;
                        medicineLine.CgstPaise += batchLine.CgstPaise;
                        medicineLine.SgstPaise += batchLine.SgstPaise;
                        medicineLine.IgstPaise += batchLine.IgstPaise;
                        medicineLine.LineTotalPaise += batchLine.LineTotalPaise;
                    }

                    invoice.Lines.Add(medicineLine);
                    invoice.SubtotalPaise += medicineLine.SubtotalPaise;
                    invoice.DiscountPaise += medicineLine.DiscountPaise;
                    invoice.TaxableValuePaise += medicineLine.TaxableValuePaise;
                    invoice.CgstPaise += medicineLine.CgstPaise;
                    invoice.SgstPaise += medicineLine.SgstPaise;
                    invoice.IgstPaise += medicineLine.IgstPaise;
                }

                long rawTotal = invoice.TaxableValuePaise + invoice.CgstPaise + invoice.SgstPaise + invoice.IgstPaise;
                long grandTotal = ApplyRoundOff(rawTotal, taxPolicy.RoundOffMode);
                invoice.RoundOffPaise = grandTotal - rawTotal;
                invoice.GrandTotalPaise = grandTotal;
            }

            return invoice;
        }

        public static long CalculateGrossPaise(long quantityMilli, long unitPricePaise)
        {
            if (quantityMilli < 0) throw new ArgumentOutOfRangeException("quantityMilli", "Quantity cannot be negative.");
            if (unitPricePaise < 0) throw new ArgumentOutOfRangeException("unitPricePaise", "Unit price cannot be negative.");

            decimal numerator = (decimal)quantityMilli * (decimal)unitPricePaise;
            return DivideRoundHalfAwayFromZero(numerator, QuantityScale);
        }

        public static long ApplyRoundOff(long rawTotalPaise, RoundOffMode mode)
        {
            if (mode == RoundOffMode.None) return rawTotalPaise;
            if (mode == RoundOffMode.NearestRupee)
            {
                long rupees = DivideRoundHalfAwayFromZero((decimal)rawTotalPaise, PaisePerRupee);
                return checked(rupees * PaisePerRupee);
            }
            throw new ArgumentOutOfRangeException("mode", "Unsupported round-off mode.");
        }

        /// <summary>
        /// Computes the financial year label (e.g. "26-27" for FY April 2026 - March 2027,
        /// or "2026" when start month is January) from the local invoice date.
        /// </summary>
        public static string GetFinancialYearLabel(DateTime localDate, int financialYearStartMonth)
        {
            if (financialYearStartMonth < 1 || financialYearStartMonth > 12)
                throw new ArgumentOutOfRangeException("financialYearStartMonth", "Financial year start month must be between 1 and 12.");

            if (financialYearStartMonth == 1)
            {
                return localDate.Year.ToString("0000", CultureInfo.InvariantCulture);
            }

            int startYear = localDate.Month >= financialYearStartMonth ? localDate.Year : localDate.Year - 1;
            int endYear = startYear + 1;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}-{1:00}",
                startYear % 100,
                endYear % 100);
        }

        public static bool IsValidInvoicePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return false;
            string clean = prefix.Trim();
            if (clean.Length < 1 || clean.Length > MaxInvoicePrefixLength) return false;
            for (int i = 0; i < clean.Length; i++)
            {
                char c = clean[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                    return false;
            }
            return true;
        }

        public static string FormatInvoiceNumber(string prefix, string financialYear, long sequenceNumber)
        {
            if (sequenceNumber <= 0)
                throw new ArgumentOutOfRangeException("sequenceNumber", "Invoice sequence number must be positive.");

            string cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? "INV" : prefix.Trim().ToUpperInvariant();
            string formatted = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2:D6}",
                cleanPrefix,
                financialYear,
                sequenceNumber);

            if (formatted.Length > MaxInvoiceNumberLength)
            {
                throw new InvalidOperationException(
                    "Generated invoice number '" + formatted + "' exceeds the 16-character GST Rule 46 limit. Update the invoice prefix in Settings.");
            }

            return formatted;
        }

        public static string FormatQuantity(long quantityMilli)
        {
            decimal units = (decimal)quantityMilli / (decimal)QuantityScale;
            return units.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string FormatRupees(long paise)
        {
            decimal rupees = (decimal)paise / (decimal)PaisePerRupee;
            return rupees.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
        }

        private static CalculatedBatchLine CalculateBatchLine(
            FefoBatchAllocation allocation,
            int gstRateBasisPoints,
            string hsnCode,
            TaxPricingMode pricingMode,
            SupplyType supplyType,
            ref bool nextOddPaiseToCgst)
        {
            if (gstRateBasisPoints < 0 || gstRateBasisPoints > 10000)
                throw new ArgumentOutOfRangeException("gstRateBasisPoints", "GST rate basis points must be between 0 and 10000.");

            checked
            {
                long grossPaise = CalculateGrossPaise(allocation.QuantityMilli, allocation.UnitPricePaise);
                long discountPaise = 0; // Discounts are disabled in V1.
                long taxableValuePaise;
                long totalTaxPaise;
                long lineTotalPaise;

                if (pricingMode == TaxPricingMode.TaxInclusive)
                {
                    lineTotalPaise = grossPaise;
                    if (gstRateBasisPoints == 0)
                    {
                        taxableValuePaise = grossPaise;
                        totalTaxPaise = 0;
                    }
                    else
                    {
                        decimal numerator = (decimal)grossPaise * (decimal)BasisPointsScale;
                        decimal denominator = (decimal)(BasisPointsScale + gstRateBasisPoints);
                        taxableValuePaise = DivideRoundHalfAwayFromZero(numerator, denominator);
                        totalTaxPaise = grossPaise - taxableValuePaise;
                    }
                }
                else
                {
                    taxableValuePaise = grossPaise;
                    if (gstRateBasisPoints == 0)
                    {
                        totalTaxPaise = 0;
                    }
                    else
                    {
                        decimal numerator = (decimal)taxableValuePaise * (decimal)gstRateBasisPoints;
                        totalTaxPaise = DivideRoundHalfAwayFromZero(numerator, BasisPointsScale);
                    }
                    lineTotalPaise = taxableValuePaise + totalTaxPaise;
                }

                long cgstPaise = 0;
                long sgstPaise = 0;
                long igstPaise = 0;

                if (supplyType == SupplyType.InterState)
                {
                    igstPaise = totalTaxPaise;
                }
                else
                {
                    long half = totalTaxPaise / 2L;
                    long remainder = totalTaxPaise % 2L;
                    if (remainder == 0)
                    {
                        cgstPaise = half;
                        sgstPaise = half;
                    }
                    else if (nextOddPaiseToCgst)
                    {
                        cgstPaise = half + 1L;
                        sgstPaise = half;
                        nextOddPaiseToCgst = false;
                    }
                    else
                    {
                        cgstPaise = half;
                        sgstPaise = half + 1L;
                        nextOddPaiseToCgst = true;
                    }
                }

                return new CalculatedBatchLine
                {
                    BatchId = allocation.BatchId,
                    BatchNumber = allocation.BatchNumber,
                    ExpiryYyyyMm = allocation.ExpiryYyyyMm,
                    QuantityMilli = allocation.QuantityMilli,
                    StockUnitName = allocation.StockUnitName,
                    UnitPricePaise = allocation.UnitPricePaise,
                    GstRateBasisPoints = gstRateBasisPoints,
                    HsnCode = hsnCode,
                    PreviousQuantityMilli = allocation.PreviousQuantityMilli,
                    SubtotalPaise = grossPaise,
                    DiscountPaise = discountPaise,
                    TaxableValuePaise = taxableValuePaise,
                    CgstPaise = cgstPaise,
                    SgstPaise = sgstPaise,
                    IgstPaise = igstPaise,
                    LineTotalPaise = lineTotalPaise
                };
            }
        }

        private static long DivideRoundHalfAwayFromZero(decimal numerator, decimal denominator)
        {
            if (denominator <= 0m) throw new ArgumentOutOfRangeException("denominator");
            decimal quotient = numerator / denominator;
            decimal rounded = Math.Round(quotient, 0, MidpointRounding.AwayFromZero);
            return checked((long)rounded);
        }
    }

    public sealed class InvoiceLineCalculationInput
    {
        public BillingMedicineRecord Medicine { get; set; }
        public long RequestedQuantityMilli { get; set; }
        public IList<FefoBatchAllocation> Allocations { get; set; }
    }
}