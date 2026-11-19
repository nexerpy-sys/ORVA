using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Orva.Controls
{
    /// <summary>
    /// Zero-dependency ISO/IEC 18004 QR Code encoder (Byte mode, ECC Level L, Versions 1..10, up to 271 bytes)
    /// rendering directly to a crisp, scannable WPF DrawingImage for offline UPI payment QR display.
    /// </summary>
    public static class QrCodeHelper
    {
        // Version 1..10 ECC Level L: { totalCodewords, ecPerBlock, numBlocksGroup1, dataCwGroup1, numBlocksGroup2, dataCwGroup2 }
        private static readonly int[][] VersionTableL = new[]
        {
            new[] {   0,  0, 0,  0, 0,  0 }, // 0 unused
            new[] {  26,  7, 1, 19, 0,  0 }, // V1 (21x21)
            new[] {  44, 10, 1, 34, 0,  0 }, // V2 (25x25)
            new[] {  70, 15, 1, 55, 0,  0 }, // V3 (29x29)
            new[] { 100, 20, 1, 80, 0,  0 }, // V4 (33x33)
            new[] { 134, 26, 1, 108, 0, 0 }, // V5 (37x37)
            new[] { 172, 18, 2, 68, 0,  0 }, // V6 (41x41)
            new[] { 196, 20, 2, 78, 0,  0 }, // V7 (45x45)
            new[] { 242, 24, 2, 97, 0,  0 }, // V8 (49x49)
            new[] { 292, 30, 2, 116, 0, 0 }, // V9 (53x53)
            new[] { 346, 18, 2, 68, 2, 69 }  // V10 (57x57)
        };

        private static readonly int[][] AlignmentCoords = new[]
        {
            new int[0],
            new int[0],
            new[] { 6, 18 },
            new[] { 6, 22 },
            new[] { 6, 26 },
            new[] { 6, 30 },
            new[] { 6, 34 },
            new[] { 6, 22, 38 },
            new[] { 6, 24, 42 },
            new[] { 6, 26, 46 },
            new[] { 6, 28, 50 }
        };

        public static bool[,] EncodeMatrix(string payload)
        {
            byte[] data = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            int version = SelectVersion(data.Length);
            if (version < 1) throw new ArgumentException("QR payload exceeds maximum supported length (271 bytes).");

            int[] spec = VersionTableL[version];
            int totalDataCw = spec[2] * spec[3] + spec[4] * spec[5];
            int ecPerBlock = spec[1];

            // 1. Encode data bits (Byte mode 0100 + length + payload + terminator + padding)
            var bits = new List<int>(totalDataCw * 8);
            AppendBits(bits, 0x4, 4); // Byte mode
            AppendBits(bits, data.Length, version <= 9 ? 8 : 16);
            for (int i = 0; i < data.Length; i++) AppendBits(bits, data[i], 8);
            int maxBits = totalDataCw * 8;
            int term = Math.Min(4, maxBits - bits.Count);
            AppendBits(bits, 0, term);
            while (bits.Count % 8 != 0) bits.Add(0);

            byte[] dataCw = new byte[totalDataCw];
            for (int i = 0; i < bits.Count / 8; i++)
            {
                int val = 0;
                for (int b = 0; b < 8; b++) val = (val << 1) | bits[i * 8 + b];
                dataCw[i] = (byte)val;
            }
            byte[] padBytes = new byte[] { 0xEC, 0x11 };
            int padIdx = 0;
            for (int i = bits.Count / 8; i < totalDataCw; i++)
            {
                dataCw[i] = padBytes[padIdx % 2];
                padIdx++;
            }

            // 2. Split into RS blocks, compute ECC, and interleave
            var dataBlocks = new List<byte[]>();
            var ecBlocks = new List<byte[]>();
            int offset = 0;
            for (int b = 0; b < spec[2]; b++)
            {
                byte[] blk = new byte[spec[3]];
                Buffer.BlockCopy(dataCw, offset, blk, 0, spec[3]);
                offset += spec[3];
                dataBlocks.Add(blk);
                ecBlocks.Add(ComputeReedSolomon(blk, ecPerBlock));
            }
            for (int b = 0; b < spec[4]; b++)
            {
                byte[] blk = new byte[spec[5]];
                Buffer.BlockCopy(dataCw, offset, blk, 0, spec[5]);
                offset += spec[5];
                dataBlocks.Add(blk);
                ecBlocks.Add(ComputeReedSolomon(blk, ecPerBlock));
            }

            var finalCw = new List<byte>(spec[0]);
            int maxDataLen = Math.Max(spec[3], spec[5]);
            for (int i = 0; i < maxDataLen; i++)
            {
                for (int b = 0; b < dataBlocks.Count; b++)
                    if (i < dataBlocks[b].Length) finalCw.Add(dataBlocks[b][i]);
            }
            for (int i = 0; i < ecPerBlock; i++)
            {
                for (int b = 0; b < ecBlocks.Count; b++)
                    finalCw.Add(ecBlocks[b][i]);
            }

            // 3. Build module matrix & function patterns
            int size = 17 + (version * 4);
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];

            PlaceFinder(modules, isFunction, 0, 0, size);
            PlaceFinder(modules, isFunction, size - 7, 0, size);
            PlaceFinder(modules, isFunction, 0, size - 7, size);

            // Timing patterns
            for (int i = 8; i < size - 8; i++)
            {
                SetFunc(modules, isFunction, i, 6, i % 2 == 0);
                SetFunc(modules, isFunction, 6, i, i % 2 == 0);
            }

            // Alignment patterns
            int[] align = AlignmentCoords[version];
            for (int i = 0; i < align.Length; i++)
            {
                for (int j = 0; j < align.Length; j++)
                {
                    int cx = align[i], cy = align[j];
                    if ((cx == 6 && cy == 6) || (cx == 6 && cy == size - 7) || (cx == size - 7 && cy == 6)) continue;
                    PlaceAlignment(modules, isFunction, cx, cy);
                }
            }

            // Reserve format info + dark module
            ReserveFormat(modules, isFunction, size);
            if (version >= 7) PlaceVersionInfo(modules, isFunction, version, size);

            // 4. Place data bits (zigzag) + apply mask 0 ((row + col) % 2 == 0)
            int bitIdx = 0;
            int totalBits = finalCw.Count * 8;
            bool upward = true;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                {
                    int y = upward ? (size - 1 - vert) : vert;
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int x = right - dx;
                        if (isFunction[y, x]) continue;
                        bool dark = false;
                        if (bitIdx < totalBits)
                        {
                            dark = ((finalCw[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1) != 0;
                            bitIdx++;
                        }
                        if (((y + x) & 1) == 0) dark = !dark; // Mask 0
                        modules[y, x] = dark;
                    }
                }
                upward = !upward;
            }

            // Write format bits for ECC Level L (01) + Mask 0 (000) -> 15-bit BCH + XOR 0x5412 = 0x77C4
            WriteFormatBits(modules, size, 0x77C4);
            return modules;
        }

        public static DrawingImage RenderToDrawingImage(string payload, Brush darkBrush = null, Brush lightBrush = null)
        {
            bool[,] matrix = EncodeMatrix(payload);
            int size = matrix.GetLength(0);
            int quiet = 4;
            int total = size + (quiet * 2);

            Brush fg = darkBrush ?? new SolidColorBrush(Color.FromRgb(26, 23, 18));
            Brush bg = lightBrush ?? Brushes.White;

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, total, total))));

            var darkGeo = new GeometryGroup();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (matrix[y, x])
                        darkGeo.Children.Add(new RectangleGeometry(new Rect(x + quiet, y + quiet, 1.02, 1.02)));
                }
            }
            group.Children.Add(new GeometryDrawing(fg, null, darkGeo));
            var img = new DrawingImage(group);
            img.Freeze();
            return img;
        }

        private static int SelectVersion(int byteLen)
        {
            for (int v = 1; v <= 10; v++)
            {
                int[] spec = VersionTableL[v];
                int capBits = (spec[2] * spec[3] + spec[4] * spec[5]) * 8;
                int headerBits = 4 + (v <= 9 ? 8 : 16);
                if (headerBits + (byteLen * 8) <= capBits) return v;
            }
            return -1;
        }

        private static void AppendBits(List<int> bits, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--) bits.Add((val >> i) & 1);
        }

        private static void SetFunc(bool[,] mod, bool[,] fn, int x, int y, bool dark)
        {
            mod[y, x] = dark;
            fn[y, x] = true;
        }

        private static void PlaceFinder(bool[,] mod, bool[,] fn, int x0, int y0, int size)
        {
            for (int dy = -1; dy <= 7; dy++)
            {
                for (int dx = -1; dx <= 7; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    bool dark = (dx >= 0 && dx <= 6 && dy >= 0 && dy <= 6) &&
                        (dx == 0 || dx == 6 || dy == 0 || dy == 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4));
                    SetFunc(mod, fn, x, y, dark);
                }
            }
        }

        private static void PlaceAlignment(bool[,] mod, bool[,] fn, int cx, int cy)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunc(mod, fn, cx + dx, cy + dy, dist != 1);
                }
            }
        }

        private static void ReserveFormat(bool[,] mod, bool[,] fn, int size)
        {
            for (int i = 0; i <= 8; i++)
            {
                if (i != 6) { fn[8, i] = true; fn[i, 8] = true; }
            }
            for (int i = 0; i < 8; i++) fn[8, size - 1 - i] = true;
            for (int i = 0; i < 7; i++) fn[size - 1 - i, 8] = true;
            SetFunc(mod, fn, 8, size - 8, true); // dark module
        }

        private static void WriteFormatBits(bool[,] mod, int size, int bits)
        {
            for (int i = 0; i <= 5; i++) mod[8, i] = ((bits >> (14 - i)) & 1) != 0;
            mod[8, 7] = ((bits >> 8) & 1) != 0;
            mod[8, 8] = ((bits >> 7) & 1) != 0;
            mod[7, 8] = ((bits >> 6) & 1) != 0;
            for (int i = 9; i < 15; i++) mod[14 - i, 8] = ((bits >> (14 - i)) & 1) != 0;

            for (int i = 0; i < 8; i++) mod[8, size - 1 - i] = ((bits >> i) & 1) != 0;
            for (int i = 0; i < 7; i++) mod[size - 1 - i, 8] = ((bits >> (14 - i)) & 1) != 0;
        }

        private static void PlaceVersionInfo(bool[,] mod, bool[,] fn, int version, int size)
        {
            int rem = version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool dark = ((bits >> i) & 1) != 0;
                int a = size - 11 + (i % 3);
                int b = i / 3;
                SetFunc(mod, fn, a, b, dark);
                SetFunc(mod, fn, b, a, dark);
            }
        }

        private static byte[] ComputeReedSolomon(byte[] data, int degree)
        {
            byte[] gen = new byte[degree];
            gen[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    gen[j] = GfMul(gen[j], (byte)root);
                    if (j + 1 < degree) gen[j] ^= gen[j + 1];
                }
                root = GfMul((byte)root, 0x02);
            }

            byte[] rem = new byte[degree];
            for (int i = 0; i < data.Length; i++)
            {
                byte factor = (byte)(data[i] ^ rem[0]);
                Array.Copy(rem, 1, rem, 0, degree - 1);
                rem[degree - 1] = 0;
                for (int j = 0; j < degree; j++)
                    rem[j] ^= GfMul(gen[j], factor);
            }
            return rem;
        }

        private static byte GfMul(byte x, byte y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }
    }
}
