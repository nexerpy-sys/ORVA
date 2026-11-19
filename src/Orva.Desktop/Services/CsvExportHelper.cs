using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Orva.Services
{
    /// <summary>
    /// RFC-4180 CSV exporter using UTF-8 with BOM so Excel on Windows renders ₹ and Bengali cleanly.
    /// </summary>
    public static class CsvExportHelper
    {
        public static void WriteCsv(string filePath, IList<string> headers, IList<IList<string>> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", "filePath");

            var sb = new StringBuilder();
            AppendRow(sb, headers);
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    AppendRow(sb, rows[i]);
                }
            }

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public static string EscapeField(string value)
        {
            if (value == null) return string.Empty;
            value = NeutralizeFormula(value);
            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void AppendRow(StringBuilder sb, IList<string> fields)
        {
            if (fields == null) return;
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscapeField(fields[i]));
            }
            sb.Append("\r\n");
        }

        private static string NeutralizeFormula(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            char first = raw[0];
            if (first == '=' || first == '+' || first == '@' || first == '\t' || first == '\r')
                return "'" + raw;
            if (first == '-')
            {
                decimal num;
                if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out num))
                    return "'" + raw;
            }
            return raw;
        }
    }
}
