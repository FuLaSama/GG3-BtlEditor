using BtlCore.Front;
using BtlCore.Scripting;
using BtldMapEditor.Front;

namespace BtldMapEditor
{
    public partial class MainEditorForm
    {

        const int RandomOffsetMin = -32, RandomOffsetMax = 32;
        static readonly HashSet<int> ProtectedRandomTerrainIds = new() { 46, 47, 48, 49, 50 };
        PaletteTarget GetActiveTerrainLayerTarget() => TerrainPage?.ActivePaletteTarget ?? PaletteTarget.Main;

        void ClearTerrainBrush()
        {
            _terrainBrush = null;
            TerrainPage?.ClearPaletteSelection();
            TerrainPage?.SetText("brush", "画笔：未选择。点选贴图后拖到地图；V 随机变种，O 随机偏移。");
        }

        void RefreshSelectedTerrainUi()
        {
            if (ActivePageId != "terrain") return;
            var cell = _selectedCellIdx >= 0 && _selectedCellIdx < mapCanvas.Cells.Count ? mapCanvas.Cells[_selectedCellIdx] : null;
            TerrainPage?.HighlightPalettes(cell);
        }

        void MapCanvasPaletteDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(PaletteDrag)) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void MapCanvasPaletteDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(PaletteDrag)))
            {
                e.Effect = DragDropEffects.None;
                mapCanvas.DropPreviewIndex = -1;
                return;
            }
            e.Effect = DragDropEffects.Copy;
            Point pt = mapCanvas.PointToClient(new Point(e.X, e.Y));
            mapCanvas.DropPreviewIndex = mapCanvas.HitTestCell(pt);
        }

        void MapCanvasPaletteDragDrop(object sender, DragEventArgs e)
        {
            mapCanvas.DropPreviewIndex = -1;
            if (!e.Data.GetDataPresent(typeof(PaletteDrag))) return;
            var drag = (PaletteDrag)e.Data.GetData(typeof(PaletteDrag));
            Point pt = mapCanvas.PointToClient(new Point(e.X, e.Y));
            int idx = mapCanvas.HitTestCell(pt);
            if (idx >= 0)
                ApplyPaletteToCell(idx, drag.Item, drag.Target, recordHistory: true);
        }

        bool ApplyPaletteToCell(int cellIdx, PaletteItem item, PaletteTarget target, bool recordHistory)
        {
            if (item == null || cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return false;
            bool ok = target == PaletteTarget.Climate
                ? RunEdit("apply_climate", new ScriptArgs
                {
                    CellIndex = cellIdx,
                    Item = EditInput(
                        ("sea", item.Sea == true),
                        ("t", item.T),
                        ("variant", item.Variant))
                })
                : RunEdit("apply_layer", new ScriptArgs
                {
                    CellIndex = cellIdx,
                    Item = EditInput(
                        ("layer", TerrainLayerKey(target)),
                        ("terrain_id", item.TerrainId ?? 0),
                        ("variant", item.Variant),
                        ("dx", item.Dx),
                        ("dy", item.Dy))
                });
            if (!ok) return false;
            if (cellIdx == _selectedCellIdx)
            {
                RefreshSelectedTerrainUi();
            }
            if (recordHistory)
                AddHistoryState();
            return true;
        }

        void ClearPaletteLayer(int cellIdx, PaletteTarget target, bool recordHistory)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;
            bool ok = target == PaletteTarget.Climate
                ? RunEdit("set_sea", new ScriptArgs { CellIndex = cellIdx, Value = false })
                : RunEdit("clear_layer", new ScriptArgs
                {
                    CellIndex = cellIdx,
                    Input = EditInput(("layer", TerrainLayerKey(target)))
                });
            if (!ok) return;
            if (cellIdx == _selectedCellIdx)
            {
                RefreshSelectedTerrainUi();
            }
            if (recordHistory)
                AddHistoryState();
        }

        void MapCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || (Control.ModifierKeys & Keys.Shift) == 0) return;
            if (ActivePageId != "terrain") return;
            if (_selectedCellIdx < 0) return;
            if (!LayerHasContent(mapCanvas.Cells[_selectedCellIdx], GetActiveTerrainLayerTarget())) return;
            float mapX = e.X - mapCanvas.AutoScrollPosition.X;
            float mapY = e.Y - mapCanvas.AutoScrollPosition.Y;
            if (mapCanvas.GetCellAtPoint(mapX, mapY) != _selectedCellIdx) return;
            _terrainOffsetDragging = true;
            mapCanvas.Capture = true;
            ApplyTerrainOffsetFromMouse(mapX, mapY, recordHistory: false);
        }

        void MapCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!_terrainOffsetDragging) return;
            if ((Control.ModifierKeys & Keys.Shift) == 0 || e.Button != MouseButtons.Left) return;
            float mapX = e.X - mapCanvas.AutoScrollPosition.X;
            float mapY = e.Y - mapCanvas.AutoScrollPosition.Y;
            ApplyTerrainOffsetFromMouse(mapX, mapY, recordHistory: false);
        }

        void MapCanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (!_terrainOffsetDragging || e.Button != MouseButtons.Left) return;
            _terrainOffsetDragging = false;
            mapCanvas.Capture = false;
            AddHistoryState();
        }

        static bool IsProtectedRandomTerrain(int terrainId)
        {
            if (terrainId <= 0) return false;
            if (ProtectedRandomTerrainIds.Contains(terrainId)) return true;
            return GameSettings.MapTerrains.TryGetValue(terrainId, out var setting) && setting.Type == 10;
        }

        void PerformRandomVariantAction()
        {
            if (!HasDoc || _selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            if (ActivePageId != "terrain") return;

            var cell = mapCanvas.Cells[_selectedCellIdx];
            RandomizeCellVariants(cell, Random.Shared);

            mapCanvas.Invalidate();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            statusLabel.Text = $"随机变种：地块 #{_selectedCellIdx}";
        }

        void PerformRandomOffsetAction()
        {
            if (!HasDoc || _selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            if (ActivePageId != "terrain") return;

            var cell = mapCanvas.Cells[_selectedCellIdx];
            int changed = RandomizeCellOffsets(cell, Random.Shared);

            if (changed == 0)
            {
                statusLabel.Text = "随机偏移：当前地块无可偏移的贴图层";
                return;
            }

            mapCanvas.Invalidate();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            statusLabel.Text = $"随机偏移：地块 #{_selectedCellIdx}（{changed} 层）";
        }

        void PerformGlobalRandomVariantAction()
        {
            if (!HasDoc || mapCanvas.Cells.Count == 0) return;

            var rng = Random.Shared;
            foreach (var cell in mapCanvas.Cells)
                RandomizeCellVariants(cell, rng);

            mapCanvas.Invalidate();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            statusLabel.Text = $"全局随机变种：{mapCanvas.Cells.Count} 格";
        }

        void PerformGlobalRandomOffsetAction()
        {
            if (!HasDoc || mapCanvas.Cells.Count == 0) return;

            var rng = Random.Shared;
            int layerCount = 0;
            foreach (var cell in mapCanvas.Cells)
                layerCount += RandomizeCellOffsets(cell, rng);

            mapCanvas.Invalidate();
            RefreshSelectedTerrainUi();
            AddHistoryState();
            statusLabel.Text = layerCount > 0
                ? $"全局随机偏移：{mapCanvas.Cells.Count} 格，{layerCount} 层"
                : "全局随机偏移：地图上没有可偏移的贴图层";
        }

        static string PaletteTargetName(PaletteTarget target)
        {
            switch (target)
            {
                case PaletteTarget.Climate: return "地质";
                case PaletteTarget.Main: return "主地形";
                case PaletteTarget.Secondary: return "次地形";
                case PaletteTarget.Decor: return "装饰";
                default: return target.ToString();
            }
        }

        static TerrainEdits.Cell ViewOf(MapCell cell)
        {
            if (cell == null) return null;
            return new TerrainEdits.Cell
            {
                Terrain = cell.Terrain,
                Decor = cell.Attr,
                Main = cell.AttrA2,
                Secondary = cell.AttrA3,
            };
        }

        static string TerrainLayerKey(PaletteTarget target)
        {
            if (target == PaletteTarget.Decor) return "decor";
            if (target == PaletteTarget.Main) return "main";
            return "secondary";
        }

        static BtlStruct GetLayerAttr(MapCell cell, PaletteTarget target)
        {
            if (target == PaletteTarget.Decor) return cell.Attr;
            if (target == PaletteTarget.Main) return cell.AttrA2;
            if (target == PaletteTarget.Secondary) return cell.AttrA3;
            return null;
        }

        static bool LayerHasContent(MapCell cell, PaletteTarget target)
        {
            if (cell == null || target == PaletteTarget.Climate) return false;
            return TerrainEdits.HasContent(ViewOf(cell), TerrainLayerKey(target));
        }

        void ApplyTerrainOffsetFromMouse(float mapX, float mapY, bool recordHistory)
        {
            if (_selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            var target = GetActiveTerrainLayerTarget();
            if (target == PaletteTarget.Climate) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (!LayerHasContent(cell, target)) return;
            if (!mapCanvas.TryGetCellCenter(_selectedCellIdx, out float cx, out float cy)) return;

            float hDist = mapCanvas.Radius * 1.5f;
            float vDist = (float)Math.Sqrt(3) * mapCanvas.Radius;
            float pxPerDx = hDist / TerrainGeometry.ColW * TerrainGeometry.Scale;
            float pxPerDy = vDist / TerrainGeometry.RowH * TerrainGeometry.Scale;
            if (pxPerDx < 0.001f) pxPerDx = 1f;
            if (pxPerDy < 0.001f) pxPerDy = 1f;

            int dx = (int)Math.Round((mapX - cx) / pxPerDx);
            int dy = (int)Math.Round((mapY - cy) / pxPerDy);
            dx = Math.Max(-128, Math.Min(127, dx));
            dy = Math.Max(-128, Math.Min(127, dy));

            var attr = GetLayerAttr(cell, target);
            if (attr == null) return;
            if (FrontNav.AttrI8(attr, 2) == (sbyte)dx && FrontNav.AttrI8(attr, 3) == (sbyte)dy) return;
            if (!RunEdit("set_offset", new ScriptArgs
            {
                CellIndex = _selectedCellIdx,
                Input = EditInput(("layer", TerrainLayerKey(target)), ("dx", dx), ("dy", dy))
            })) return;
            RefreshSelectedTerrainUi();
            if (recordHistory) AddHistoryState();
        }

        static bool CellHasProtectedRiverLayer(MapCell cell)
        {
            if (LayerHasContent(cell, PaletteTarget.Decor)
                && IsProtectedRandomTerrain(FrontNav.AttrU8(GetLayerAttr(cell, PaletteTarget.Decor), 0)))
                return true;
            if (LayerHasContent(cell, PaletteTarget.Main)
                && IsProtectedRandomTerrain(FrontNav.AttrU8(GetLayerAttr(cell, PaletteTarget.Main), 0)))
                return true;
            if (LayerHasContent(cell, PaletteTarget.Secondary)
                && IsProtectedRandomTerrain(FrontNav.AttrU8(GetLayerAttr(cell, PaletteTarget.Secondary), 0)))
                return true;
            return false;
        }

        static void RandomizeCellVariants(MapCell cell, Random rng)
        {
            if (!CellHasProtectedRiverLayer(cell))
            {
                ushort t = cell.Terrain;
                int climate = t & 7;
                int flags = t >> 8;
                int variation = EditorPaletteCatalog.PickRandomClimateVariant(rng);
                cell.Terrain = (ushort)((climate & 7) | ((variation & 0x1F) << 3) | (flags << 8));
            }
            RandomizeLayerVariant(cell, PaletteTarget.Decor, rng);
            RandomizeLayerVariant(cell, PaletteTarget.Main, rng);
            RandomizeLayerVariant(cell, PaletteTarget.Secondary, rng);
        }

        static int RandomizeCellOffsets(MapCell cell, Random rng)
        {
            int changed = 0;
            if (RandomizeLayerOffset(cell, PaletteTarget.Decor, rng)) changed++;
            if (RandomizeLayerOffset(cell, PaletteTarget.Main, rng)) changed++;
            if (RandomizeLayerOffset(cell, PaletteTarget.Secondary, rng)) changed++;
            return changed;
        }

        static void RandomizeLayerVariant(MapCell cell, PaletteTarget target, Random rng)
        {
            if (!LayerHasContent(cell, target)) return;
            var attr = GetLayerAttr(cell, target);
            int id = FrontNav.AttrU8(attr, 0);
            if (IsProtectedRandomTerrain(id)) return;
            FrontNav.SetMember(attr, 1, (byte)EditorPaletteCatalog.PickRandomTerrainVariant(id, rng));
        }

        static bool RandomizeLayerOffset(MapCell cell, PaletteTarget target, Random rng)
        {
            if (!LayerHasContent(cell, target)) return false;
            var attr = GetLayerAttr(cell, target);
            int id = FrontNav.AttrU8(attr, 0);
            if (IsProtectedRandomTerrain(id)) return false;
            FrontNav.SetMember(attr, 2, (sbyte)rng.Next(RandomOffsetMin, RandomOffsetMax + 1));
            FrontNav.SetMember(attr, 3, (sbyte)rng.Next(RandomOffsetMin, RandomOffsetMax + 1));
            return true;
        }

        private void PerformCopyAction()
        {
            if (_selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (ActivePageId == "terrain")
            {
                _copiedTerrain = cell.Terrain;
                _copiedAttr = FrontNav.CloneStruct(cell.Attr);
                _copiedAttrA2 = FrontNav.CloneStruct(cell.AttrA2);
                _copiedAttrA3 = FrontNav.CloneStruct(cell.AttrA3);
            }
            else
            {
                _copiedTerrain = null;
                _copiedAttr = _copiedAttrA2 = _copiedAttrA3 = null;
            }
            var copied = new ScriptArgs { CellIndex = _selectedCellIdx };
            if (!RunEdit("copy_unit", copied) || !RunEdit("copy_landmarks", copied)) return;
            statusLabel.Text = $"复制成功：已将地块 #{_selectedCellIdx} 的完整数据（地形/部队/建筑/工事）存入画笔/剪贴板";
        }

        static object[] AttrParts(BtlStruct st)
        {
            if (st == null) return null;
            return new object[]
            {
                FrontNav.AttrU8(st, 0),
                FrontNav.AttrU8(st, 1),
                FrontNav.AttrI8(st, 2),
                FrontNav.AttrI8(st, 3)
            };
        }

        bool PasteTerrainOnto(int cellIdx)
        {
            if (!_copiedTerrain.HasValue) return false;
            return RunEdit("paste_terrain", new ScriptArgs
            {
                CellIndex = cellIdx,
                Input = EditInput(
                    ("terrain", _copiedTerrain.Value),
                    ("decor", AttrParts(_copiedAttr)),
                    ("main", AttrParts(_copiedAttrA2)),
                    ("secondary", AttrParts(_copiedAttrA3)))
            });
        }

        bool PasteUnitOnto(int cellIdx)
        {
            return RunEdit("paste_unit", new ScriptArgs { CellIndex = cellIdx });
        }

        bool PasteLandmarksOnto(int cellIdx)
        {
            return RunEdit("paste_landmarks", new ScriptArgs { CellIndex = cellIdx });
        }

        private void PerformPasteAction()
        {
            if (_selectedCellIdx < 0) return;
            if (ActivePageId == "terrain")
            {
                if (_terrainBrush != null)
                {
                    if (!ApplyPaletteToCell(_selectedCellIdx, _terrainBrush.Item, _terrainBrush.Target, recordHistory: false)) return;
                    CellSelectedClick(_selectedCellIdx);
                    mapCanvas.Invalidate();
                    statusLabel.Text = $"已将「{_terrainBrush.Item.Label}」应用到地块 #{_selectedCellIdx}";
                }
                else if (_copiedTerrain.HasValue)
                {
                    if (!PasteTerrainOnto(_selectedCellIdx)) return;
                    CellSelectedClick(_selectedCellIdx);
                    mapCanvas.Invalidate();
                    statusLabel.Text = $"粘贴成功：已将地形与属性粘贴至地块 #{_selectedCellIdx}";
                }
                else statusLabel.Text = "粘贴失败：剪贴板中无地形数据";
            }
            else if (ActivePageId == "unit")
            {
                if (!PasteUnitOnto(_selectedCellIdx)) return;
                CellSelectedClick(_selectedCellIdx);
                mapCanvas.Invalidate();
                statusLabel.Text = $"粘贴成功：已将部队数据粘贴至地块 #{_selectedCellIdx}";
            }
            else if (ActivePageId == "site")
            {
                if (!PasteLandmarksOnto(_selectedCellIdx)) return;
                CellSelectedClick(_selectedCellIdx);
                mapCanvas.Invalidate();
                statusLabel.Text = $"粘贴成功：已将地标建筑与工事数据粘贴至地块 #{_selectedCellIdx}";
            }
            AddHistoryState();
        }

        private void PaintCell(int cellIdx)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;
            if (ActivePageId == "terrain")
            {
                if (_terrainBrush != null)
                    ApplyPaletteToCell(cellIdx, _terrainBrush.Item, _terrainBrush.Target, recordHistory: false);
                else if (_copiedTerrain.HasValue)
                    PasteTerrainOnto(cellIdx);
            }
            else if (ActivePageId == "unit")
                PasteUnitOnto(cellIdx);
            else if (ActivePageId == "site")
                PasteLandmarksOnto(cellIdx);
        }
    }
}
