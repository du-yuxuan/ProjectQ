// ============================================================
// SimpleQR.cs
// 纯 C# QR Code 生成（Byte 模式 + ECC Level M，Version 1–10）
// 无外部依赖，用于 WalletConnect URI 展示。
// ============================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Q.Pico
{
    public static class SimpleQR
    {
        // ECC-M: 数据容量(字节) / 总码字 / ECC 码字 / 块数
        static readonly int[] DataCapacity = { 0, 14, 26, 42, 62, 84, 106, 122, 152, 180, 213 };
        static readonly int[] TotalCodewords = { 0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346 };
        static readonly int[] EccCodewords = { 0, 10, 16, 26, 36, 48, 64, 72, 88, 110, 130 };
        static readonly int[] BlockCount = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        static readonly int[][] AlignCenters =
        {
            null,
            new int[0],
            new int[] { 6, 18 },
            new int[] { 6, 22 },
            new int[] { 6, 26 },
            new int[] { 6, 30 },
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 },
        };

        // GF(256) tables
        static readonly byte[] Exp = new byte[512];
        static readonly byte[] Log = new byte[256];
        static bool gfReady;

        public static Sprite CreateSprite(string content, int pixelsPerModule = 6, int quietZone = 2)
        {
            var tex = CreateTexture(content, pixelsPerModule, quietZone);
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Texture2D CreateTexture(string content, int pixelsPerModule = 6, int quietZone = 2)
        {
            bool[,] modules = Encode(content);
            if (modules == null) return null;

            int size = modules.GetLength(0);
            int dim = (size + quietZone * 2) * pixelsPerModule;
            var tex = new Texture2D(dim, dim, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[dim * dim];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!modules[y, x]) continue;
                    int px0 = (x + quietZone) * pixelsPerModule;
                    int py0 = (size - 1 - y + quietZone) * pixelsPerModule;
                    for (int dy = 0; dy < pixelsPerModule; dy++)
                    for (int dx = 0; dx < pixelsPerModule; dx++)
                        pixels[(py0 + dy) * dim + (px0 + dx)] = black;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        public static bool[,] Encode(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            EnsureGf();

            byte[] data = Encoding.UTF8.GetBytes(content);
            int version = PickVersion(data.Length);
            if (version < 0)
            {
                // 截断到 v10
                version = 10;
                int maxPayload = DataCapacity[10] - 3;
                if (data.Length > maxPayload)
                {
                    var t = new byte[maxPayload];
                    Array.Copy(data, t, maxPayload);
                    data = t;
                }
                Debug.LogWarning($"[SimpleQR] 内容过长，已截断到 {data.Length} bytes (v10)");
            }

            int size = 17 + 4 * version;
            int totalCw = TotalCodewords[version];
            int eccCw = EccCodewords[version];
            int dataCw = totalCw - eccCw;
            int blocks = BlockCount[version];
            int eccPerBlock = eccCw / blocks;

            // ---- bit stream ----
            var bits = new BitBuffer();
            bits.Append(0b0100, 4); // Byte mode
            bits.Append(data.Length, version >= 10 ? 16 : 8);
            foreach (byte b in data) bits.Append(b, 8);

            int capacityBits = dataCw * 8;
            int term = Math.Min(4, capacityBits - bits.BitLength);
            if (term > 0) bits.Append(0, term);
            while (bits.BitLength % 8 != 0) bits.Append(0, 1);

            byte[] pads = { 0xEC, 0x11 };
            int pi = 0;
            while (bits.BitLength / 8 < dataCw)
            {
                bits.Append(pads[pi & 1], 8);
                pi++;
            }

            byte[] dataCodewords = bits.ToBytes(dataCw);

            // ---- split blocks ----
            int shortDataLen = dataCw / blocks;
            int numLongBlocks = dataCw % blocks;
            int numShortBlocks = blocks - numLongBlocks;

            var dataBlocks = new List<byte[]>(blocks);
            int off = 0;
            for (int i = 0; i < blocks; i++)
            {
                int len = i < numShortBlocks ? shortDataLen : shortDataLen + 1;
                var block = new byte[len];
                Array.Copy(dataCodewords, off, block, 0, len);
                off += len;
                dataBlocks.Add(block);
            }

            var eccBlocks = new List<byte[]>(blocks);
            byte[] generator = BuildGenerator(eccPerBlock);
            foreach (var db in dataBlocks)
                eccBlocks.Add(RsEncode(db, generator, eccPerBlock));

            // ---- interleave ----
            var finalCw = new byte[totalCw];
            int p = 0;
            int maxData = shortDataLen + (numLongBlocks > 0 ? 1 : 0);
            for (int i = 0; i < maxData; i++)
            {
                foreach (var b in dataBlocks)
                    if (i < b.Length) finalCw[p++] = b[i];
            }
            for (int i = 0; i < eccPerBlock; i++)
            {
                foreach (var b in eccBlocks)
                    finalCw[p++] = b[i];
            }

            // ---- matrix + best mask ----
            bool[,] function = new bool[size, size];
            bool[,] reserved = new bool[size, size];
            DrawFunctionPatterns(function, reserved, version, size);

            int bestPenalty = int.MaxValue;
            bool[,] best = null;

            for (int mask = 0; mask < 8; mask++)
            {
                var modules = (bool[,])function.Clone();
                var res = (bool[,])reserved.Clone();
                PlaceDataBits(modules, res, finalCw, size, mask);
                PlaceFormatInfo(modules, mask, size);
                if (version >= 7) PlaceVersionInfo(modules, version, size);

                int penalty = CalcPenalty(modules, size);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    best = modules;
                }
            }

            return best;
        }

        static int PickVersion(int dataLen)
        {
            for (int v = 1; v <= 10; v++)
            {
                // mode(4) + count(8/16) + data + terminator overhead ≈ dataLen + 3
                if (dataLen + 3 <= DataCapacity[v]) return v;
            }
            return -1;
        }

        // ============================================================
        // Function patterns
        // ============================================================

        static void DrawFunctionPatterns(bool[,] m, bool[,] r, int version, int size)
        {
            DrawFinder(m, r, 0, 0, size);
            DrawFinder(m, r, 0, size - 7, size);
            DrawFinder(m, r, size - 7, 0, size);

            // separators (white reserved)
            for (int i = 0; i < 8; i++)
            {
                Reserve(r, 7, i, size); Reserve(r, i, 7, size);
                Reserve(r, 7, size - 8 + i, size); Reserve(r, i, size - 8, size);
                Reserve(r, size - 8, i, size); Reserve(r, size - 1 - i, 7, size);
            }

            // timing
            for (int i = 8; i < size - 8; i++)
            {
                SetFunc(m, r, 6, i, i % 2 == 0, size);
                SetFunc(m, r, i, 6, i % 2 == 0, size);
            }

            // alignment
            if (version >= 2)
            {
                int[] c = AlignCenters[version];
                for (int i = 0; i < c.Length; i++)
                for (int j = 0; j < c.Length; j++)
                {
                    int row = c[i], col = c[j];
                    if (OverlapsFinder(row, col, size)) continue;
                    DrawAlignment(m, r, row, col, size);
                }
            }

            // dark module
            SetFunc(m, r, 4 * version + 9, 8, true, size);

            // reserve format
            for (int i = 0; i < 9; i++)
            {
                Reserve(r, 8, i, size);
                Reserve(r, i, 8, size);
            }
            for (int i = 0; i < 8; i++)
            {
                Reserve(r, 8, size - 1 - i, size);
                Reserve(r, size - 1 - i, 8, size);
            }

            if (version >= 7)
            {
                for (int i = 0; i < 6; i++)
                for (int j = 0; j < 3; j++)
                {
                    Reserve(r, i, size - 11 + j, size);
                    Reserve(r, size - 11 + j, i, size);
                }
            }
        }

        static bool OverlapsFinder(int row, int col, int size)
        {
            return (row < 9 && col < 9) ||
                   (row < 9 && col > size - 10) ||
                   (row > size - 10 && col < 9);
        }

        static void DrawFinder(bool[,] m, bool[,] r, int row, int col, int size)
        {
            for (int dy = 0; dy < 7; dy++)
            for (int dx = 0; dx < 7; dx++)
            {
                bool dark = dx == 0 || dx == 6 || dy == 0 || dy == 6 ||
                            (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4);
                SetFunc(m, r, row + dy, col + dx, dark, size);
            }
        }

        static void DrawAlignment(bool[,] m, bool[,] r, int cy, int cx, int size)
        {
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int d = Math.Max(Math.Abs(dy), Math.Abs(dx));
                bool dark = d == 0 || d == 2;
                SetFunc(m, r, cy + dy, cx + dx, dark, size);
            }
        }

        static void SetFunc(bool[,] m, bool[,] r, int y, int x, bool dark, int size)
        {
            if (y < 0 || x < 0 || y >= size || x >= size) return;
            m[y, x] = dark;
            r[y, x] = true;
        }

        static void Reserve(bool[,] r, int y, int x, int size)
        {
            if (y < 0 || x < 0 || y >= size || x >= size) return;
            r[y, x] = true;
        }

        // ============================================================
        // Data placement + mask
        // ============================================================

        static void PlaceDataBits(bool[,] m, bool[,] r, byte[] data, int size, int mask)
        {
            int bit = 0;
            int total = data.Length * 8;
            int dir = -1;
            for (int col = size - 1; col > 0; col -= 2)
            {
                if (col == 6) col--;
                for (int i = 0; i < size; i++)
                {
                    int row = dir < 0 ? size - 1 - i : i;
                    for (int c = 0; c < 2; c++)
                    {
                        int x = col - c;
                        if (r[row, x]) continue;
                        bool dark = false;
                        if (bit < total)
                        {
                            int b = data[bit >> 3];
                            dark = ((b >> (7 - (bit & 7))) & 1) == 1;
                            bit++;
                        }
                        if (Mask(mask, row, x)) dark = !dark;
                        m[row, x] = dark;
                    }
                }
                dir = -dir;
            }
        }

        static bool Mask(int mask, int row, int col)
        {
            switch (mask)
            {
                case 0: return ((row + col) & 1) == 0;
                case 1: return (row & 1) == 0;
                case 2: return col % 3 == 0;
                case 3: return (row + col) % 3 == 0;
                case 4: return ((row / 2 + col / 3) & 1) == 0;
                case 5: return (row * col) % 2 + (row * col) % 3 == 0;
                case 6: return (((row * col) % 2 + (row * col) % 3) & 1) == 0;
                case 7: return (((row + col) % 2 + (row * col) % 3) & 1) == 0;
                default: return false;
            }
        }

        // ECC Level M = 00
        static void PlaceFormatInfo(bool[,] m, int mask, int size)
        {
            int data = (0b00 << 3) | mask; // M + mask
            int bch = BchEncode(data, 0x537, 10, 5);
            int bits = ((data << 10) | bch) ^ 0x5412;

            // pattern 1 (around TL finder)
            int[] ys = { 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0 };
            int[] xs = { 0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8 };
            for (int i = 0; i < 15; i++)
                m[ys[i], xs[i]] = ((bits >> i) & 1) == 1;

            // pattern 2
            int[] ys2 = { size - 1, size - 2, size - 3, size - 4, size - 5, size - 6, size - 7, 8, 8, 8, 8, 8, 8, 8, 8 };
            int[] xs2 = { 8, 8, 8, 8, 8, 8, 8, size - 8, size - 7, size - 6, size - 5, size - 4, size - 3, size - 2, size - 1 };
            // bit 7 is dark module already at (size-8, 8) — overwrite carefully
            // Standard second copy:
            // bits 0..6 vertical bottom-left, bits 7..14 horizontal bottom-right of top
            // Fix mapping to ISO:
            for (int i = 0; i < 7; i++)
                m[size - 1 - i, 8] = ((bits >> i) & 1) == 1;
            for (int i = 0; i < 8; i++)
                m[8, size - 8 + i] = ((bits >> (i + 7)) & 1) == 1;
        }

        static void PlaceVersionInfo(bool[,] m, int version, int size)
        {
            int bch = BchEncode(version, 0x1F25, 12, 6);
            int bits = (version << 12) | bch;
            for (int i = 0; i < 18; i++)
            {
                bool dark = ((bits >> i) & 1) == 1;
                int a = i / 3;
                int b = i % 3;
                m[a, size - 11 + b] = dark;
                m[size - 11 + b, a] = dark;
            }
        }

        static int BchEncode(int data, int poly, int eccBits, int dataBits)
        {
            int v = data << eccBits;
            for (int i = dataBits + eccBits - 1; i >= eccBits; i--)
            {
                if (((v >> i) & 1) != 0)
                    v ^= poly << (i - eccBits);
            }
            return v & ((1 << eccBits) - 1);
        }

        static int CalcPenalty(bool[,] m, int size)
        {
            int dark = 0;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (m[y, x]) dark++;
            int total = size * size;
            int pct = dark * 100 / Math.Max(1, total);
            int penalty = Math.Abs(pct - 50) / 5 * 10;

            // N1: runs of 5+
            for (int y = 0; y < size; y++)
            {
                int run = 1;
                for (int x = 1; x < size; x++)
                {
                    if (m[y, x] == m[y, x - 1]) run++;
                    else { if (run >= 5) penalty += 3 + (run - 5); run = 1; }
                }
                if (run >= 5) penalty += 3 + (run - 5);
            }
            return penalty;
        }

        // ============================================================
        // Reed-Solomon
        // ============================================================

        static void EnsureGf()
        {
            if (gfReady) return;
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;
                x <<= 1;
                if (x >= 256) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
            gfReady = true;
        }

        static byte GfMul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        static byte[] BuildGenerator(int degree)
        {
            // g(x) = (x-α^0)(x-α^1)...(x-α^{degree-1})
            var g = new List<byte> { 1 };
            for (int i = 0; i < degree; i++)
            {
                // multiply by (x - α^i) = (x + α^i) in GF
                var next = new List<byte>(new byte[g.Count + 1]);
                for (int j = 0; j < g.Count; j++)
                {
                    next[j] ^= g[j]; // * x
                    next[j + 1] ^= GfMul(g[j], Exp[i]);
                }
                g = next;
            }
            // drop leading 1 → remainder generator coeffs
            var coeffs = new byte[degree];
            for (int i = 0; i < degree; i++)
                coeffs[i] = g[i + 1];
            return coeffs;
        }

        static byte[] RsEncode(byte[] data, byte[] generator, int eccLen)
        {
            var rem = new byte[eccLen];
            foreach (byte b in data)
            {
                byte factor = (byte)(b ^ rem[0]);
                Array.Copy(rem, 1, rem, 0, eccLen - 1);
                rem[eccLen - 1] = 0;
                if (factor == 0) continue;
                for (int i = 0; i < eccLen; i++)
                    rem[i] ^= GfMul(generator[i], factor);
            }
            return rem;
        }

        // ============================================================
        // Bit buffer
        // ============================================================

        class BitBuffer
        {
            readonly List<byte> bytes = new List<byte>();
            public int BitLength { get; private set; }

            public void Append(int value, int len)
            {
                for (int i = len - 1; i >= 0; i--)
                {
                    int idx = BitLength >> 3;
                    while (bytes.Count <= idx) bytes.Add(0);
                    if (((value >> i) & 1) != 0)
                        bytes[idx] |= (byte)(0x80 >> (BitLength & 7));
                    BitLength++;
                }
            }

            public byte[] ToBytes(int count)
            {
                var arr = new byte[count];
                for (int i = 0; i < count && i < bytes.Count; i++)
                    arr[i] = bytes[i];
                return arr;
            }
        }
    }
}
