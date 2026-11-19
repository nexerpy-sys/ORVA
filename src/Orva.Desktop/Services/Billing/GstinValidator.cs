using System;

namespace Orva.Services.Billing
{
    public sealed class GstinValidationResult
    {
        public bool IsValid { get; set; }
        public string NormalizedGstin { get; set; }
        public string StateCode { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Validates Indian 15-character GSTIN structure and official GSTN Mod-36 checksum.
    /// Note: Structural and checksum validation confirms format integrity only;
    /// it does not prove that a GSTIN is currently active on the GST portal.
    /// </summary>
    public static class GstinValidator
    {
        private const string ChecksumAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static GstinValidationResult Validate(string rawGstin)
        {
            if (string.IsNullOrWhiteSpace(rawGstin))
            {
                return Invalid("GSTIN cannot be empty.");
            }

            string gstin = rawGstin.Trim().ToUpperInvariant();
            if (gstin.Length != 15)
            {
                return Invalid("GSTIN must be exactly 15 characters.");
            }

            string stateCode = gstin.Substring(0, 2);
            if (!char.IsDigit(stateCode[0]) || !char.IsDigit(stateCode[1]) || !IndianGstStateCatalog.IsValidGstinStateCode(stateCode))
            {
                return Invalid("GSTIN does not begin with a valid 2-digit Indian GST state code.");
            }

            // Positions 2..11 must match Indian PAN structure: AAAAA9999A
            for (int i = 2; i <= 6; i++)
            {
                if (!IsAsciiUpperLetter(gstin[i]))
                    return Invalid("GSTIN structure is invalid (characters 3-7 must be letters).");
            }
            for (int i = 7; i <= 10; i++)
            {
                if (!char.IsDigit(gstin[i]))
                    return Invalid("GSTIN structure is invalid (characters 8-11 must be digits).");
            }
            if (!IsAsciiUpperLetter(gstin[11]))
            {
                return Invalid("GSTIN structure is invalid (character 12 must be a letter).");
            }

            // Position 12: Entity number (1-9 or A-Z, cannot be '0')
            char entityChar = gstin[12];
            if (!(entityChar >= '1' && entityChar <= '9') && !IsAsciiUpperLetter(entityChar))
            {
                return Invalid("GSTIN structure is invalid (character 13 must be 1-9 or A-Z).");
            }

            // Position 13: Default 'Z'
            if (gstin[13] != 'Z')
            {
                return Invalid("GSTIN structure is invalid (character 14 must be 'Z').");
            }

            // Position 14: Official Mod-36 checksum character
            char expectedChecksum = ComputeChecksumCharacter(gstin.Substring(0, 14));
            if (gstin[14] != expectedChecksum)
            {
                return Invalid("GSTIN checksum is invalid. Please re-check the 15-character GSTIN.");
            }

            return new GstinValidationResult
            {
                IsValid = true,
                NormalizedGstin = gstin,
                StateCode = stateCode,
                ErrorMessage = null
            };
        }

        public static char ComputeChecksumCharacter(string first14Characters)
        {
            if (string.IsNullOrEmpty(first14Characters) || first14Characters.Length != 14)
                throw new ArgumentException("Exactly 14 characters are required to compute the GSTIN checksum.", "first14Characters");

            int sum = 0;
            for (int i = 0; i < 14; i++)
            {
                int codePoint = ChecksumAlphabet.IndexOf(first14Characters[i]);
                if (codePoint < 0)
                    throw new ArgumentException("Invalid character in GSTIN prefix: " + first14Characters[i], "first14Characters");

                int factor = (i % 2 == 0) ? 1 : 2;
                int product = codePoint * factor;
                int digit = (product / 36) + (product % 36);
                sum += digit;
            }

            int remainder = sum % 36;
            int checkCodePoint = (36 - remainder) % 36;
            return ChecksumAlphabet[checkCodePoint];
        }

        private static bool IsAsciiUpperLetter(char c)
        {
            return c >= 'A' && c <= 'Z';
        }

        private static GstinValidationResult Invalid(string message)
        {
            return new GstinValidationResult
            {
                IsValid = false,
                ErrorMessage = message
            };
        }
    }
}
