/*
 * EditorPaletteCatalog.cs
 *
 * 编辑器自己的贴图栏配置：读 GameData/EditorPalette/catalog.json。
 * 和游戏 def_mapterrain.xml 是两套东西——栏里是「好拖的分类缩略图」，
 * 真正画到地图上的大图仍走 TerrainAtlas。
 *
 * PaletteTarget：气候 / 主地形 / 副地形 / 装饰，对应画布落笔改哪一层。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BtldMapEditor
{
    /// <summary>拖到地图上时要改的层。Main 改 tiles 低字节，Decor 改 A1/A2/A3。</summary>
    public enum PaletteTarget
    {
        Climate,
        Main,
        Secondary,
        Decor
    }

    /// <summary>贴图栏一项：缩略图文件、落到格子上要改的地形码/建筑 id。</summary>
    public sealed class PaletteItem
    {
        public string Kind { get; init; }
        public string File { get; init; }
        public string Label { get; init; }
        public bool Snow { get; init; }
        public int? T { get; init; }
        public bool? Sea { get; init; }
        public int? TerrainId { get; init; }
        public int Variant { get; init; }
        public int Dx { get; init; }
        public int Dy { get; init; }
        public string FullPath { get; init; }
        public Image Thumb { get; set; }

        public string GroupName
        {
            get
            {
                if (TerrainId.HasValue)
                {
                    string n = GameSettings.GetTerrainName(TerrainId.Value);
                    if (!string.IsNullOrEmpty(n) && n != "无" && !n.StartsWith("未知", StringComparison.Ordinal))
                        return n;
                }
                string label = Label ?? "";
                int cut = label.IndexOf(" 变种", StringComparison.Ordinal);
                return cut > 0 ? label.Substring(0, cut) : label;
            }
        }

        public string TooltipText
        {
            get
            {
                if (Sea == true)
                    return Label ?? "开阔海";
                string label = Label ?? "";
                int cut = label.LastIndexOf("变种", StringComparison.Ordinal);
                if (cut >= 0)
                    return label.Substring(cut);
                return "变种" + Variant;
            }
        }
    }

    public sealed class PaletteDrag
    {
        public PaletteItem Item { get; init; }
        public PaletteTarget Target { get; init; }
    }

    public static class EditorPaletteCatalog
    {
        static readonly object Gate = new object();
        static bool _loaded;
        static string _root;
        static readonly List<PaletteItem> _climate = new List<PaletteItem>();
        static readonly List<PaletteItem> _terrain = new List<PaletteItem>();

        public static string Root => _root;
        public static IReadOnlyList<PaletteItem> Climate => Ensure().ClimateList;
        public static IReadOnlyList<PaletteItem> Terrain => Ensure().TerrainList;

        static (List<PaletteItem> ClimateList, List<PaletteItem> TerrainList) Ensure()
        {
            if (_loaded) return (_climate, _terrain);
            lock (Gate)
            {
                if (_loaded) return (_climate, _terrain);
                _root = GameSettings.FindDirectory("EditorPalette");
                if (string.IsNullOrEmpty(_root))
                {
                    _loaded = true;
                    return (_climate, _terrain);
                }

                string catalogPath = Path.Combine(_root, "catalog.json");
                if (!File.Exists(catalogPath))
                {
                    _loaded = true;
                    return (_climate, _terrain);
                }

                var dto = JsonSerializer.Deserialize<CatalogDto>(File.ReadAllText(catalogPath));
                if (dto?.Items == null)
                {
                    _loaded = true;
                    return (_climate, _terrain);
                }

                foreach (var item in dto.Items)
                {
                    if (item == null || string.IsNullOrEmpty(item.File)) continue;
                    string full = Path.Combine(_root, item.File.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full)) continue;

                    var parsed = new PaletteItem
                    {
                        Kind = item.Kind,
                        File = item.File,
                        Label = item.Label,
                        Snow = item.Snow,
                        T = item.Place?.T,
                        Sea = item.Place?.Sea,
                        TerrainId = item.Place?.TerrainId,
                        Variant = item.Place?.Variant ?? 0,
                        Dx = item.Place?.Dx ?? 0,
                        Dy = item.Place?.Dy ?? 0,
                        FullPath = full,
                    };

                    if (string.Equals(item.Kind, "climate", StringComparison.OrdinalIgnoreCase))
                        _climate.Add(parsed);
                    else if (string.Equals(item.Kind, "terrain", StringComparison.OrdinalIgnoreCase) && !item.Snow)
                        _terrain.Add(parsed);
                }

                ExpandClimateVariants();
                _loaded = true;
                return (_climate, _terrain);
            }
        }

        const int ClimateMaskCount = 12;
        public const int ClimateVariantCount = ClimateMaskCount;

        /// <summary>从 catalog 中为指定地形 ID 随机取一个已有变种索引。</summary>
        public static int PickRandomTerrainVariant(int terrainId, Random rng)
        {
            Ensure();
            int picked = 0;
            int n = 0;
            foreach (var it in _terrain)
            {
                if (it.TerrainId != terrainId) continue;
                n++;
                if (rng.Next(n) == 0)
                    picked = it.Variant;
            }
            return picked;
        }

        public static int PickRandomClimateVariant(Random rng)
        {
            return rng.Next(ClimateVariantCount);
        }
        static TerrainAtlas _maskAtlas;
        static bool _maskTried;

        static void ExpandClimateVariants()
        {
            var sea = new List<PaletteItem>();
            var land = new List<PaletteItem>(_climate.Count * ClimateMaskCount);
            foreach (var src in _climate)
            {
                if (src.Sea == true || !src.T.HasValue)
                {
                    sea.Add(src);
                    continue;
                }
                for (int v = 0; v < ClimateMaskCount; v++)
                {
                    land.Add(new PaletteItem
                    {
                        Kind = src.Kind,
                        File = src.File,
                        Label = src.Label + " 变种" + v,
                        Snow = src.Snow,
                        T = src.T,
                        Sea = false,
                        Variant = v,
                        Dx = src.Dx,
                        Dy = src.Dy,
                        FullPath = src.FullPath,
                    });
                }
            }
            _climate.Clear();
            _climate.AddRange(sea);
            _climate.AddRange(land);
        }

        static TerrainAtlas Masks()
        {
            if (_maskTried) return _maskAtlas;
            _maskTried = true;
            string dir = GameSettings.FindDirectory("Terrain");
            if (string.IsNullOrEmpty(dir)) return null;
            string xml = Path.Combine(dir, "mask.xml");
            if (!File.Exists(xml)) return null;
            try
            {
                _maskAtlas = new TerrainAtlas(dir);
                _maskAtlas.LoadXml(xml);
            }
            catch
            {
                _maskAtlas = null;
            }
            return _maskAtlas;
        }

        static bool IsLandClimate(PaletteItem item)
        {
            return item != null
                && string.Equals(item.Kind, "climate", StringComparison.OrdinalIgnoreCase)
                && item.Sea != true
                && item.T.HasValue;
        }

        public static Image LoadThumb(PaletteItem item, int size)
        {
            if (item == null) return null;
            if (item.Thumb != null) return item.Thumb;
            if (string.IsNullOrEmpty(item.FullPath) || !File.Exists(item.FullPath))
                return null;
            try
            {
                item.Thumb = IsLandClimate(item)
                    ? MakeClimateVariantThumb(item, size)
                    : MakeSimpleThumb(item.FullPath, size);
                return item.Thumb;
            }
            catch
            {
                return null;
            }
        }

        static Bitmap MakeSimpleThumb(string path, int size)
        {
            using var fs = File.OpenRead(path);
            using var src = Image.FromStream(fs, false, false);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(241, 245, 249));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            float scale = Math.Min((size - 6f) / src.Width, (size - 6f) / src.Height);
            float w = src.Width * scale;
            float h = src.Height * scale;
            g.DrawImage(src, (size - w) / 2f, (size - h) / 2f, w, h);
            return bmp;
        }

        static Bitmap MakeClimateVariantThumb(PaletteItem item, int size)
        {
            using var fs = File.OpenRead(item.FullPath);
            using var hexBmp = new Bitmap(fs, false);
            var hex = PixelBuffer.FromBitmap(hexBmp);

            int hexW = hex.Width;
            int hexH = hex.Height;
            int ox = hexW / 2;
            int oy = hexH / 2;
            int margin = Math.Max(hexW, hexH) * 2 / 3;
            int cw = hexW + margin * 2;
            int ch = hexH + margin * 2;
            var canvas = new PixelBuffer { Width = cw, Height = ch, Argb = new int[cw * ch] };
            int neighbor = unchecked((int)0xFF64748B);
            for (int i = 0; i < canvas.Argb.Length; i++)
                canvas.Argb[i] = neighbor;

            for (int y = 0; y < hexH; y++)
            {
                int srow = y * hexW;
                int drow = (y + margin) * cw + margin;
                for (int x = 0; x < hexW; x++)
                {
                    int sp = hex.Argb[srow + x];
                    int sa = (sp >> 24) & 255;
                    if (sa < 8) continue;
                    canvas.Argb[drow + x] = Blend(canvas.Argb[drow + x], sp, sa);
                }
            }

            int avg = hex.Argb[Math.Min(oy, hexH - 1) * hexW + Math.Min(ox, hexW - 1)];
            var atlas = Masks();
            string name = "mask" + (Math.Min(Math.Max(item.Variant, 0), ClimateMaskCount - 1) + 1).ToString("00") + ".png";
            if (atlas != null && atlas.TryGet(name, out var crop, out var sl))
            {
                int mw = crop.Width;
                int mh = crop.Height;
                for (int iy = 0; iy < mh; iy++)
                {
                    int mrow = iy * mw;
                    for (int ix = 0; ix < mw; ix++)
                    {
                        int a = (crop.Argb[mrow + ix] >> 16) & 255;
                        if (a < 10) continue;
                        int dx = margin + ix - sl.RefX + ox;
                        int dy = margin + iy - sl.RefY + oy;
                        if ((uint)dx >= (uint)cw || (uint)dy >= (uint)ch) continue;
                        int px = ix - sl.RefX + ox;
                        int py = iy - sl.RefY + oy;
                        int src = avg;
                        if ((uint)px < (uint)hexW && (uint)py < (uint)hexH)
                        {
                            int hp = hex.Argb[py * hexW + px];
                            if (((hp >> 24) & 255) > 16)
                                src = hp;
                        }
                        int di = dy * cw + dx;
                        canvas.Argb[di] = Blend(canvas.Argb[di], src, a);
                    }
                }
            }

            using var canvasBmp = canvas.ToBitmap();
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(100, 116, 139));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                float sc = Math.Min((size - 4f) / cw, (size - 4f) / ch);
                float dw = cw * sc;
                float dh = ch * sc;
                g.DrawImage(canvasBmp, (size - dw) / 2f, (size - dh) / 2f, dw, dh);
                DrawVariantIndex(g, item.Variant, size);
            }
            return bmp;
        }

        static void DrawVariantIndex(Graphics g, int variant, int size)
        {
            string s = variant.ToString();
            using var font = new Font("Segoe UI", Math.Max(7f, size / 8f), FontStyle.Bold);
            var sz = g.MeasureString(s, font);
            float x = 2f;
            float y = size - sz.Height - 1f;
            using var bg = new SolidBrush(Color.FromArgb(185, 15, 23, 42));
            g.FillRectangle(bg, x, y, sz.Width + 1f, sz.Height - 1f);
            g.DrawString(s, font, Brushes.White, x, y);
        }

        static int Blend(int dst, int src, int a)
        {
            if (a >= 255) return src | unchecked((int)0xFF000000);
            if (a <= 0) return dst;
            int ia = 255 - a;
            int dr = (((dst >> 16) & 255) * ia + ((src >> 16) & 255) * a) / 255;
            int dg = (((dst >> 8) & 255) * ia + ((src >> 8) & 255) * a) / 255;
            int db = ((dst & 255) * ia + (src & 255) * a) / 255;
            return unchecked((int)0xFF000000) | (dr << 16) | (dg << 8) | db;
        }

        sealed class CatalogDto
        {
            [JsonPropertyName("items")]
            public List<ItemDto> Items { get; set; }
        }

        sealed class ItemDto
        {
            [JsonPropertyName("kind")] public string Kind { get; set; }
            [JsonPropertyName("file")] public string File { get; set; }
            [JsonPropertyName("label")] public string Label { get; set; }
            [JsonPropertyName("snow")] public bool Snow { get; set; }
            [JsonPropertyName("place")] public PlaceDto Place { get; set; }
        }

        sealed class PlaceDto
        {
            [JsonPropertyName("t")] public int? T { get; set; }
            [JsonPropertyName("sea")] public bool? Sea { get; set; }
            [JsonPropertyName("terrain_id")] public int? TerrainId { get; set; }
            [JsonPropertyName("variant")] public int? Variant { get; set; }
            [JsonPropertyName("dx")] public int Dx { get; set; }
            [JsonPropertyName("dy")] public int Dy { get; set; }
        }
    }
}
