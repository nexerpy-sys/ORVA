using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Orva.Services
{
    /// <summary>
    /// Zero-dependency OpenXML (.xlsx) spreadsheet writer using System.IO.Compression.ZipArchive.
    /// Produces clean, valid Excel 2007+ workbooks with bold header row and UTF-8 (English + Bengali) text.
    /// </summary>
    public static class SpreadsheetExportHelper
    {
        public static void WriteXlsx(string filePath, string sheetName, IList<string> headers, IList<IList<string>> rows, bool[] numericColumns = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Output file path is required.", "filePath");

            string safeSheet = SanitizeSheetName(sheetName);
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tempPath = filePath + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                WriteEntry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");

                WriteEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                WriteEntry(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");

                WriteEntry(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"" + EscapeXml(safeSheet) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                    "</workbook>");

                WriteEntry(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "</styleSheet>");

                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

                int rowIndex = 1;
                if (headers != null && headers.Count > 0)
                {
                    AppendRow(sb, rowIndex++, headers, true, null);
                }

                if (rows != null)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        AppendRow(sb, rowIndex++, rows[i], false, numericColumns);
                    }
                }

                sb.Append("</sheetData></worksheet>");
                WriteEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
            }

            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);
        }

        private static void AppendRow(StringBuilder sb, int rowIndex, IList<string> cells, bool isHeader, bool[] numericColumns)
        {
            sb.Append("<row r=\"").Append(rowIndex.ToString(CultureInfo.InvariantCulture)).Append("\">");
            if (cells != null)
            {
                for (int c = 0; c < cells.Count; c++)
                {
                    string colRef = ColumnIndexToName(c) + rowIndex.ToString(CultureInfo.InvariantCulture);
                    string val = cells[c] ?? string.Empty;
                    decimal numVal;
                    bool isNumericCol = numericColumns != null && c < numericColumns.Length && numericColumns[c];
                    string strippedNum = val.Replace(",", string.Empty).Trim();
                    if (!isHeader && isNumericCol && !string.IsNullOrEmpty(strippedNum)
                        && decimal.TryParse(strippedNum, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out numVal))
                    {
                        sb.Append("<c r=\"").Append(colRef).Append("\"><v>")
                          .Append(numVal.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
                    }
                    else
                    {
                        sb.Append("<c r=\"").Append(colRef).Append("\" t=\"inlineStr\"");
                        if (isHeader) sb.Append(" s=\"1\"");
                        sb.Append("><is><t xml:space=\"preserve\">").Append(EscapeXml(val)).Append("</t></is></c>");
                    }
                }
            }
            sb.Append("</row>");
        }

        private static string ColumnIndexToName(int index)
        {
            var sb = new StringBuilder();
            int n = index;
            while (n >= 0)
            {
                sb.Insert(0, (char)('A' + (n % 26)));
                n = (n / 26) - 1;
            }
            return sb.ToString();
        }

        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Report";
            string cleaned = name.Trim();
            char[] invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            for (int i = 0; i < invalid.Length; i++) cleaned = cleaned.Replace(invalid[i], '_');
            return cleaned.Length > 31 ? cleaned.Substring(0, 31) : cleaned;
        }

        private static string EscapeXml(string s)
        {
            return SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;
        }

        private static void WriteEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }
    }
}
