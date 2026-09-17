/*
 * TerrainPkm.cs
 *
 * 解码 PKM 容器里的 ETC1（Android 纹理）。GameData 优先用 PNG；
 * 只有用户丢进来的是 .pkm 才走这里，得到 RGBA 再交给 TerrainAtlas。
 * Mod 表是 ETC1 标准差分表，不要改数字。
 */
using System;
using System.IO;

namespace BtldMapEditor
{
    public static class TerrainPkm
    {
        static readonly int[,] Mod =
        {
            { 2, 8, -2, -8 },
            { 5, 17, -5, -17 },
            { 9, 29, -9, -29 },
            { 13, 42, -13, -42 },
            { 18, 60, -18, -60 },
            { 24, 80, -24, -80 },
            { 33, 106, -33, -106 },
            { 47, 183, -47, -183 },
        };

        /// <summary>识别 PKM 10 / PKM 2 头，宽高在偏移 12/14（大端），像素从第 16 字节起是 ETC1 块。</summary>
        public static bool TryLoadRgba(string path, out byte[] rgba, out int width, out int height)
        {
            rgba = null;
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 16)
                return false;
            bool pkm10 = data[0] == (byte)'P' && data[1] == (byte)'K' && data[2] == (byte)'M' && data[3] == (byte)' ' && data[4] == (byte)'1' && data[5] == (byte)'0';
            bool pkm2 = data[0] == (byte)'P' && data[1] == (byte)'K' && data[2] == (byte)'M' && data[3] == (byte)' ' && data[4] == (byte)'2';
            if (!pkm10 && !pkm2)
                return false;

            width = (data[12] << 8) | data[13];
            height = (data[14] << 8) | data[15];
            if (width <= 0 || height <= 0)
                return false;

            rgba = DecodeEtc1(data, 16, width, height);
            return rgba != null;
        }

        static byte[] DecodeEtc1(byte[] data, int offset, int width, int height)
        {
            var outRgba = new byte[width * height * 4];
            int bw = (width + 3) / 4;
            int bh = (height + 3) / 4;
            int off = offset;
            for (int by = 0; by < bh; by++)
            {
                int y0 = by * 4;
                for (int bx = 0; bx < bw; bx++)
                {
                    if (off + 8 > data.Length)
                        return outRgba;
                    DecodeBlock(data, off, outRgba, bx * 4, y0, width, height);
                    off += 8;
                }
            }
            return outRgba;
        }

        static void DecodeBlock(byte[] block, int bo, byte[] outRgba, int x0, int y0, int w, int h)
        {
            bool diff = (block[bo + 3] & 2) != 0;
            bool flip = (block[bo + 3] & 1) != 0;
            int t0 = (block[bo + 3] >> 5) & 7;
            int t1 = (block[bo + 3] >> 2) & 7;
            int r0, g0, b0, r1, g1, b1;

            if (diff)
            {
                r0 = block[bo] >> 3;
                int d = block[bo] & 7;
                g0 = block[bo + 1] >> 3;
                int e = block[bo + 1] & 7;
                b0 = block[bo + 2] >> 3;
                int f = block[bo + 2] & 7;
                if (d >= 4) d -= 8;
                if (e >= 4) e -= 8;
                if (f >= 4) f -= 8;
                r1 = r0 + d;
                g1 = g0 + e;
                b1 = b0 + f;
                r0 = Extend5(r0);
                g0 = Extend5(g0);
                b0 = Extend5(b0);
                r1 = Extend5(Clamp5(r1));
                g1 = Extend5(Clamp5(g1));
                b1 = Extend5(Clamp5(b1));
            }
            else
            {
                r0 = Extend4(block[bo] >> 4);
                r1 = Extend4(block[bo] & 15);
                g0 = Extend4(block[bo + 1] >> 4);
                g1 = Extend4(block[bo + 1] & 15);
                b0 = Extend4(block[bo + 2] >> 4);
                b1 = Extend4(block[bo + 2] & 15);
            }

            int[] br = { r0, r1 };
            int[] bg = { g0, g1 };
            int[] bb = { b0, b1 };
            int[] tables = { t0, t1 };

            for (int y = 0; y < 4; y++)
            {
                int py = y0 + y;
                if (py >= h) break;
                for (int x = 0; x < 4; x++)
                {
                    int px = x0 + x;
                    if (px >= w) continue;
                    int bit = x * 4 + y;
                    int lsb = (block[bo + 7 - bit / 8] >> (bit % 8)) & 1;
                    int msb = (block[bo + 5 - bit / 8] >> (bit % 8)) & 1;
                    int idx = (msb << 1) | lsb;
                    int sub = flip ? (y >> 1) : (x >> 1);
                    int m = Mod[tables[sub], idx];
                    int o = (py * w + px) * 4;
                    outRgba[o] = Clamp(br[sub] + m);
                    outRgba[o + 1] = Clamp(bg[sub] + m);
                    outRgba[o + 2] = Clamp(bb[sub] + m);
                    outRgba[o + 3] = 255;
                }
            }
        }

        static int Extend5(int v) => (v << 3) | (v >> 2);
        static int Extend4(int v) => (v << 4) | v;
        static int Clamp5(int v) => v < 0 ? 0 : (v > 31 ? 31 : v);
        static byte Clamp(int x) => (byte)(x < 0 ? 0 : (x > 255 ? 255 : x));
    }
}
