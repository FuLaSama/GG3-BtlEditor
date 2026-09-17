/*
 * TerrainSpriteRenderer.cs  （BtldMapEditor）
 *
 * 把 DecodedTerrainMap 画成一张大位图（Sprite 模式）。
 * 不读 BtlFront：输入已经是格子几何。图集目录由 GameSettings 找 GameData。
 *
 * 层序：底图 → 气候 mask → 海 → 海岸线 → 地表装饰 A1/A2/A3 → 工事 → 建筑。
 * 脏区重绘只 blit 受影响的六角邻域，避免每次拖地图都全图重画。
 *
 * 注释里旧称 CellItem 的地方，现在对应 TerrainCell。
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BtldMapEditor
{
    public sealed class TerrainSpriteRenderer : IDisposable
    {
        static readonly int[] MapPtTints =
        {
            160, 163, 118,
            130, 150, 95,
            200, 177, 151,
            183, 187, 181,
            168, 158, 120,
        };

        static readonly Dictionary<int, int> DefaultBuildingTypes = new Dictionary<int, int>
        {
            { 101, 1 }, { 102, 1 }, { 103, 1 }, { 104, 2 }, { 107, 3 },
            { 109, 4 }, { 112, 5 }, { 113, 5 }, { 114, 6 }, { 115, 5 }, { 116, 5 },
        };

        struct FortGun
        {
            public string Normal, Snow;
            public float Scale;
            public int Ox, Oy;
            public FortGun(string n, string s, float scale, int ox, int oy)
            {
                Normal = n; Snow = s; Scale = scale; Ox = ox; Oy = oy;
            }
        }

        static readonly Dictionary<int, FortGun> FortGuns = new Dictionary<int, FortGun>
        {
            { 3, new FortGun("79.png", "88.png", 0.8f, 0, 6) },
            { 4, new FortGun("91.png", "92.png", 0.8f, 0, 2) },
            { 5, new FortGun("89.png", "90.png", 0.8f, 0, 6) },
        };

        static readonly Dictionary<int, int> FortYLift = new Dictionary<int, int>
        {
            { 1, 20 }, { 3, 27 }, { 4, 27 }, { 5, 27 },
        };

        readonly TerrainAtlas _doodads;
        readonly TerrainAtlas _masks;
        readonly TerrainAtlas _coast;
        readonly TerrainAtlas _coastmask;
        TerrainAtlas _buildingsAtlas;
        TerrainAtlas _fortsAtlas;
        TerrainAtlas _gunsAtlas;
        readonly Dictionary<int, TerrainDef> _defs;
        readonly Dictionary<int, int> _buildingTypes;
        readonly string _entityDir;
        readonly PixelBuffer[] _mapPt = new PixelBuffer[5];
        PixelBuffer _mapSea;
        byte[] _hexMask;
        int _hexMaskW, _hexMaskH, _hexMaskOx, _hexMaskOy;
        bool _disposed;
        PixelBuffer _buf;
        byte[] _occ;
        Bitmap _bitmap;

        // 装饰/建筑会画出格子外，脏区四周要留足余量。
        const int SpriteInfluence = 280;

        public string AtlasDir { get; }
        public string LastError { get; private set; }
        public Bitmap MapBitmap => _bitmap;

        TerrainSpriteRenderer(string atlasDir, string defXml, string entityDir)
        {
            AtlasDir = atlasDir;
            _entityDir = entityDir;
            _defs = TerrainAtlas.ParseDefMapTerrain(defXml);
            _buildingTypes = new Dictionary<int, int>(DefaultBuildingTypes);
            foreach (var kv in GameSettings.Buildings)
            {
                if (kv.Value != null && kv.Value.Type > 0)
                    _buildingTypes[kv.Key] = kv.Value.Type;
            }

            _doodads = new TerrainAtlas(atlasDir);
            _doodads.LoadXml(Path.Combine(atlasDir, "terrain.xml"));
            _doodads.LoadXml(Path.Combine(atlasDir, "mountain.xml"));
            _doodads.LoadXml(Path.Combine(atlasDir, "rivers.xml"));

            _masks = new TerrainAtlas(atlasDir);
            _masks.LoadXml(Path.Combine(atlasDir, "mask.xml"));

            _coast = new TerrainAtlas(atlasDir);
            _coast.LoadXml(Path.Combine(atlasDir, "coast.xml"));
            _coastmask = new TerrainAtlas(atlasDir);
            _coastmask.LoadXml(Path.Combine(atlasDir, "coastmask.xml"));

            LoadBaseTiles(atlasDir);
            BuildHexMask();
        }

        public static bool TryCreate(out TerrainSpriteRenderer renderer, out string error)
        {
            renderer = null;
            error = null;
            try
            {
                string atlasDir = GameSettings.FindDirectory("Terrain");
                string entityDir = GameSettings.FindDirectory("Buildings")
                    ?? GameSettings.FindDirectory("Entities");
                string defXml = GameSettings.FindFile("def_mapterrain.xml");

                if (string.IsNullOrEmpty(atlasDir) || !File.Exists(Path.Combine(atlasDir, "terrain.xml")))
                {
                    error = "未找到 GameData/Terrain（需要 terrain.xml 与对应贴图）。";
                    return false;
                }
                if (string.IsNullOrEmpty(defXml))
                {
                    error = "未找到 def_mapterrain.xml。";
                    return false;
                }

                renderer = new TerrainSpriteRenderer(atlasDir, defXml, entityDir);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                renderer?.Dispose();
                renderer = null;
                return false;
            }
        }

        /// <summary>全图画完后返回内部位图（调用方不要 Dispose，由本类管）。</summary>
        public Bitmap Render(DecodedTerrainMap decoded, bool showBuildings, bool showForts)
        {
            RenderFull(decoded, showBuildings, showForts);
            return _bitmap;
        }

        /// <summary>清空缓冲后画整张图。</summary>
        public void RenderFull(DecodedTerrainMap decoded, bool showBuildings, bool showForts)
        {
            int cw = TerrainGeometry.CanvasWidth(decoded.Width);
            int ch = TerrainGeometry.CanvasHeight(decoded.Height);
            EnsureBuffer(cw, ch);
            Array.Clear(_buf.Argb, 0, _buf.Argb.Length);
            Array.Clear(_occ, 0, _occ.Length);
            var clip = new PixelClip(0, 0, cw, ch);
            DrawScene(decoded, showBuildings, showForts, clip, includeAll: true);
            _buf.CopyToBitmap(_bitmap, 0, 0, cw, ch);
        }

        /// <summary>只重画 dirtyCells 的邻域，拷回位图对应矩形。</summary>
        public void RenderDirty(DecodedTerrainMap decoded, bool showBuildings, bool showForts, IReadOnlyList<int> dirtyCells)
        {
            int cw = TerrainGeometry.CanvasWidth(decoded.Width);
            int ch = TerrainGeometry.CanvasHeight(decoded.Height);
            if (_buf == null || _buf.Width != cw || _buf.Height != ch || _bitmap == null)
            {
                RenderFull(decoded, showBuildings, showForts);
                return;
            }

            if (dirtyCells == null || dirtyCells.Count == 0)
                return;

            var dirty = new HashSet<int>(dirtyCells);
            int w = decoded.Width, h = decoded.Height;
            var extra = new List<int>();
            foreach (int i in dirty)
            {
                for (int d = 0; d < 6; d++)
                {
                    int n = TerrainGeometry.NeighborIndex(w, h, i, d);
                    if (n >= 0) extra.Add(n);
                }
            }
            foreach (int n in extra)
                dirty.Add(n);

            int pad = SpriteInfluence * TerrainGeometry.Scale;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
            foreach (int i in dirty)
            {
                if ((uint)i >= (uint)decoded.Cells.Count) continue;
                ToCanvas(decoded.Cells[i], out int cx, out int cy);
                x0 = Math.Min(x0, cx - pad);
                y0 = Math.Min(y0, cy - pad);
                x1 = Math.Max(x1, cx + pad);
                y1 = Math.Max(y1, cy + pad);
            }
            if (x0 >= x1)
            {
                RenderFull(decoded, showBuildings, showForts);
                return;
            }

            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            x1 = Math.Min(cw, x1);
            y1 = Math.Min(ch, y1);
            long area = (long)(x1 - x0) * (y1 - y0);
            if (area <= 0 || area * 2 > (long)cw * ch)
            {
                RenderFull(decoded, showBuildings, showForts);
                return;
            }

            var clip = new PixelClip(x0, y0, x1, y1);
            ClearClip(clip);
            DrawScene(decoded, showBuildings, showForts, clip, includeAll: false);
            _buf.CopyToBitmap(_bitmap, x0, y0, x1 - x0, y1 - y0);
        }

        void EnsureBuffer(int cw, int ch)
        {
            if (_buf != null && _buf.Width == cw && _buf.Height == ch && _bitmap != null)
                return;
            _bitmap?.Dispose();
            _buf = new PixelBuffer { Width = cw, Height = ch, Argb = new int[cw * ch] };
            _occ = new byte[cw * ch];
            _bitmap = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        }

        void ClearClip(PixelClip clip)
        {
            int cw = _buf.Width;
            for (int y = clip.Y0; y < clip.Y1; y++)
            {
                int row = y * cw;
                Array.Clear(_buf.Argb, row + clip.X0, clip.X1 - clip.X0);
                Array.Clear(_occ, row + clip.X0, clip.X1 - clip.X0);
            }
        }

        void DrawScene(DecodedTerrainMap decoded, bool showBuildings, bool showForts, PixelClip clip, bool includeAll)
        {
            int influence = SpriteInfluence * TerrainGeometry.Scale;
            var coastal = new bool[decoded.Cells.Count];
            for (int i = 0; i < decoded.Cells.Count; i++)
            {
                if (!includeAll && !Intersects(decoded.Cells[i], clip, influence))
                    continue;
                coastal[i] = decoded.Cells[i].IsCoast(decoded.Cells, decoded.Width, decoded.Height);
            }

            DrawBase(_buf, _occ, decoded, coastal, clip, includeAll, influence);
            DrawCoast(_buf, decoded, coastal, clip, includeAll, influence);
            FillUnoccupied(_buf, _occ, ColorToArgb(32, 34, 30, 255), clip);
            DrawDoodads(_buf, decoded, clip, includeAll, influence);
            EnsureEntityAtlases(showBuildings, showForts);

            var overlays = new List<OverlayJob>();
            if (showForts) CollectFortJobs(decoded, overlays, clip, includeAll, influence);
            if (showBuildings) CollectBuildingJobs(decoded, overlays, clip, includeAll, influence);
            overlays.Sort((a, b) =>
            {
                int c = a.Cy.CompareTo(b.Cy);
                if (c != 0) return c;
                return a.Layer.CompareTo(b.Layer);
            });
            foreach (var job in overlays)
                Paste(_buf, job.Sprite, job.X, job.Y, clip);
        }

        static bool Intersects(TerrainCell cell, PixelClip clip, int radius)
        {
            ToCanvas(cell, out int cx, out int cy);
            return cx + radius >= clip.X0 && cx - radius < clip.X1
                && cy + radius >= clip.Y0 && cy - radius < clip.Y1;
        }

        readonly struct PixelClip
        {
            public readonly int X0, Y0, X1, Y1;
            public PixelClip(int x0, int y0, int x1, int y1)
            {
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
            }
        }

        void LoadBaseTiles(string atlasDir)
        {
            for (int i = 1; i <= 5; i++)
            {
                string path = ResolveBaseTile(atlasDir, "map_pt" + i);
                if (path == null)
                    throw new FileNotFoundException("缺少底图 map_pt" + i);
                var tex = TerrainAtlas.LoadSheet(path);
                TintIfGray(tex, i - 1);
                _mapPt[i - 1] = tex;
            }
            string seaPath = ResolveBaseTile(atlasDir, "map_sea");
            if (seaPath == null)
                throw new FileNotFoundException("缺少底图 map_sea");
            _mapSea = TerrainAtlas.LoadSheet(seaPath);
        }

        static string ResolveBaseTile(string atlasDir, string stem)
        {
            return TerrainAtlas.ResolveTexture(atlasDir, stem + "_hd.png")
                ?? TerrainAtlas.ResolveTexture(atlasDir, stem + ".png");
        }

        static void TintIfGray(PixelBuffer tex, int tintIndex)
        {
            long diff = 0;
            long lumSum = 0;
            int n = tex.Argb.Length;
            for (int i = 0; i < n; i++)
            {
                int p = tex.Argb[i];
                int r = (p >> 16) & 255;
                int g = (p >> 8) & 255;
                diff += Math.Abs(r - g);
                lumSum += r;
            }
            if (n == 0 || diff / (double)n >= 1.0)
                return;

            double mean = Math.Max(1.0, lumSum / (double)n);
            int tr = MapPtTints[tintIndex * 3];
            int tg = MapPtTints[tintIndex * 3 + 1];
            int tb = MapPtTints[tintIndex * 3 + 2];
            for (int i = 0; i < n; i++)
            {
                int p = tex.Argb[i];
                int lum = (p >> 16) & 255;
                int nr = Clamp((int)(lum * (tr / mean)));
                int ng = Clamp((int)(lum * (tg / mean)));
                int nb = Clamp((int)(lum * (tb / mean)));
                tex.Argb[i] = (255 << 24) | (nr << 16) | (ng << 8) | nb;
            }
        }

        void BuildHexMask()
        {
            int scale = TerrainGeometry.Scale;
            var verts = TerrainGeometry.HexFill;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            var pts = new Point[6];
            for (int i = 0; i < 6; i++)
            {
                int x = verts[i * 2] * scale;
                int y = verts[i * 2 + 1] * scale;
                pts[i] = new Point(x, y);
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
            _hexMaskOx = minX;
            _hexMaskOy = minY;
            _hexMaskW = maxX - minX + 1;
            _hexMaskH = maxY - minY + 1;
            _hexMask = new byte[_hexMaskW * _hexMaskH];

            using (var bmp = new Bitmap(_hexMaskW, _hexMaskH))
            using (var g = Graphics.FromImage(bmp))
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var shifted = new Point[6];
                for (int i = 0; i < 6; i++)
                    shifted[i] = new Point(pts[i].X - minX, pts[i].Y - minY);
                path.AddPolygon(shifted);
                g.Clear(Color.Black);
                g.FillPath(Brushes.White, path);
                var pixels = PixelBuffer.FromBitmap(bmp);
                for (int i = 0; i < pixels.Argb.Length; i++)
                    _hexMask[i] = (byte)((pixels.Argb[i] >> 16) & 255);
            }
        }

        void DrawBase(PixelBuffer buf, byte[] occ, DecodedTerrainMap decoded, bool[] coastal, PixelClip clip, bool includeAll, int influence)
        {
            var cells = decoded.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                if (!includeAll && !Intersects(cells[i], clip, influence)) continue;
                if (cells[i].Sea && !coastal[i]) continue;
                var tex = _mapPt[Math.Min(cells[i].T, _mapPt.Length - 1)];
                ToCanvas(cells[i], out int cx, out int cy);
                FillHex(buf, occ, tex, cx, cy, clip);
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (!includeAll && !Intersects(cells[i], clip, influence)) continue;
                if (cells[i].Sea && !coastal[i]) continue;
                if (cells[i].T == decoded.PlayableFlag) continue;
                if (!cells[i].ClimateDiffers(cells, decoded.Width, decoded.Height)) continue;
                var tex = _mapPt[Math.Min(cells[i].T, _mapPt.Length - 1)];
                StampMask(buf, cells[i], tex, clip);
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (!includeAll && !Intersects(cells[i], clip, influence)) continue;
                if (!(cells[i].Sea && !coastal[i])) continue;
                ToCanvas(cells[i], out int cx, out int cy);
                FillHex(buf, occ, _mapSea, cx, cy, clip);
            }
        }

        void FillHex(PixelBuffer buf, byte[] occ, PixelBuffer tex, int cx, int cy, PixelClip clip)
        {
            int x0 = cx + _hexMaskOx;
            int y0 = cy + _hexMaskOy;
            int pad = TerrainGeometry.Pad;
            int cw = buf.Width;
            int iy0 = Math.Max(0, clip.Y0 - y0);
            int iy1 = Math.Min(_hexMaskH, clip.Y1 - y0);
            int ix0 = Math.Max(0, clip.X0 - x0);
            int ix1 = Math.Min(_hexMaskW, clip.X1 - x0);
            if (iy0 >= iy1 || ix0 >= ix1) return;

            int tw = tex.Width, th = tex.Height;
            var texPx = tex.Argb;
            var dst = buf.Argb;
            for (int iy = iy0; iy < iy1; iy++)
            {
                int dy = y0 + iy;
                int row = dy * cw;
                int mrow = iy * _hexMaskW;
                int sy = dy - pad;
                sy %= th;
                if (sy < 0) sy += th;
                int texRow = sy * tw;
                for (int ix = ix0; ix < ix1; ix++)
                {
                    if (_hexMask[mrow + ix] == 0) continue;
                    int dx = x0 + ix;
                    int sx = dx - pad;
                    sx %= tw;
                    if (sx < 0) sx += tw;
                    int dest = row + dx;
                    dst[dest] = texPx[texRow + sx] | unchecked((int)0xFF000000);
                    occ[dest] = 255;
                }
            }
        }

        void StampMask(PixelBuffer buf, TerrainCell cell, PixelBuffer tex, PixelClip clip)
        {
            string name = "mask" + (Math.Min(Math.Max(cell.V, 0), 11) + 1).ToString("00") + ".png";
            if (!_masks.TryGet(name, out var crop, out var sl))
                return;
            ToCanvas(cell, out int cx, out int cy);
            int x = cx - sl.RefX;
            int y = cy - sl.RefY;
            BlendByRed(buf, crop, tex, x, y, clip);
        }

        void BlendByRed(PixelBuffer buf, PixelBuffer mask, PixelBuffer tex, int x, int y, PixelClip clip)
        {
            int pad = TerrainGeometry.Pad;
            int cw = buf.Width;
            int iy0 = Math.Max(0, clip.Y0 - y);
            int iy1 = Math.Min(mask.Height, clip.Y1 - y);
            int ix0 = Math.Max(0, clip.X0 - x);
            int ix1 = Math.Min(mask.Width, clip.X1 - x);
            if (iy0 >= iy1 || ix0 >= ix1) return;

            int tw = tex.Width, th = tex.Height;
            for (int iy = iy0; iy < iy1; iy++)
            {
                int dy = y + iy;
                int row = dy * cw;
                int mrow = iy * mask.Width;
                int sy = dy - pad;
                sy %= th;
                if (sy < 0) sy += th;
                int texRow = sy * tw;
                for (int ix = ix0; ix < ix1; ix++)
                {
                    int a = (mask.Argb[mrow + ix] >> 16) & 255;
                    if (a == 0) continue;
                    int dx = x + ix;
                    int sx = dx - pad;
                    sx %= tw;
                    if (sx < 0) sx += tw;
                    int src = tex.Argb[texRow + sx];
                    int destI = row + dx;
                    int dst = buf.Argb[destI];
                    if (a == 255)
                    {
                        buf.Argb[destI] = src | unchecked((int)0xFF000000);
                        continue;
                    }
                    int ia = 255 - a;
                    int dr = ((dst >> 16) & 255) * ia + ((src >> 16) & 255) * a;
                    int dg = ((dst >> 8) & 255) * ia + ((src >> 8) & 255) * a;
                    int db = (dst & 255) * ia + (src & 255) * a;
                    buf.Argb[destI] = unchecked((int)0xFF000000) | ((dr / 255) << 16) | ((dg / 255) << 8) | (db / 255);
                }
            }
        }

        void DrawCoast(PixelBuffer buf, DecodedTerrainMap decoded, bool[] coastal, PixelClip clip, bool includeAll, int influence)
        {
            int cw = buf.Width;
            int pad = TerrainGeometry.Pad;
            for (int i = 0; i < decoded.Cells.Count; i++)
            {
                if (!coastal[i]) continue;
                if (!includeAll && !Intersects(decoded.Cells[i], clip, influence)) continue;
                var cell = decoded.Cells[i];
                int bits = cell.CoastMask(decoded.Cells, decoded.Width, decoded.Height);
                if (bits >= 63) continue;
                int n = bits + 1;
                bool odd = cell.A2.HasValue && (cell.A2.Value.Variant & 1) != 0;
                string prefix = cell.CoastB ? "B" : "A";
                string used = null;
                PixelBuffer crop = null;
                AtlasSlice sl = null;
                if (odd && _coast.TryGet(prefix + n.ToString("000") + "b.png", out crop, out sl))
                    used = prefix + n.ToString("000") + "b.png";
                else if (_coast.TryGet(prefix + n.ToString("000") + ".png", out crop, out sl))
                    used = prefix + n.ToString("000") + ".png";
                if (crop == null || used == null) continue;
                if (!_coastmask.TryGet(used, out var mimg, out _))
                    continue;

                ToCanvas(cell, out int cx, out int cy);
                int x = cx - crop.Width / 2;
                int y = cy - crop.Height / 2;
                int w = crop.Width, h = crop.Height;
                int iy0 = Math.Max(0, clip.Y0 - y);
                int iy1 = Math.Min(h, clip.Y1 - y);
                int ix0 = Math.Max(0, clip.X0 - x);
                int ix1 = Math.Min(w, clip.X1 - x);
                if (iy0 >= iy1 || ix0 >= ix1) continue;

                int seaW = _mapSea.Width, seaH = _mapSea.Height;
                for (int iy = iy0; iy < iy1; iy++)
                {
                    int dy = y + iy;
                    int row = dy * cw;
                    int crow = iy * w;
                    int hx = dy - (cy + _hexMaskOy);
                    int sy = dy - pad;
                    sy %= seaH;
                    if (sy < 0) sy += seaH;
                    int seaRow = sy * seaW;
                    for (int ix = ix0; ix < ix1; ix++)
                    {
                        int dx = x + ix;
                        int mx = dx - (cx + _hexMaskOx);
                        int hexA = 0;
                        if ((uint)mx < (uint)_hexMaskW && (uint)hx < (uint)_hexMaskH)
                            hexA = _hexMask[hx * _hexMaskW + mx];
                        if (hexA == 0) continue;

                        int mp = mimg.Argb[crow + ix];
                        int mr = (mp >> 16) & 255;
                        int mg = (mp >> 8) & 255;
                        int a = mr * hexA / 255;
                        if (a == 0) continue;

                        int sx = dx - pad;
                        sx %= seaW;
                        if (sx < 0) sx += seaW;
                        int sea = _mapSea.Argb[seaRow + sx];
                        int coast = crop.Argb[crow + ix];
                        int rr = Clamp((((sea >> 16) & 255) * mg + ((coast >> 16) & 255) * 255) / 255);
                        int gg = Clamp((((sea >> 8) & 255) * mg + ((coast >> 8) & 255) * 255) / 255);
                        int bb = Clamp(((sea & 255) * mg + (coast & 255) * 255) / 255);

                        int destI = row + dx;
                        int dst = buf.Argb[destI];
                        int ia = 255 - a;
                        int dr = ((dst >> 16) & 255) * ia + rr * a;
                        int dg = ((dst >> 8) & 255) * ia + gg * a;
                        int db = (dst & 255) * ia + bb * a;
                        buf.Argb[destI] = unchecked((int)0xFF000000) | ((dr / 255) << 16) | ((dg / 255) << 8) | (db / 255);
                    }
                }
            }
        }

        void DrawDoodads(PixelBuffer buf, DecodedTerrainMap decoded, PixelClip clip, bool includeAll, int influence)
        {
            var jobs = new List<OverlayJob>();
            foreach (var cell in decoded.Cells)
            {
                if (!includeAll && !Intersects(cell, clip, influence)) continue;
                TryAddSpriteJob(jobs, cell, cell.A2, 0);
                TryAddSpriteJob(jobs, cell, cell.A1, 1);
                TryAddSpriteJob(jobs, cell, cell.A3, 2);
            }
            jobs.Sort((a, b) =>
            {
                int c = a.Cy.CompareTo(b.Cy);
                if (c != 0) return c;
                c = a.Layer.CompareTo(b.Layer);
                if (c != 0) return c;
                return a.Order.CompareTo(b.Order);
            });
            foreach (var job in jobs)
                Paste(buf, job.Sprite, job.X, job.Y, clip);
        }

        void TryAddSpriteJob(List<OverlayJob> jobs, TerrainCell cell, TerrainSlot? slot, int order)
        {
            if (!slot.HasValue || slot.Value.IsEmpty) return;
            if (!_defs.TryGetValue(slot.Value.TerrainId, out var tdef)) return;
            string name = tdef.ImageAt(slot.Value.Variant);
            if (string.IsNullOrEmpty(name)) return;
            if (!_doodads.TryGet(name, out var crop, out var sl)) return;
            ToCanvas(cell, out int cx, out int cy);
            int x = cx - sl.RefX + slot.Value.Dx * TerrainGeometry.Scale;
            int y = cy - sl.RefY + slot.Value.Dy * TerrainGeometry.Scale;
            jobs.Add(new OverlayJob { Cy = cy, Layer = tdef.Layer, Order = order, X = x, Y = y, Sprite = crop });
        }

        void EnsureEntityAtlases(bool wantBuildings, bool wantForts)
        {
            if (string.IsNullOrEmpty(_entityDir) || !Directory.Exists(_entityDir))
                return;
            if (wantBuildings && _buildingsAtlas == null)
            {
                var atlas = new TerrainAtlas(_entityDir);
                atlas.LoadXml(Path.Combine(_entityDir, "buildings.xml"));
                _buildingsAtlas = atlas;
            }
            if (wantForts)
            {
                if (_fortsAtlas == null)
                {
                    var atlas = new TerrainAtlas(_entityDir);
                    atlas.LoadXml(Path.Combine(_entityDir, "battleres.xml"));
                    _fortsAtlas = atlas;
                }
                if (_gunsAtlas == null)
                {
                    string gunXml = Path.Combine(_entityDir, "unit_artillery.xml");
                    if (File.Exists(gunXml))
                    {
                        var atlas = new TerrainAtlas(_entityDir);
                        atlas.LoadXml(gunXml);
                        _gunsAtlas = atlas;
                    }
                }
            }
        }

        void CollectBuildingJobs(DecodedTerrainMap decoded, List<OverlayJob> jobs, PixelClip clip, bool includeAll, int influence)
        {
            foreach (var item in decoded.Buildings)
            {
                if (item.Index < 0 || item.Index >= decoded.Cells.Count) continue;
                var cell = decoded.Cells[item.Index];
                if (!includeAll && !Intersects(cell, clip, influence)) continue;
                bool snow = TerrainDecode.CellIsSnow(cell, item.Val2);
                if (!TrySlice(_buildingsAtlas, BuildingNames(item, snow), out var crop, out var sl))
                    continue;
                ToCanvas(cell, out int cx, out int cy);
                int x = cx - sl.RefX + item.Dx * TerrainGeometry.Scale;
                int y = cy - sl.RefY + item.Dy * TerrainGeometry.Scale;
                jobs.Add(new OverlayJob { Cy = cy, Layer = 11, X = x, Y = y, Sprite = crop });
            }
        }

        void CollectFortJobs(DecodedTerrainMap decoded, List<OverlayJob> jobs, PixelClip clip, bool includeAll, int influence)
        {
            foreach (var item in decoded.Forts)
            {
                if (item.Index < 0 || item.Index >= decoded.Cells.Count) continue;
                var cell = decoded.Cells[item.Index];
                if (!includeAll && !Intersects(cell, clip, influence)) continue;
                bool snow = TerrainDecode.CellIsSnow(cell);
                ToCanvas(cell, out int cx, out int cy);
                int fid = item.FortId;
                PixelBuffer crop;
                AtlasSlice sl;
                int x, y;
                if (fid == 1 || fid == 2)
                {
                    var names = new List<string> { "fortification_" + fid + (snow ? "s" : "") + ".png" };
                    if (snow) names.Add("fortification_" + fid + ".png");
                    if (!TrySlice(_fortsAtlas, names, out crop, out sl))
                        continue;
                    x = cx - sl.RefX;
                    y = cy - sl.RefY;
                }
                else
                {
                    if (!FortGuns.TryGetValue(fid, out var body))
                        continue;
                    var names = snow
                        ? new List<string> { body.Snow, body.Normal }
                        : new List<string> { body.Normal };
                    if (!TrySlice(_gunsAtlas, names, out crop, out sl))
                        continue;
                    if (Math.Abs(body.Scale - 1f) > 1e-6f)
                        crop = ResizeNearest(crop, Math.Max(1, (int)(crop.Width * body.Scale)), Math.Max(1, (int)(crop.Height * body.Scale)));
                    int ox = body.Ox * TerrainGeometry.Scale;
                    // 炮类贴图原图朝左：朝右时需要镜像。
                    if (!item.FaceLeft)
                    {
                        crop = FlipHorizontal(crop);
                        ox = -ox;
                    }
                    x = cx - crop.Width / 2 + ox;
                    y = cy - (int)(crop.Height * 0.72f) + body.Oy * TerrainGeometry.Scale;
                }
                if (FortYLift.TryGetValue(fid, out int lift))
                    y -= lift * TerrainGeometry.Scale;
                jobs.Add(new OverlayJob { Cy = cy, Layer = 10, X = x, Y = y, Sprite = crop });
            }
        }

        List<string> BuildingNames(TerrainMapBuilding item, bool snow)
        {
            int bid = item.BuildingId;
            int typ = _buildingTypes.TryGetValue(bid, out int t) ? t : 1;
            string snowN, norm;
            if (typ == 4)
            {
                int n = item.Variant % 4 + 1;
                snowN = "b_" + bid + "s_" + n + ".png";
                norm = "b_" + bid + "_" + n + ".png";
            }
            else if (typ == 6)
            {
                int n = item.Variant % 6 + 1;
                snowN = "b_" + bid + "s_" + n + ".png";
                norm = "b_" + bid + "_" + n + ".png";
            }
            else
            {
                snowN = "b_" + bid + "s.png";
                norm = "b_" + bid + ".png";
            }
            return snow ? new List<string> { snowN, norm } : new List<string> { norm };
        }

        static bool TrySlice(TerrainAtlas atlas, List<string> names, out PixelBuffer crop, out AtlasSlice slice)
        {
            crop = null;
            slice = null;
            if (atlas == null) return false;
            foreach (var name in names)
            {
                if (atlas.TryGet(name, out crop, out slice))
                    return true;
            }
            return false;
        }

        static void Paste(PixelBuffer dst, PixelBuffer src, int x, int y, PixelClip clip)
        {
            int x0 = Math.Max(clip.X0, x);
            int y0 = Math.Max(clip.Y0, y);
            int x1 = Math.Min(clip.X1, x + src.Width);
            int y1 = Math.Min(clip.Y1, y + src.Height);
            if (x0 >= x1 || y0 >= y1) return;

            int dw = dst.Width;
            var srcPx = src.Argb;
            var dstPx = dst.Argb;
            int sw = src.Width;
            for (int dy = y0; dy < y1; dy++)
            {
                int drow = dy * dw;
                int srow = (dy - y) * sw;
                for (int dx = x0; dx < x1; dx++)
                {
                    int sp = srcPx[srow + (dx - x)];
                    int a = (sp >> 24) & 255;
                    if (a == 0) continue;
                    int destI = drow + dx;
                    if (a == 255)
                    {
                        dstPx[destI] = sp;
                        continue;
                    }
                    int dp = dstPx[destI];
                    int ia = 255 - a;
                    int r = (((dp >> 16) & 255) * ia + ((sp >> 16) & 255) * a) / 255;
                    int g = (((dp >> 8) & 255) * ia + ((sp >> 8) & 255) * a) / 255;
                    int b = ((dp & 255) * ia + (sp & 255) * a) / 255;
                    int aa = Math.Min(255, ((dp >> 24) & 255) + a);
                    dstPx[destI] = (aa << 24) | (r << 16) | (g << 8) | b;
                }
            }
        }

        static PixelBuffer ResizeNearest(PixelBuffer src, int w, int h)
        {
            var dst = new PixelBuffer { Width = w, Height = h, Argb = new int[w * h] };
            for (int y = 0; y < h; y++)
            {
                int sy = y * src.Height / h;
                for (int x = 0; x < w; x++)
                {
                    int sx = x * src.Width / w;
                    dst.Argb[y * w + x] = src.Argb[sy * src.Width + sx];
                }
            }
            return dst;
        }

        static PixelBuffer FlipHorizontal(PixelBuffer src)
        {
            int w = src.Width;
            int h = src.Height;
            var dst = new PixelBuffer { Width = w, Height = h, Argb = new int[w * h] };
            for (int y = 0; y < h; y++)
            {
                int srow = y * w;
                int drow = y * w;
                for (int x = 0; x < w; x++)
                    dst.Argb[drow + x] = src.Argb[srow + (w - 1 - x)];
            }
            return dst;
        }

        static void FillUnoccupied(PixelBuffer buf, byte[] occ, int color, PixelClip clip)
        {
            int cw = buf.Width;
            var px = buf.Argb;
            for (int y = clip.Y0; y < clip.Y1; y++)
            {
                int row = y * cw;
                for (int x = clip.X0; x < clip.X1; x++)
                {
                    int i = row + x;
                    if (occ[i] == 0)
                        px[i] = color;
                }
            }
        }

        static void ToCanvas(TerrainCell cell, out int cx, out int cy)
        {
            cx = TerrainGeometry.Pad + cell.X * TerrainGeometry.Scale;
            cy = TerrainGeometry.Pad + cell.Y * TerrainGeometry.Scale;
        }

        static int ColorToArgb(int r, int g, int b, int a) => (a << 24) | (r << 16) | (g << 8) | b;
        static int Clamp(int x) => x < 0 ? 0 : (x > 255 ? 255 : x);

        struct OverlayJob
        {
            public int Cy, Layer, Order, X, Y;
            public PixelBuffer Sprite;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bitmap?.Dispose();
            _bitmap = null;
            _buf = null;
            _occ = null;
        }
    }
}
