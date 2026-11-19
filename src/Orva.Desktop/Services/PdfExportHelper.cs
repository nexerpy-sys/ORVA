using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Orva.Services
{
    /// <summary>
    /// Zero-dependency PDF 1.4 tabular register writer for ORVA Records & Tax exports.
    /// Produces clean multi-page A4 landscape PDF documents with header bar, metadata,
    /// alternating row shading, automatic pagination, and valid xref offsets.
    /// Uses standard built-in Helvetica / Helvetica-Bold PDF Type 1 fonts (offline, no external packages).
    /// </summary>
    public static class PdfExportHelper
    {
        public static void WriteTablePdf(
            string filePath,
            string title,
            string subtitle,
            IList<string> headers,
            IList<IList<string>> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Output file path is required.", "filePath");

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // A4 Landscape: 842 x 595 pt
            const double pageWidth = 842.0;
            const double pageHeight = 595.0;
            const double marginX = 36.0;
            const double marginTop = 36.0;
            const double marginBottom = 36.0;
            const double rowHeight = 18.0;
            const double headerRowHeight = 22.0;
            double usableWidth = pageWidth - (marginX * 2.0);

            int colCount = headers != null ? Math.Max(1, headers.Count) : 1;
            double colWidth = usableWidth / colCount;
            int maxCharsPerCol = Math.Max(8, (int)(colWidth / 5.4));

            var allRows = rows ?? new List<IList<string>>();
            double firstPageTableTop = pageHeight - marginTop - 52.0;
            double nextPageTableTop = pageHeight - marginTop - 26.0;
            int rowsFirstPage = Math.Max(1, (int)((firstPageTableTop - headerRowHeight - marginBottom - 16.0) / rowHeight));
            int rowsPerNextPage = Math.Max(1, (int)((nextPageTableTop - headerRowHeight - marginBottom - 16.0) / rowHeight));

            var pageRowSlices = new List<Tuple<int, int>>();
            if (allRows.Count == 0)
            {
                pageRowSlices.Add(Tuple.Create(0, 0));
            }
            else
            {
                int idx = 0;
                int take = Math.Min(rowsFirstPage, allRows.Count);
                pageRowSlices.Add(Tuple.Create(idx, take));
                idx += take;
                while (idx < allRows.Count)
                {
                    take = Math.Min(rowsPerNextPage, allRows.Count - idx);
                    pageRowSlices.Add(Tuple.Create(idx, take));
                    idx += take;
                }
            }

            var pageStreams = new List<string>();
            int totalPages = pageRowSlices.Count;
            for (int p = 0; p < totalPages; p++)
            {
                var sb = new StringBuilder();
                double tableTop = (p == 0) ? firstPageTableTop : nextPageTableTop;

                // Top header bar (Deep Charcoal Teal #172C30)
                if (p == 0)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "0.090 0.173 0.188 rg {0:0.##} {1:0.##} {2:0.##} 38 re f\n",
                        marginX, pageHeight - marginTop - 38.0, usableWidth);
                    sb.Append("0.953 0.918 0.855 rg\n");
                    DrawText(sb, "F2", 14, marginX + 12.0, pageHeight - marginTop - 17.0, Truncate(title ?? "ORVA Report", 95));
                    DrawText(sb, "F1", 9, marginX + 12.0, pageHeight - marginTop - 31.0, Truncate(subtitle ?? string.Empty, 125));
                }
                else
                {
                    sb.Append("0.090 0.173 0.188 rg\n");
                    DrawText(sb, "F2", 11, marginX, pageHeight - marginTop - 14.0, Truncate((title ?? "ORVA Report") + " (continued)", 95));
                }

                // Table header row background (#E7DCC6)
                double headerBottom = tableTop - headerRowHeight;
                sb.AppendFormat(CultureInfo.InvariantCulture, "0.906 0.863 0.776 rg {0:0.##} {1:0.##} {2:0.##} {3:0.##} re f\n",
                    marginX, headerBottom, usableWidth, headerRowHeight);
                sb.Append("0.102 0.090 0.071 rg\n");

                if (headers != null)
                {
                    for (int c = 0; c < headers.Count; c++)
                    {
                        double x = marginX + (c * colWidth) + 4.0;
                        DrawText(sb, "F2", 8.5, x, headerBottom + 7.0, Truncate(headers[c], maxCharsPerCol));
                    }
                }

                // Data rows
                int startIdx = pageRowSlices[p].Item1;
                int count = pageRowSlices[p].Item2;
                double y = headerBottom;

                if (count == 0)
                {
                    y -= rowHeight;
                    sb.Append("0.35 0.35 0.35 rg\n");
                    DrawText(sb, "F1", 9, marginX + 6.0, y + 5.0, "No records found for the selected filter.");
                }
                else
                {
                    for (int r = 0; r < count; r++)
                    {
                        y -= rowHeight;
                        if (r % 2 == 1)
                        {
                            sb.AppendFormat(CultureInfo.InvariantCulture, "0.984 0.969 0.937 rg {0:0.##} {1:0.##} {2:0.##} {3:0.##} re f\n",
                                marginX, y, usableWidth, rowHeight);
                        }
                        sb.Append("0.102 0.090 0.071 rg\n");
                        var row = allRows[startIdx + r];
                        if (row != null)
                        {
                            for (int c = 0; c < Math.Min(colCount, row.Count); c++)
                            {
                                double x = marginX + (c * colWidth) + 4.0;
                                DrawText(sb, "F1", 8.0, x, y + 5.0, Truncate(row[c], maxCharsPerCol));
                            }
                        }
                        // Thin rule under row
                        sb.AppendFormat(CultureInfo.InvariantCulture, "0.82 0.78 0.72 RG 0.4 w {0:0.##} {1:0.##} m {2:0.##} {1:0.##} l S\n",
                            marginX, y, marginX + usableWidth);
                    }
                }

                // Footer
                sb.Append("0.35 0.35 0.35 rg\n");
                string footerLeft = "ORVA - Pharmacy Billing & Inventory (Rindle Works) | Generated " + DateTime.Now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
                string footerRight = string.Format(CultureInfo.InvariantCulture, "Page {0} of {1}", p + 1, totalPages);
                DrawText(sb, "F1", 8.0, marginX, marginBottom - 14.0, footerLeft);
                DrawText(sb, "F1", 8.0, pageWidth - marginX - 65.0, marginBottom - 14.0, footerRight);

                pageStreams.Add(sb.ToString());
            }

            // Assemble valid PDF 1.4 binary
            // Objects: 1=Catalog, 2=Pages, 3=Font F1 (Helvetica), 4=Font F2 (Helvetica-Bold),
            // then per page i: (5 + 2*i)=Page, (6 + 2*i)=Content stream
            int totalObjects = 4 + (totalPages * 2);
            var offsets = new long[totalObjects + 1];
            string tempPath = filePath + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using (var ms = new MemoryStream())
            {
                WriteAscii(ms, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

                offsets[1] = ms.Position;
                WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                var kids = new StringBuilder();
                for (int i = 0; i < totalPages; i++)
                {
                    if (i > 0) kids.Append(' ');
                    kids.Append(5 + (i * 2)).Append(" 0 R");
                }
                offsets[2] = ms.Position;
                WriteAscii(ms, string.Format(CultureInfo.InvariantCulture,
                    "2 0 obj\n<< /Type /Pages /Kids [{0}] /Count {1} >>\nendobj\n", kids, totalPages));

                offsets[3] = ms.Position;
                WriteAscii(ms, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

                offsets[4] = ms.Position;
                WriteAscii(ms, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

                for (int i = 0; i < totalPages; i++)
                {
                    int pageObj = 5 + (i * 2);
                    int streamObj = 6 + (i * 2);
                    byte[] streamBytes = Encoding.GetEncoding(1252).GetBytes(pageStreams[i]);

                    offsets[pageObj] = ms.Position;
                    WriteAscii(ms, string.Format(CultureInfo.InvariantCulture,
                        "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1:0.##} {2:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {3} 0 R >>\nendobj\n",
                        pageObj, pageWidth, pageHeight, streamObj));

                    offsets[streamObj] = ms.Position;
                    WriteAscii(ms, string.Format(CultureInfo.InvariantCulture,
                        "{0} 0 obj\n<< /Length {1} >>\nstream\n", streamObj, streamBytes.Length));
                    ms.Write(streamBytes, 0, streamBytes.Length);
                    WriteAscii(ms, "\nendstream\nendobj\n");
                }

                long xrefPos = ms.Position;
                WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "xref\n0 {0}\n0000000000 65535 f \n", totalObjects + 1));
                for (int obj = 1; obj <= totalObjects; obj++)
                {
                    WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", offsets[obj]));
                }
                WriteAscii(ms, string.Format(CultureInfo.InvariantCulture,
                    "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n",
                    totalObjects + 1, xrefPos));

                File.WriteAllBytes(tempPath, ms.ToArray());
            }

            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);
        }

        private static void DrawText(StringBuilder sb, string fontName, double size, double x, double y, string text)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "BT /{0} {1:0.##} Tf {2:0.##} {3:0.##} Td ({4}) Tj ET\n",
                fontName, size, x, y, EscapePdfString(text));
        }

        private static string EscapePdfString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string cleaned = raw.Replace("\u20B9", "Rs.");
            var sb = new StringBuilder(cleaned.Length + 8);
            for (int i = 0; i < cleaned.Length; i++)
            {
                char ch = cleaned[i];
                if (ch == '\\' || ch == '(' || ch == ')')
                {
                    sb.Append('\\').Append(ch);
                }
                else if (ch >= 32 && ch <= 126)
                {
                    sb.Append(ch);
                }
                else if (ch == '\r' || ch == '\n' || ch == '\t')
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append('?');
                }
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            string t = s.Trim();
            if (t.Length <= maxLen) return t;
            return maxLen > 3 ? t.Substring(0, maxLen - 3) + "..." : t.Substring(0, maxLen);
        }

        private static void WriteAscii(Stream stream, string text)
        {
            byte[] b = Encoding.ASCII.GetBytes(text);
            stream.Write(b, 0, b.Length);
        }
    }
}
