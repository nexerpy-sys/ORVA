namespace Orva.Domain
{
    public sealed class ShopProfile
    {
        public string ShopId { get; set; }
        public string ShopName { get; set; }
        public string LegalName { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string StateName { get; set; }
        public string StateCode { get; set; }
        public string Gstin { get; set; }
        public string DrugLicenceNumber { get; set; }
        public string InvoicePrefix { get; set; }
        public int FinancialYearStartMonth { get; set; }
    }
}