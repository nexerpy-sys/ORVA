using System;
using System.IO;

namespace Orva.Infrastructure
{
    public static class AppPaths
    {
        public static string ProductRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Rindle Works", "ORVA"); }
        }

        public static string DataDirectory { get { return Path.Combine(ProductRoot, "Data"); } }
        public static string DatabasePath { get { return Path.Combine(DataDirectory, "orva.sqlite"); } }
        public static string LogsDirectory { get { return Path.Combine(ProductRoot, "Logs"); } }
        public static string BackupsDirectory { get { return Path.Combine(ProductRoot, "Backups"); } }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(ProductRoot);
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(BackupsDirectory);
        }

        public static string GetShopFolder(string shopName)
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, MakeSafeFolderName(shopName));
        }

        public static string MakeSafeFolderName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "ORVA Shop Data";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '-';
            }
            var result = new string(chars).Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(result)) return "ORVA Shop";
            if (IsReservedWindowsDeviceName(result)) result = "_" + result;
            return result;
        }

        private static bool IsReservedWindowsDeviceName(string name)
        {
            string upper = (name ?? string.Empty).Trim().ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL") return true;
            if (upper.Length == 4 && (upper.StartsWith("COM") || upper.StartsWith("LPT")) && upper[3] >= '1' && upper[3] <= '9') return true;
            return false;
        }
    }
}