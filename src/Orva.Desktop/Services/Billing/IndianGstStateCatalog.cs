using System;
using System.Collections.Generic;

namespace Orva.Services.Billing
{
    public sealed class IndianGstState
    {
        public IndianGstState(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public string Code { get; private set; }
        public string Name { get; private set; }
        public string DisplayLabel { get { return Code + " — " + Name; } }
    }

    /// <summary>
    /// Official 2-digit Indian GST State / Union Territory code catalogue (CBIC / GSTN).
    /// Used for shop state selection and GSTIN state-prefix validation.
    /// </summary>
    public static class IndianGstStateCatalog
    {
        private static readonly List<IndianGstState> _states = new List<IndianGstState>
        {
            new IndianGstState("01", "Jammu and Kashmir"),
            new IndianGstState("02", "Himachal Pradesh"),
            new IndianGstState("03", "Punjab"),
            new IndianGstState("04", "Chandigarh"),
            new IndianGstState("05", "Uttarakhand"),
            new IndianGstState("06", "Haryana"),
            new IndianGstState("07", "Delhi"),
            new IndianGstState("08", "Rajasthan"),
            new IndianGstState("09", "Uttar Pradesh"),
            new IndianGstState("10", "Bihar"),
            new IndianGstState("11", "Sikkim"),
            new IndianGstState("12", "Arunachal Pradesh"),
            new IndianGstState("13", "Nagaland"),
            new IndianGstState("14", "Manipur"),
            new IndianGstState("15", "Mizoram"),
            new IndianGstState("16", "Tripura"),
            new IndianGstState("17", "Meghalaya"),
            new IndianGstState("18", "Assam"),
            new IndianGstState("19", "West Bengal"),
            new IndianGstState("20", "Jharkhand"),
            new IndianGstState("21", "Odisha"),
            new IndianGstState("22", "Chhattisgarh"),
            new IndianGstState("23", "Madhya Pradesh"),
            new IndianGstState("24", "Gujarat"),
            new IndianGstState("26", "Dadra and Nagar Haveli and Daman and Diu"),
            new IndianGstState("27", "Maharashtra"),
            new IndianGstState("29", "Karnataka"),
            new IndianGstState("30", "Goa"),
            new IndianGstState("31", "Lakshadweep"),
            new IndianGstState("32", "Kerala"),
            new IndianGstState("33", "Tamil Nadu"),
            new IndianGstState("34", "Puducherry"),
            new IndianGstState("35", "Andaman and Nicobar Islands"),
            new IndianGstState("36", "Telangana"),
            new IndianGstState("37", "Andhra Pradesh"),
            new IndianGstState("38", "Ladakh")
        };

        private static readonly Dictionary<string, IndianGstState> _byCode;
        private static readonly Dictionary<string, IndianGstState> _byName;

        static IndianGstStateCatalog()
        {
            _byCode = new Dictionary<string, IndianGstState>(StringComparer.Ordinal);
            _byName = new Dictionary<string, IndianGstState>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                _byCode[state.Code] = state;
                _byName[state.Name] = state;
            }
        }

        public static IList<IndianGstState> AllStates
        {
            get { return _states.AsReadOnly(); }
        }

        public static bool IsValidShopStateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            return _byCode.ContainsKey(code.Trim());
        }

        public static bool IsValidGstinStateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            string trimmed = code.Trim();
            if (_byCode.ContainsKey(trimmed)) return true;
            // 97 = Other Territory, 99 = Centre Jurisdiction (valid in GSTIN prefix)
            return string.Equals(trimmed, "97", StringComparison.Ordinal)
                || string.Equals(trimmed, "99", StringComparison.Ordinal);
        }

        public static IndianGstState FindByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            IndianGstState state;
            return _byCode.TryGetValue(code.Trim(), out state) ? state : null;
        }

        public static IndianGstState FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            IndianGstState state;
            return _byName.TryGetValue(name.Trim(), out state) ? state : null;
        }
    }
}
