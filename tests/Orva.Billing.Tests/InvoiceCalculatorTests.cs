using System;
using System.Collections.Generic;
using Orva.Services.Billing;
using Xunit;

namespace Orva.Billing.Tests
{
    public sealed class InvoiceCalculatorTests
    {
        [Fact]
        public void ExclusiveTax_IntraState_ReconcilesExactly()
        {
            var calc = new InvoiceCalculator();
            var medicine = new BillingMedicineRecord
            {
                MedicineId = "m1",
                DisplayName = "Paracetamol 500mg",
                StockUnitName = "strip",
                GstRateBasisPoints = 1200,
                GstRateConfigured = true
            };
            var input = new InvoiceLineCalculationInput
            {
                Medicine = medicine,
                RequestedQuantityMilli = 2000, // 2 strips
                Allocations = new List<FefoBatchAllocation>
                {
                    new FefoBatchAllocation
                    {
                        BatchId = "b1",
                        BatchNumber = "B1",
                        ExpiryYyyyMm = 202812,
                        QuantityMilli = 2000,
                        UnitPricePaise = 2500, // Rs 25.00
                        StockUnitName = "strip",
                        PreviousQuantityMilli = 10000
                    }
                }
            };

            var policy = new ResolvedTaxPolicy
            {
                PricingMode = TaxPricingMode.TaxExclusive,
                SupplyType = SupplyType.IntraState,
                RoundOffMode = RoundOffMode.None
            };

            var invoice = calc.Calculate(new List<InvoiceLineCalculationInput> { input }, policy);

            Assert.Equal(5000, invoice.SubtotalPaise);
            Assert.Equal(0, invoice.DiscountPaise);
            Assert.Equal(5000, invoice.TaxableValuePaise);
            Assert.Equal(300, invoice.CgstPaise);
            Assert.Equal(300, invoice.SgstPaise);
            Assert.Equal(0, invoice.IgstPaise);
            Assert.Equal(5600, invoice.GrandTotalPaise);
            Assert.Equal(invoice.GrandTotalPaise, invoice.TaxableValuePaise + invoice.CgstPaise + invoice.SgstPaise + invoice.IgstPaise + invoice.RoundOffPaise);
        }

        [Fact]
        public void InclusiveTax_InterState_ReconcilesExactly()
        {
            var calc = new InvoiceCalculator();
            var medicine = new BillingMedicineRecord
            {
                MedicineId = "m1",
                DisplayName = "Amoxicillin 500mg",
                StockUnitName = "strip",
                GstRateBasisPoints = 1200,
                GstRateConfigured = true
            };
            var input = new InvoiceLineCalculationInput
            {
                Medicine = medicine,
                RequestedQuantityMilli = 1000,
                Allocations = new List<FefoBatchAllocation>
                {
                    new FefoBatchAllocation
                    {
                        BatchId = "b1",
                        BatchNumber = "B1",
                        ExpiryYyyyMm = 202812,
                        QuantityMilli = 1000,
                        UnitPricePaise = 11200, // Rs 112.00 MRP inclusive of 12% GST
                        StockUnitName = "strip",
                        PreviousQuantityMilli = 5000
                    }
                }
            };

            var policy = new ResolvedTaxPolicy
            {
                PricingMode = TaxPricingMode.TaxInclusive,
                SupplyType = SupplyType.InterState,
                RoundOffMode = RoundOffMode.None
            };

            var invoice = calc.Calculate(new List<InvoiceLineCalculationInput> { input }, policy);

            Assert.Equal(11200, invoice.SubtotalPaise);
            Assert.Equal(10000, invoice.TaxableValuePaise);
            Assert.Equal(0, invoice.CgstPaise);
            Assert.Equal(0, invoice.SgstPaise);
            Assert.Equal(1200, invoice.IgstPaise);
            Assert.Equal(11200, invoice.GrandTotalPaise);
        }

        [Fact]
        public void OddPaiseTax_IntraState_BalancesCgstAndSgstDeterministically()
        {
            var calc = new InvoiceCalculator();
            var medicine = new BillingMedicineRecord
            {
                MedicineId = "m1",
                DisplayName = "Test Med",
                StockUnitName = "tablet",
                GstRateBasisPoints = 500, // 5%
                GstRateConfigured = true
            };

            // 5% of 100 paise = 5 paise (odd). Two such lines => 5 + 5 = 10 paise total -> CGST 5, SGST 5.
            var inputs = new List<InvoiceLineCalculationInput>
            {
                new InvoiceLineCalculationInput
                {
                    Medicine = medicine,
                    RequestedQuantityMilli = 1000,
                    Allocations = new List<FefoBatchAllocation>
                    {
                        new FefoBatchAllocation { BatchId = "b1", BatchNumber = "B1", ExpiryYyyyMm = 202812, QuantityMilli = 1000, UnitPricePaise = 100, StockUnitName = "tablet", PreviousQuantityMilli = 5000 }
                    }
                },
                new InvoiceLineCalculationInput
                {
                    Medicine = medicine,
                    RequestedQuantityMilli = 1000,
                    Allocations = new List<FefoBatchAllocation>
                    {
                        new FefoBatchAllocation { BatchId = "b2", BatchNumber = "B2", ExpiryYyyyMm = 202812, QuantityMilli = 1000, UnitPricePaise = 100, StockUnitName = "tablet", PreviousQuantityMilli = 5000 }
                    }
                }
            };

            var policy = new ResolvedTaxPolicy
            {
                PricingMode = TaxPricingMode.TaxExclusive,
                SupplyType = SupplyType.IntraState,
                RoundOffMode = RoundOffMode.None
            };

            var invoice = calc.Calculate(inputs, policy);

            Assert.Equal(5, invoice.CgstPaise);
            Assert.Equal(5, invoice.SgstPaise);
            Assert.Equal(210, invoice.GrandTotalPaise);
        }

        [Fact]
        public void FinancialYearLabel_HandlesAprilBoundaryCorrectly()
        {
            Assert.Equal("25-26", InvoiceCalculator.GetFinancialYearLabel(new DateTime(2026, 3, 31), 4));
            Assert.Equal("26-27", InvoiceCalculator.GetFinancialYearLabel(new DateTime(2026, 4, 1), 4));
            Assert.Equal("2026", InvoiceCalculator.GetFinancialYearLabel(new DateTime(2026, 1, 15), 1));
        }

        [Fact]
        public void NearestRupeeRoundOff_ReconcilesGrandTotal()
        {
            Assert.Equal(10000, InvoiceCalculator.ApplyRoundOff(10049, RoundOffMode.NearestRupee));
            Assert.Equal(10100, InvoiceCalculator.ApplyRoundOff(10050, RoundOffMode.NearestRupee));
        }
    }
}