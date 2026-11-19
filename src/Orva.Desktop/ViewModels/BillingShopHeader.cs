using Orva.Domain;

namespace Orva.ViewModels
{
    public sealed class BillingShopHeader
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

        public static BillingShopHeader FromProfile(ShopProfile profile)
        {
            if (profile == null) return new BillingShopHeader();

            return new BillingShopHeader
            {
                ShopId = profile.ShopId,
                ShopName = profile.ShopName,
                LegalName = profile.LegalName,
                Address = profile.Address,
                Phone = profile.Phone,
                Email = profile.Email,
                StateName = profile.StateName,
                StateCode = profile.StateCode,
                Gstin = profile.Gstin,
                DrugLicenceNumber = profile.DrugLicenceNumber,
                InvoicePrefix = profile.InvoicePrefix
            };
        }
    }
}