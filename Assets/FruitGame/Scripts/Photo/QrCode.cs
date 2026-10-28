using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Minimal QR code encoder for the photo booth's download link (M7d): byte mode, error-correction level M,
    /// versions 1–10 (up to 213 bytes), automatic mask choice by the standard penalty rules. Follows ISO/IEC 18004;
    /// the structure mirrors Project Nayuki's reference implementation. Registers itself as PhotoBooth.QrEncoder.
    /// </summary>
    public static class QrCode
    {
        // Level M per version 1–10: EC codewords per block, then (blocks, data codewords) for group 1 and group 2.
        static readonly int[] EcPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[,] Groups =
        {
            { 0, 0, 0, 0 }, { 1, 16, 0, 0 }, { 1, 28, 0, 0 }, { 1, 44, 0, 0 }, { 2, 32, 0, 0 }, { 2, 43, 0, 0 },
            { 4, 27, 0, 0 }, { 4, 31, 0, 0 }, { 2, 38, 2, 39 }, { 3, 36, 2, 37 }, { 4, 43, 1, 44 },
        };
        static readonly int[][] AlignmentPositions =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => PhotoBooth.QrEncoder = text => ToTexture(Encode(text));

        /// <summary>Encodes text (UTF-8) into a module grid, [y, x], true = dark. Throws if it doesn't fit version 10.</summary>
        public static bool[,] Encode(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            int version = 1;
            while (version <= 10 && DataCapacity(version) * 8 < 4 + CountBits(version) + payload.Length * 8) version++;
            if (version > 10) throw new ArgumentException("Text too long for the built-in QR encoder.");

            byte[] data = BuildData(payload, version);
            byte[] codewords = AddErrorCorrection(data, version);

            int size = version * 4 + 17;
            var modules = new bool[size, size];
            var isFunction = new bool[size, size];
            DrawFunctionPatterns(modules, isFunction, version);
            PlaceData(modules, isFunction, codewords);

            // Try all eight masks; keep the one with the lowest penalty.
            int bestMask = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(modules, isFunction, mask);
                DrawFormatBits(modules, isFunction, mask);
                int penalty = Penalty(modules);
                if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
                ApplyMask(modules, isFunction, mask); // XOR again to undo
            }
            ApplyMask(modules, isFunction, bestMask);
            DrawFormatBits(modules, isFunction, bestMask);
            return modules;
        }

        /// <summary>Renders the grid with a 4-module quiet zone, one pixel per module (point-filtered).</summary>
        public static Texture2D ToTexture(bool[,] modules)
        {
            int size = modules.GetLength(0), border = 4, n = size + border * 2;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "QR" };
            var pixels = new Color32[n * n];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int mx = x - border, my = y - border;
                    bool dark = mx >= 0 && my >= 0 && mx < size && my < size && modules[my, mx];
                    // Texture rows go bottom-up; QR row 0 is the top.
                    pixels[(n - 1 - y) * n + x] = dark ? black : white;
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        static int CountBits(int version) => version < 10 ? 8 : 16;

        static int DataCapacity(int version) =>
            Groups[version, 0] * Groups[version, 1] + Groups[version, 2] * Groups[version, 3];

        static byte[] BuildData(byte[] payload, int version)
        {
            var bits = new List<bool>();
            void Append(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
            Append(0b0100, 4);                       // byte mode
            Append(payload.Length, CountBits(version));
            foreach (byte b in payload) Append(b, 8);

            int capacityBits = DataCapacity(version) * 8;
            Append(0, Math.Min(4, capacityBits - bits.Count)); // terminator
            Append(0, (8 - bits.Count % 8) % 8);               // byte align
            for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) Append(pad, 8);

            var data = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i >> 3] |= (byte)(0x80 >> (i & 7));
            return data;
        }

        static byte[] AddErrorCorrection(byte[] data, int version)
        {
            int ec = EcPerBlock[version];
            byte[] generator = RsGenerator(ec);
            var blocks = new List<byte[]>();
            var ecBlocks = new List<byte[]>();
            int offset = 0;
            for (int g = 0; g < 2; g++)
                for (int b = 0; b < Groups[version, g * 2]; b++)
                {
                    int len = Groups[version, g * 2 + 1];
                    var block = new byte[len];
                    Array.Copy(data, offset, block, 0, len);
                    offset += len;
                    blocks.Add(block);
                    ecBlocks.Add(RsRemainder(block, generator));
                }

            // Interleave: data codewords column by column (longer blocks last), then the EC codewords.
            var result = new List<byte>();
            int maxLen = 0;
            foreach (var b in blocks) maxLen = Math.Max(maxLen, b.Length);
            for (int i = 0; i < maxLen; i++)
                foreach (var b in blocks) if (i < b.Length) result.Add(b[i]);
            for (int i = 0; i < ec; i++)
                foreach (var e in ecBlocks) result.Add(e[i]);
            return result.ToArray();
        }

        // Reed–Solomon over GF(2^8) with polynomial 0x11D.
        static byte GfMultiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        static byte[] RsGenerator(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = GfMultiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = GfMultiply(root, 0x02);
            }
            return result;
        }

        static byte[] RsRemainder(byte[] data, byte[] generator)
        {
            var result = new byte[generator.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= GfMultiply(generator[i], factor);
            }
            return result;
        }

        static void DrawFunctionPatterns(bool[,] m, bool[,] f, int version)
        {
            int size = m.GetLength(0);
            for (int i = 0; i < size; i++)
            {
                Set(m, f, 6, i, i % 2 == 0); // timing patterns
                Set(m, f, i, 6, i % 2 == 0);
            }
            DrawFinder(m, f, 3, 3);
            DrawFinder(m, f, size - 4, 3);
            DrawFinder(m, f, 3, size - 4);

            var align = AlignmentPositions[version];
            for (int i = 0; i < align.Length; i++)
                for (int j = 0; j < align.Length; j++)
                {
                    bool nearFinder = (i == 0 && j == 0) || (i == 0 && j == align.Length - 1) || (i == align.Length - 1 && j == 0);
                    if (nearFinder) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(m, f, align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }

            DrawFormatBits(m, f, 0); // reserve the format areas (real bits are drawn after masking)
            if (version >= 7)
            {
                int rem = version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                long bits = (long)version << 12 | (long)rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(m, f, a, b, bit);
                    Set(m, f, b, a, bit);
                }
            }
        }

        static void DrawFinder(bool[,] m, bool[,] f, int cx, int cy)
        {
            int size = m.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(m, f, x, y, dist != 2 && dist != 4);
                }
        }

        static void DrawFormatBits(bool[,] m, bool[,] f, int mask)
        {
            int size = m.GetLength(0);
            int data = 0 << 3 | mask; // level M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;

            for (int i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(i));
            Set(m, f, 8, 7, Bit(6));
            Set(m, f, 8, 8, Bit(7));
            Set(m, f, 7, 8, Bit(8));
            for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(i));
            for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(i));
            Set(m, f, 8, size - 8, true); // dark module
        }

        static void Set(bool[,] m, bool[,] f, int x, int y, bool dark)
        {
            m[y, x] = dark;
            f[y, x] = true;
        }

        static void PlaceData(bool[,] m, bool[,] f, byte[] codewords)
        {
            int size = m.GetLength(0), i = 0, total = codewords.Length * 8;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5; // skip the vertical timing column
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (f[y, x] || i >= total) continue;
                        m[y, x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] f, int mask)
        {
            int size = m.GetLength(0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (f[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) m[y, x] = !m[y, x];
                }
        }

        /// <summary>The four standard penalty rules: runs, 2×2 blocks, finder-like patterns, dark balance.</summary>
        static int Penalty(bool[,] m)
        {
            int size = m.GetLength(0), penalty = 0, dark = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    var line = new bool[size];
                    for (int b = 0; b < size; b++) line[b] = pass == 0 ? m[a, b] : m[b, a];
                    for (int b = 1; b < size; b++)
                    {
                        if (line[b] == line[b - 1]) run++;
                        else { if (run >= 5) penalty += run - 2; run = 1; }
                    }
                    if (run >= 5) penalty += run - 2;
                    for (int b = 0; b + 10 < size; b++)
                    {
                        bool core = line[b + 4] && !line[b + 5] && line[b + 6] && line[b + 7] && line[b + 8] && !line[b + 9] && line[b + 10];
                        bool core2 = line[b] && !line[b + 1] && line[b + 2] && line[b + 3] && line[b + 4] && !line[b + 5] && line[b + 6];
                        if (core && !line[b] && !line[b + 1] && !line[b + 2] && !line[b + 3]) penalty += 40;
                        if (core2 && !line[b + 7] && !line[b + 8] && !line[b + 9] && !line[b + 10]) penalty += 40;
                    }
                }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                        penalty += 3;
                }
            int total = size * size;
            penalty += Math.Abs(dark * 20 - total * 10) / total * 10;
            return penalty;
        }
    }
}
