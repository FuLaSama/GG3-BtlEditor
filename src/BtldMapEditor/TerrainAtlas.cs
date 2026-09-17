/*
 * TerrainAtlas.cs
 *
 * 读游戏图集 XML + 对应 png/webp/pkm。切片矩形用 XML 原像素（不是编辑器缩略图栏）。
 * AtlasSlice.RefX/RefY 是锚点：绘制时格子中心减这个偏移才和游戏重叠。
 * PixelBuffer 是软件 blit 目标，给 Sprite 模式用。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;

namespace BtldMapEditor
{
    /// <summary>图集里一块：名字、源矩形、锚点、所属页。</summary>
    public sealed class AtlasSlice
    {
        public string Name;
        public int X, Y, W, H, RefX, RefY;
        public string Sheet;
    }

    public sealed class TerrainDef
    {
        public int TerrainId;
        public string Name;
        public int Type;
        public int Plants;
        public bool LayerTerrain = true;
        public bool LayerLowerDoodad;
        public bool LayerUpperDoodad;
        public List<string> Images = new List<string>();

        public int Layer
        {
            get
            {
                if (Type == 10) return 3;
                if (LayerLowerDoodad) return 0;
                if (LayerTerrain) return 1;
                if (LayerUpperDoodad) return 2;
                return 1;
            }
        }

        public string ImageAt(int variant)
        {
            int v = variant;
            int n = Images.Count;
            while (v > 0 && (v >= n || string.IsNullOrEmpty(Images[v])))
                v--;
            if (v >= 0 && v < n && !string.IsNullOrEmpty(Images[v]))
                return Images[v];
            return null;
        }
    }

    public sealed class PixelBuffer
    {
        public int Width;
        public int Height;
        public int[] Argb;

        public int this[int x, int y]
        {
            get => Argb[y * Width + x];
            set => Argb[y * Width + x] = value;
        }

        public static PixelBuffer FromBitmap(Bitmap bmp)
        {
            var buf = new PixelBuffer { Width = bmp.Width, Height = bmp.Height, Argb = new int[bmp.Width * bmp.Height] };
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == bmp.Width * 4)
                {
                    Marshal.Copy(data.Scan0, buf.Argb, 0, buf.Argb.Length);
                }
                else
                {
                    for (int y = 0; y < bmp.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, buf.Argb, y * bmp.Width, bmp.Width);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return buf;
        }

        public static PixelBuffer FromRgba(byte[] rgba, int w, int h)
        {
            var buf = new PixelBuffer { Width = w, Height = h, Argb = new int[w * h] };
            for (int i = 0; i < buf.Argb.Length; i++)
            {
                int o = i * 4;
                buf.Argb[i] = (rgba[o + 3] << 24) | (rgba[o] << 16) | (rgba[o + 1] << 8) | rgba[o + 2];
            }
            return buf;
        }

        public Bitmap ToBitmap()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            CopyToBitmap(bmp, 0, 0, Width, Height);
            return bmp;
        }

        public void CopyToBitmap(Bitmap bmp, int x, int y, int w, int h)
        {
            if (bmp == null || w <= 0 || h <= 0) return;
            x = Math.Max(0, x);
            y = Math.Max(0, y);
            w = Math.Min(w, Width - x);
            h = Math.Min(h, Height - y);
            w = Math.Min(w, bmp.Width - x);
            h = Math.Min(h, bmp.Height - y);
            if (w <= 0 || h <= 0) return;

            var rect = new Rectangle(x, y, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < h; row++)
                    Marshal.Copy(Argb, (y + row) * Width + x, data.Scan0 + row * data.Stride, w);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public PixelBuffer Crop(int x, int y, int w, int h)
        {
            w = Math.Max(1, Math.Min(w, Width - x));
            h = Math.Max(1, Math.Min(h, Height - y));
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            var dst = new PixelBuffer { Width = w, Height = h, Argb = new int[w * h] };
            for (int row = 0; row < h; row++)
                Array.Copy(Argb, (y + row) * Width + x, dst.Argb, row * w, w);
            return dst;
        }

        public int SampleWrapped(int x, int y)
        {
            int tw = Width;
            int th = Height;
            x %= tw;
            if (x < 0) x += tw;
            y %= th;
            if (y < 0) y += th;
            return Argb[y * tw + x];
        }
    }

    public sealed class TerrainAtlas
    {
        static readonly string[] TextureExts = { ".png", ".webp", ".pkm", ".dds" };

        readonly string _folder;
        readonly Dictionary<string, AtlasSlice> _slices = new Dictionary<string, AtlasSlice>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _sheetPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, PixelBuffer> _sheets = new Dictionary<string, PixelBuffer>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, PixelBuffer> _crops = new Dictionary<string, PixelBuffer>(StringComparer.OrdinalIgnoreCase);

        public TerrainAtlas(string folder)
        {
            _folder = folder;
        }

        public IReadOnlyDictionary<string, AtlasSlice> Slices => _slices;

        public void LoadXml(string xmlPath)
        {
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
                throw new FileNotFoundException("找不到图集 XML", xmlPath);

            string text = File.ReadAllText(xmlPath);
            var doc = new XmlDocument();
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<root", StringComparison.OrdinalIgnoreCase))
                doc.LoadXml(text);
            else
                doc.LoadXml("<root>" + text + "</root>");

            XmlNode tex = doc.SelectSingleNode("//Texture");
            string sheetName = tex?.Attributes?["name"]?.Value;
            if (string.IsNullOrEmpty(sheetName))
                sheetName = Path.GetFileNameWithoutExtension(xmlPath) + ".png";

            if (!_sheetPaths.ContainsKey(sheetName))
            {
                string real = ResolveTexture(_folder, sheetName) ?? ResolveTexture(Path.GetDirectoryName(xmlPath), sheetName);
                if (real == null)
                    throw new FileNotFoundException("找不到图集贴图: " + sheetName + " （" + xmlPath + "）");
                _sheetPaths[sheetName] = real;
            }

            foreach (XmlNode img in doc.SelectNodes("//Image"))
            {
                var sl = new AtlasSlice
                {
                    Name = img.Attributes?["name"]?.Value ?? "",
                    X = ParseInt(img.Attributes?["x"]?.Value),
                    Y = ParseInt(img.Attributes?["y"]?.Value),
                    W = ParseInt(img.Attributes?["w"]?.Value),
                    H = ParseInt(img.Attributes?["h"]?.Value),
                    RefX = (int)ParseFloat(img.Attributes?["refx"]?.Value),
                    RefY = (int)ParseFloat(img.Attributes?["refy"]?.Value),
                    Sheet = sheetName,
                };
                if (!string.IsNullOrEmpty(sl.Name))
                    _slices[sl.Name] = sl;
            }
        }

        public bool TryGet(string name, out PixelBuffer crop, out AtlasSlice slice)
        {
            crop = null;
            slice = null;
            if (string.IsNullOrEmpty(name) || !_slices.TryGetValue(name, out slice))
                return false;
            if (_crops.TryGetValue(name, out crop))
                return true;

            if (!_sheets.TryGetValue(slice.Sheet, out var sheet))
            {
                sheet = LoadSheet(_sheetPaths[slice.Sheet]);
                _sheets[slice.Sheet] = sheet;
            }
            crop = sheet.Crop(slice.X, slice.Y, slice.W, slice.H);
            _crops[name] = crop;
            return true;
        }

        public static Dictionary<int, TerrainDef> ParseDefMapTerrain(string path)
        {
            var outDict = new Dictionary<int, TerrainDef>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return outDict;

            var doc = new XmlDocument();
            doc.Load(path);
            foreach (XmlNode node in doc.SelectNodes("//terrain"))
            {
                if (node.Attributes?["terrain"] == null) continue;
                int tid = int.Parse(node.Attributes["terrain"].Value);
                var images = new List<string>();
                foreach (XmlNode tile in node.SelectNodes("tile"))
                    images.Add((tile.Attributes?["image"]?.Value ?? "").Trim());

                outDict[tid] = new TerrainDef
                {
                    TerrainId = tid,
                    Name = node.Attributes["name"]?.Value ?? "",
                    Type = ParseInt(node.Attributes["type"]?.Value),
                    Plants = ParseInt(node.Attributes["plants"]?.Value),
                    LayerTerrain = ParseBool(node.Attributes?["layerterrain"]?.Value, true),
                    LayerLowerDoodad = ParseBool(node.Attributes?["layerlowerdoodad"]?.Value, false),
                    LayerUpperDoodad = ParseBool(node.Attributes?["layerupperdoodad"]?.Value, false),
                    Images = images,
                };
            }
            return outDict;
        }

        public static string ResolveTexture(string folder, string xmlName)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return null;
            string stem = Path.GetFileNameWithoutExtension(xmlName);
            foreach (var ext in TextureExts)
            {
                string p = Path.Combine(folder, stem + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static PixelBuffer LoadSheet(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pkm")
            {
                if (!TerrainPkm.TryLoadRgba(path, out var rgba, out int w, out int h))
                    throw new InvalidDataException("无法解码 PKM: " + path);
                return PixelBuffer.FromRgba(rgba, w, h);
            }

            using (var fs = File.OpenRead(path))
            using (var bmp = new Bitmap(fs, false))
            {
                return PixelBuffer.FromBitmap(bmp);
            }
        }

        static int ParseInt(string s) => int.TryParse(s, out int v) ? v : 0;
        static float ParseFloat(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;

        static bool ParseBool(string raw, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            raw = raw.Trim().ToLowerInvariant();
            return raw == "true" || raw == "1";
        }
    }
}
