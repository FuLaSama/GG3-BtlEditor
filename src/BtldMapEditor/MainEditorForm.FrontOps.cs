using System.Globalization;
using BtlCore.Front;
using BtldMapEditor.Front;

namespace BtldMapEditor
{
    /// <summary>
    /// 主窗体里「改关卡数据」的那一半：选中格子后刷部队/建筑面板、
    /// 增删目标/天气/势力/增援，以及把 NumericUpDown 写回对应 field id。
    /// 不经过 *Model 类；列表项就是 BtlTable。
    /// </summary>
    public partial class MainEditorForm
    {
        BtlVector BehaviorVec()
        {
            var ai = FrontNav.EnsureTable(Doc.Root, 6);
            return FrontNav.EnsureVec(ai, 1, "table");
        }

        BtlVector TargetVec()
        {
            var meta = FrontNav.EnsureTable(Doc.Root, 2);
            return FrontNav.EnsureVec(meta, 0, "table");
        }

        BtlVector WeatherVec()
        {
            var w = FrontNav.EnsureTable(Doc.Root, 9);
            return FrontNav.EnsureVec(w, 0, "table");
        }

        BtlVector ReinforceVec()
        {
            var battle = FrontNav.EnsureTable(Doc.Root, 3);
            return FrontNav.EnsureVec(battle, 1, "table");
        }

        BtlVector FactionVec()
        {
            var f = FrontNav.EnsureTable(Doc.Root, 4);
            return FrontNav.EnsureVec(f, 0, "table");
        }

        static decimal? Nud(NullableNumericUpDown n) => n?.NullableValue;
        static ushort? U16(NullableNumericUpDown n) => n?.NullableValue == null ? null : (ushort)n.NullableValue.Value;
        static byte? U8(NullableNumericUpDown n) => n?.NullableValue == null ? null : (byte)n.NullableValue.Value;
        static short? I16(NullableNumericUpDown n) => n?.NullableValue == null ? null : (short)n.NullableValue.Value;
        static sbyte? I8(NullableNumericUpDown n) => n?.NullableValue == null ? null : (sbyte)n.NullableValue.Value;
        static uint? U32(NullableNumericUpDown n) => n?.NullableValue == null ? null : (uint)n.NullableValue.Value;
        static float? F32(NullableNumericUpDown n) => n?.NullableValue == null ? null : (float)n.NullableValue.Value;

        static void SetOpt(BtlTable tbl, int id, string t, object value) => FrontNav.SetScalar(tbl, id, t, value);

        static decimal? Dec(long? v) => v == null ? null : (decimal)v.Value;

        void ReloadStageLists()
        {
            lvTargets.Items.Clear();
            foreach (var t in FrontNav.TableItems(FrontNav.Targets(Doc)))
            {
                var item = new ListViewItem(FrontNav.ScalarI64N(t, 0)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.ScalarI64N(t, 1)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.ScalarI64N(t, 2)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.ScalarI64N(t, 3)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.ScalarI64N(t, 4)?.ToString() ?? "");
                lvTargets.Items.Add(item);
            }

            lvReinforces.Items.Clear();
            foreach (var rp in FrontNav.TableItems(FrontNav.ReinforcePoints(Doc)))
            {
                var item = new ListViewItem(FrontNav.ScalarI64N(rp, 0)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.ScalarI64N(rp, 1)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.AsBool(FrontNav.ScalarV(rp, 2)) ? "是" : "否");
                item.SubItems.Add(FrontNav.ScalarI64N(rp, 3)?.ToString() ?? "");
                lvReinforces.Items.Add(item);
            }

            lvWeathers.Items.Clear();
            foreach (var w in FrontNav.TableItems(FrontNav.Weathers(Doc)))
            {
                var item = new ListViewItem(FrontNav.ScalarI64(w, 0).ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(FrontNav.ScalarI64(w, 1).ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(FrontNav.ScalarI64(w, 2).ToString(CultureInfo.InvariantCulture));
                lvWeathers.Items.Add(item);
            }

            lvReinforceUnits.Items.Clear();
            _selectedOffMapUnit = null;
            foreach (var agent in FrontNav.TableItems(FrontNav.Agents(Doc)))
            {
                if (FrontNav.AgentU16(agent, 0) == 65535)
                    lvReinforceUnits.Items.Add(new ListViewItem(FrontNav.AgentU16(agent, 2).ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>点中一格：把该格的部队/建筑/工事填到右侧。同一 BtlTable 引用。</summary>
        private void CellSelectedClick(int cellIdx)
        {
            _selectedCellIdx = cellIdx;
            _selectedOffMapUnit = null;
            if (lvReinforceUnits.SelectedIndices.Count > 0)
                lvReinforceUnits.SelectedIndices.Clear();

            var cell = mapCanvas.Cells[cellIdx];
            string coordsText = $"cellIdx: {cellIdx} (X: {cell.X}, Y: {cell.Y})";
            lblCellCoords.Text = coordsText;
            lblCellCoordsUnit.Text = coordsText;
            lblCellCoordsBuilding.Text = coordsText;

            _isUpdatingTerrainUi = true;
            try
            {
                chkPlayableFlag.Checked = ((cell.Terrain >> 8) & 1) == 0;
                chkSeaFlag.Checked = ((cell.Terrain >> 8) & 2) != 0;
                RefreshTerrainPaletteSelection(cell);
                RefreshTerrainOffsetUi(cell);
            }
            finally { _isUpdatingTerrainUi = false; }

            _isUpdatingUnitUi = true;
            try
            {
                if (cell.Unit != null)
                {
                    gbUnit.Text = "部队配置";
                    FillUnitControls(cell.Unit);
                    btnAddDeleteUnit.Text = "删除部队";
                }
                else
                {
                    gbUnit.Text = "部队配置 (未放置)";
                    ClearUnitControls();
                    btnAddDeleteUnit.Text = "添加部队";
                }
            }
            finally
            {
                UpdateOptionalGroupEnabledStates();
                _isUpdatingUnitUi = false;
            }

            RefreshAiBehaviorsUi();
            FillBuildingControls(cell);
            FillFortControls(cell);
        }

        void ClearUnitControls()
        {
            nudUnitType.NullableValue = null;
            lblUnitTypeName.Text = "兵种名称: 无";
            nudUnitFaction.NullableValue = null;
            nudUnitAgentId.NullableValue = null;
            nudUnitHp.NullableValue = null;
            nudUnitMaxHp.NullableValue = null;
            nudUnitStack.NullableValue = null;
            nudUnitLevel.NullableValue = null;
            nudUnitMobility.NullableValue = null;
            nudUnitDirection.NullableValue = null;
            nudUnitVal8.NullableValue = null;
            nudUnitVal9.NullableValue = null;
            nudUnitPlayMode.NullableValue = null;
            nudUnitAiTarget.NullableValue = null;
            nudUnitFactionExtra.NullableValue = null;
            chkUnitBehavior.Checked = false;
            nudUnitBehaviorField0.NullableValue = null;
            nudUnitBehaviorId.NullableValue = null;
            nudUnitBehaviorField2.NullableValue = null;
            nudUnitBehaviorRadius.NullableValue = null;
            nudUnitBehaviorField4.NullableValue = null;
            nudUnitBehaviorCenter.NullableValue = null;
            nudUnitBehaviorField6.NullableValue = null;
            chkUnitGeneral.Checked = false;
            nudUnitGeneralId.NullableValue = null;
            chkUnitGeneralActive.Checked = false;
            nudUnitGeneralParam2.NullableValue = null;
            lblUnitGeneralName.Text = "将领姓名: 无";
            chkUnitExArmy.Checked = false;
            nudUnitExArmyId.NullableValue = null;
            nudUnitExArmyHp.NullableValue = null;
            nudUnitExArmyMaxHp.NullableValue = null;
            nudUnitExArmyField1.NullableValue = null;
            nudUnitExArmyField4.NullableValue = null;
            nudUnitExArmyField5.NullableValue = null;
            lblUnitExArmyName.Text = "名称: 无";
        }

        void FillUnitControls(BtlTable unit)
        {
            if (unit == null) { ClearUnitControls(); return; }

            int? unitId = FrontNav.AgentU16N(unit, 3);
            nudUnitType.NullableValue = unitId;
            if (unitId.HasValue && GameSettings.Units.TryGetValue(unitId.Value, out var unitSetting))
                lblUnitTypeName.Text = "兵种名称: " + unitSetting;
            else
                lblUnitTypeName.Text = "兵种名称: " + (unitId.HasValue ? "未知兵种" : "无");

            nudUnitFaction.NullableValue = FrontNav.AgentU16N(unit, 1);
            nudUnitAgentId.NullableValue = FrontNav.AgentU16N(unit, 2);
            nudUnitHp.NullableValue = FrontNav.AgentU16N(unit, 6);
            nudUnitMaxHp.NullableValue = FrontNav.AgentU16N(unit, 7);

            var stack = FrontNav.AgentU16N(unit, 4);
            if (stack.HasValue)
            {
                nudUnitStack.NullableValue = stack.Value >> 8;
                nudUnitLevel.NullableValue = stack.Value & 0xFF;
            }
            else
            {
                nudUnitStack.NullableValue = null;
                nudUnitLevel.NullableValue = null;
            }

            var val5 = FrontNav.AgentU16N(unit, 5);
            if (val5.HasValue)
            {
                nudUnitMobility.NullableValue = (val5.Value >> 8) & 0xFF;
                nudUnitDirection.NullableValue = val5.Value & 0xFF;
            }
            else
            {
                nudUnitMobility.NullableValue = null;
                nudUnitDirection.NullableValue = null;
            }

            nudUnitVal8.NullableValue = FrontNav.AgentU16N(unit, 8);
            nudUnitVal9.NullableValue = FrontNav.AgentU16N(unit, 9);
            nudUnitPlayMode.NullableValue = Dec(FrontNav.ScalarI64N(unit, 2));
            nudUnitAiTarget.NullableValue = Dec(FrontNav.ScalarI64N(unit, 5));
            nudUnitFactionExtra.NullableValue = Dec(FrontNav.ScalarI64N(unit, 6));

            var beh = FrontNav.Child(unit, 3);
            if (beh != null)
            {
                chkUnitBehavior.Checked = true;
                nudUnitBehaviorField0.NullableValue = Dec(FrontNav.ScalarI64N(beh, 0));
                nudUnitBehaviorId.NullableValue = Dec(FrontNav.ScalarI64N(beh, 1));
                nudUnitBehaviorField2.NullableValue = Dec(FrontNav.ScalarI64N(beh, 2));
                nudUnitBehaviorRadius.NullableValue = Dec(FrontNav.ScalarI64N(beh, 3));
                nudUnitBehaviorField4.NullableValue = Dec(FrontNav.ScalarI64N(beh, 4));
                nudUnitBehaviorCenter.NullableValue = Dec(FrontNav.ScalarI64N(beh, 5));
                nudUnitBehaviorField6.NullableValue = Dec(FrontNav.ScalarI64N(beh, 6));
            }
            else
            {
                chkUnitBehavior.Checked = false;
                nudUnitBehaviorField0.NullableValue = null;
                nudUnitBehaviorId.NullableValue = null;
                nudUnitBehaviorField2.NullableValue = null;
                nudUnitBehaviorRadius.NullableValue = null;
                nudUnitBehaviorField4.NullableValue = null;
                nudUnitBehaviorCenter.NullableValue = null;
                nudUnitBehaviorField6.NullableValue = null;
            }

            var gen = FrontNav.Child(unit, 11);
            if (gen != null)
            {
                chkUnitGeneral.Checked = true;
                int? genId = (int?)FrontNav.ScalarI64N(gen, 0);
                nudUnitGeneralId.NullableValue = genId;
                chkUnitGeneralActive.Checked = FrontNav.Has(gen, 1) && FrontNav.AsBool(FrontNav.ScalarV(gen, 1));
                nudUnitGeneralParam2.NullableValue = Dec(FrontNav.ScalarI64N(gen, 2));
                lblUnitGeneralName.Text = "将领姓名: " + (genId.HasValue ? GameSettings.GetGeneralName(genId.Value) : "无");
            }
            else
            {
                chkUnitGeneral.Checked = false;
                nudUnitGeneralId.NullableValue = null;
                chkUnitGeneralActive.Checked = false;
                nudUnitGeneralParam2.NullableValue = null;
                lblUnitGeneralName.Text = "将领姓名: 无";
            }

            var ex = FrontNav.Child(unit, 10);
            if (ex != null)
            {
                chkUnitExArmy.Checked = true;
                int? exId = (int?)FrontNav.ScalarI64N(ex, 0);
                nudUnitExArmyId.NullableValue = exId;
                nudUnitExArmyField1.NullableValue = Dec(FrontNav.ScalarI64N(ex, 1));
                nudUnitExArmyHp.NullableValue = Dec(FrontNav.ScalarI64N(ex, 2));
                nudUnitExArmyMaxHp.NullableValue = Dec(FrontNav.ScalarI64N(ex, 3));
                nudUnitExArmyField4.NullableValue = Dec(FrontNav.ScalarI64N(ex, 4));
                nudUnitExArmyField5.NullableValue = Dec(FrontNav.ScalarI64N(ex, 5));
                if (exId.HasValue && GameSettings.Units.TryGetValue(exId.Value, out var exName))
                    lblUnitExArmyName.Text = "名称: " + exName;
                else
                    lblUnitExArmyName.Text = "名称: " + (exId.HasValue ? "未知特种" : "无");
            }
            else
            {
                chkUnitExArmy.Checked = false;
                nudUnitExArmyId.NullableValue = null;
                nudUnitExArmyHp.NullableValue = null;
                nudUnitExArmyMaxHp.NullableValue = null;
                nudUnitExArmyField1.NullableValue = null;
                nudUnitExArmyField4.NullableValue = null;
                nudUnitExArmyField5.NullableValue = null;
                lblUnitExArmyName.Text = "名称: 无";
            }
        }

        private void LoadUnitToEditor(BtlTable unit, string groupText)
        {
            if (unit == null) return;
            _isUpdatingUnitUi = true;
            try
            {
                gbUnit.Text = groupText;
                FillUnitControls(unit);
            }
            finally
            {
                UpdateOptionalGroupEnabledStates();
                _isUpdatingUnitUi = false;
            }
        }

        void FillBuildingControls(MapCell cell)
        {
            _isUpdatingBuildingUi = true;
            try
            {
                if (cell.TriggerBldg != null)
                {
                    gbBuilding.Text = "建筑配置";
                    var bData = FrontNav.BuildingData(cell.TriggerBldg);
                    int? bId = bData == null ? null : (int?)FrontNav.MemberU16(bData, 1);
                    nudBldgType.NullableValue = bId;
                    lblBldgTypeName.Text = "名: " + (bId.HasValue ? GameSettings.GetBuildingName(bId.Value) : "空");
                    nudBldgOwner.NullableValue = bData == null ? null : (decimal?)FrontNav.MemberI64(bData, 3);
                    nudBldgFlag.NullableValue = bData == null ? null : (decimal?)FrontNav.MemberU16(bData, 0);
                    nudBldgExtraFlag.NullableValue = bData == null ? null : (decimal?)FrontNav.MemberI64(bData, 2);
                    nudBldgDx.NullableValue = bData == null ? null : (decimal?)FrontNav.MemberI64(bData, 4);
                    nudBldgDy.NullableValue = bData == null ? null : (decimal?)FrontNav.MemberI64(bData, 5);
                    nudBldgField6.NullableValue = Dec(FrontNav.ScalarI64N(FrontNav.BuildingDetail(cell.TriggerBldg), 6));
                    btnAddDeleteBldg.Text = "删除建筑";
                }
                else
                {
                    gbBuilding.Text = "建筑配置 (未放置)";
                    nudBldgType.NullableValue = null;
                    lblBldgTypeName.Text = "建筑名称: 空";
                    nudBldgOwner.NullableValue = null;
                    nudBldgFlag.NullableValue = null;
                    nudBldgExtraFlag.NullableValue = null;
                    nudBldgDx.NullableValue = null;
                    nudBldgDy.NullableValue = null;
                    nudBldgField6.NullableValue = null;
                    btnAddDeleteBldg.Text = "添加建筑";
                }
            }
            finally { _isUpdatingBuildingUi = false; }
        }

        void FillFortControls(MapCell cell)
        {
            _isUpdatingFortUi = true;
            try
            {
                if (cell.TriggerFort != null)
                {
                    gbFort.Text = "军事工事配置";
                    var fort = FrontNav.FortDetail(cell.TriggerFort);
                    int? fId = (int?)FrontNav.ScalarI64N(fort, 0);
                    nudFortType.NullableValue = fId;
                    lblFortTypeName.Text = "工事名称: " + (fId.HasValue ? GameSettings.GetFortName(fId.Value) : "空");
                    nudFortField1.NullableValue = Dec(FrontNav.ScalarI64N(cell.TriggerFort, 1));
                    nudFortField3.NullableValue = Dec(FrontNav.ScalarI64N(fort, 3));
                    btnAddDeleteFort.Text = "删除工事";
                }
                else
                {
                    gbFort.Text = "军事工事配置 (未放置)";
                    nudFortType.NullableValue = null;
                    lblFortTypeName.Text = "工事名称: 空";
                    nudFortField1.NullableValue = null;
                    nudFortField3.NullableValue = null;
                    btnAddDeleteFort.Text = "添加工事";
                }
            }
            finally { _isUpdatingFortUi = false; }
        }

        private void LvReinforceUnitsSelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvReinforceUnits.SelectedIndices.Count == 0) return;
            int idx = lvReinforceUnits.SelectedIndices[0];
            int reinIdx = 0;
            BtlTable target = null;
            foreach (var agent in FrontNav.TableItems(FrontNav.Agents(Doc)))
            {
                if (FrontNav.AgentU16(agent, 0) != 65535) continue;
                if (reinIdx == idx) { target = agent; break; }
                reinIdx++;
            }
            if (target == null) return;
            _selectedOffMapUnit = target;
            _selectedCellIdx = -1;
            mapCanvas.SelectedCellIndex = -1;
            mapCanvas.Invalidate();
            lblCellCoordsUnit.Text = $"援军部队 (ID: {FrontNav.AgentU16(target, 2)})";
            LoadUnitToEditor(target, "部队配置 (援军)");
        }

        private void RefreshAiBehaviorsUi()
        {
            _isUpdatingBehaviorUi = true;
            lvFactionAiBehaviors.Items.Clear();
            foreach (var b in FrontNav.TableItems(FrontNav.Behaviors(Doc)))
            {
                ushort flag = (ushort)FrontNav.ScalarI64(b, 0);
                var cells = FrontNav.U16Items(FrontNav.ChildVec(b, 1));
                string cellsStr = cells.Count > 0 ? string.Join(", ", cells) : "无";
                var item = new ListViewItem(flag.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(cellsStr);
                lvFactionAiBehaviors.Items.Add(item);
            }
            _isUpdatingBehaviorUi = false;
        }

        void BehaviorSelectedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingBehaviorUi) return;
            var list = FrontNav.TableItems(FrontNav.Behaviors(Doc));
            if (lvFactionAiBehaviors.SelectedIndices.Count == 0 || list.Count == 0) return;
            int selIdx = lvFactionAiBehaviors.SelectedIndices[0];
            if (selIdx < 0 || selIdx >= list.Count) return;
            _isUpdatingBehaviorUi = true;
            var item = list[selIdx];
            nudAiBehaviorActionFlag.NullableValue = FrontNav.ScalarI64(item, 0);
            var cells = FrontNav.U16Items(FrontNav.ChildVec(item, 1));
            txtAiBehaviorTargetCells.Text = cells.Count > 0 ? string.Join(", ", cells) : "";
            _isUpdatingBehaviorUi = false;
        }

        void AddBehaviorRule(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            var vec = BehaviorVec();
            var tbl = BtlFrontJson.NewTable();
            SetOpt(tbl, 0, "u16", (ushort)(nudAiBehaviorActionFlag.NullableValue ?? 1));
            var cells = BtlFrontJson.NewVector("u16");
            foreach (var c in ParseTargetCellsInput(txtAiBehaviorTargetCells.Text)) cells.V.Add(c);
            tbl.F[1] = cells;
            vec.V.Add(tbl);
            RefreshAiBehaviorsUi();
            if (lvFactionAiBehaviors.Items.Count > 0)
            {
                int lastIdx = lvFactionAiBehaviors.Items.Count - 1;
                lvFactionAiBehaviors.Items[lastIdx].Selected = true;
                lvFactionAiBehaviors.Items[lastIdx].EnsureVisible();
            }
            AddHistoryState();
        }

        void SaveBehaviorRule(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.Behaviors(Doc));
            if (lvFactionAiBehaviors.SelectedIndices.Count == 0 || list.Count == 0)
            {
                MessageBox.Show("请先在表格中选中需要修改的 AI 规则项。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int selIdx = lvFactionAiBehaviors.SelectedIndices[0];
            if (selIdx < 0 || selIdx >= list.Count) return;
            var item = list[selIdx];
            SetOpt(item, 0, "u16", (ushort)(nudAiBehaviorActionFlag.NullableValue ?? 1));
            var cells = FrontNav.EnsureVec(item, 1, "u16");
            FrontNav.SetU16Items(cells, ParseTargetCellsInput(txtAiBehaviorTargetCells.Text));
            RefreshAiBehaviorsUi();
            if (selIdx < lvFactionAiBehaviors.Items.Count)
            {
                lvFactionAiBehaviors.Items[selIdx].Selected = true;
                lvFactionAiBehaviors.Items[selIdx].EnsureVisible();
            }
            AddHistoryState();
        }

        void DeleteBehaviorRule(object sender, EventArgs e)
        {
            var vec = FrontNav.Behaviors(Doc);
            if (lvFactionAiBehaviors.SelectedIndices.Count == 0 || vec?.V == null) return;
            int selIdx = lvFactionAiBehaviors.SelectedIndices[0];
            FrontNav.RemoveAt(vec, selIdx);
            RefreshAiBehaviorsUi();
            if (lvFactionAiBehaviors.Items.Count > 0)
            {
                int nextSel = Math.Min(selIdx, lvFactionAiBehaviors.Items.Count - 1);
                lvFactionAiBehaviors.Items[nextSel].Selected = true;
                lvFactionAiBehaviors.Items[nextSel].EnsureVisible();
            }
            AddHistoryState();
        }

        static void ApplyClimate(MapCell cell, PaletteItem item)
        {
            ushort t = cell.Terrain;
            int flags = t >> 8;
            int climate = t & 7;
            int variation = (t >> 3) & 0x1F;
            if (item.Sea == true) flags |= 2;
            else
            {
                climate = item.T ?? climate;
                variation = item.Variant;
            }
            cell.Terrain = (ushort)((climate & 7) | ((variation & 0x1F) << 3) | (flags << 8));
        }

        static void ApplyLayer(MapCell cell, PaletteItem item, PaletteTarget target)
        {
            SetLayer(cell, target, FrontNav.NewAttr((byte)(item.TerrainId ?? 0), (byte)item.Variant, (sbyte)item.Dx, (sbyte)item.Dy));
        }

        static void SetLayer(MapCell cell, PaletteTarget target, BtlStruct attr)
        {
            int bit = target == PaletteTarget.Decor ? 4 : target == PaletteTarget.Main ? 8 : 16;
            ushort t = cell.Terrain;
            if (attr == null) t = (ushort)(t & unchecked((ushort)~(bit << 8)));
            else t = (ushort)(t | (bit << 8));
            cell.Terrain = t;
            if (target == PaletteTarget.Decor) cell.Attr = attr ?? FrontNav.NewAttr(0, 0, 0, 0);
            else if (target == PaletteTarget.Main) cell.AttrA2 = attr;
            else cell.AttrA3 = attr;
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
            if (target == PaletteTarget.Climate) return false;
            int bit = LayerBit(target);
            if (((cell.Terrain >> 8) & bit) == 0) return false;
            var attr = GetLayerAttr(cell, target);
            return attr != null && FrontNav.AttrU8(attr, 0) != 0;
        }

        void RefreshTerrainOffsetUi(MapCell cell)
        {
            if (trkTerrainDx == null || trkTerrainDy == null) return;
            var target = GetActiveTerrainLayerTarget();
            bool enabled = cell != null && LayerHasContent(cell, target);
            _isUpdatingOffsetUi = true;
            try
            {
                lblTerrainOffsetTitle.Text = "贴图偏移（" + PaletteTargetName(target) + "）";
                trkTerrainDx.Enabled = enabled;
                trkTerrainDy.Enabled = enabled;
                if (!enabled)
                {
                    trkTerrainDx.Value = 0;
                    trkTerrainDy.Value = 0;
                    lblTerrainDxVal.Text = "0";
                    lblTerrainDyVal.Text = "0";
                    return;
                }
                var attr = GetLayerAttr(cell, target);
                int dx = attr == null ? 0 : FrontNav.AttrI8(attr, 2);
                int dy = attr == null ? 0 : FrontNav.AttrI8(attr, 3);
                trkTerrainDx.Value = Math.Max(-128, Math.Min(127, dx));
                trkTerrainDy.Value = Math.Max(-128, Math.Min(127, dy));
                lblTerrainDxVal.Text = dx.ToString();
                lblTerrainDyVal.Text = dy.ToString();
            }
            finally { _isUpdatingOffsetUi = false; }
        }

        void TerrainOffsetChanged(object sender, EventArgs e)
        {
            if (_isUpdatingOffsetUi || _selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            var target = GetActiveTerrainLayerTarget();
            if (target == PaletteTarget.Climate) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (!LayerHasContent(cell, target)) return;
            var attr = GetLayerAttr(cell, target);
            if (attr == null) return;
            FrontNav.SetMember(attr, 2, (sbyte)trkTerrainDx.Value);
            FrontNav.SetMember(attr, 3, (sbyte)trkTerrainDy.Value);
            lblTerrainDxVal.Text = FrontNav.AttrI8(attr, 2).ToString();
            lblTerrainDyVal.Text = FrontNav.AttrI8(attr, 3).ToString();
            mapCanvas.Invalidate();
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
            FrontNav.SetMember(attr, 2, (sbyte)dx);
            FrontNav.SetMember(attr, 3, (sbyte)dy);
            mapCanvas.Invalidate();
            RefreshTerrainOffsetUi(cell);
            if (recordHistory) AddHistoryState();
        }

        void RefreshTerrainPaletteSelection(MapCell cell)
        {
            if (cell == null || lblTerrainLayers == null) return;
            ushort terrain = cell.Terrain;
            int climate = terrain & 7;
            int climateVar = (terrain >> 3) & 0x1F;
            bool sea = ((terrain >> 8) & 2) != 0;
            string climateName = GetBaseTerrainName(climate) + " 变种" + climateVar;
            if (sea) climateName += " + 海";

            if (chkSeaFlag != null)
            {
                bool prev = _isUpdatingTerrainUi;
                _isUpdatingTerrainUi = true;
                try { chkSeaFlag.Checked = sea; }
                finally { _isUpdatingTerrainUi = prev; }
            }

            _paletteClimate?.HighlightMatch(it =>
            {
                if (it.Sea == true) return sea;
                int shown = Math.Min(climateVar, 11);
                return it.T == climate && it.Variant == shown;
            });

            int decorId = ((terrain >> 8) & 4) != 0 && cell.Attr != null ? FrontNav.AttrU8(cell.Attr, 0) : 0;
            int decorVar = cell.Attr != null ? FrontNav.AttrU8(cell.Attr, 1) : 0;
            int mainId = ((terrain >> 8) & 8) != 0 && cell.AttrA2 != null ? FrontNav.AttrU8(cell.AttrA2, 0) : 0;
            int mainVar = cell.AttrA2 != null ? FrontNav.AttrU8(cell.AttrA2, 1) : 0;
            int secId = ((terrain >> 8) & 16) != 0 && cell.AttrA3 != null ? FrontNav.AttrU8(cell.AttrA3, 0) : 0;
            int secVar = cell.AttrA3 != null ? FrontNav.AttrU8(cell.AttrA3, 1) : 0;

            _paletteDecor?.HighlightMatch(it =>
            {
                if (_paletteMain != null && _paletteMain.Target == PaletteTarget.Decor)
                    return it.TerrainId == decorId && it.Variant == decorVar;
                if (_paletteMain != null && _paletteMain.Target == PaletteTarget.Main)
                    return it.TerrainId == mainId && it.Variant == mainVar;
                if (_paletteMain != null && _paletteMain.Target == PaletteTarget.Secondary)
                    return it.TerrainId == secId && it.Variant == secVar;
                return false;
            });

            string summary =
                "地质: " + climateName + "\r\n" +
                "主地形: " + LayerName(mainId) +
                "    次地形: " + LayerName(secId) +
                "    装饰: " + LayerName(decorId);
            if (lblTerrainLayers.Text != summary)
                lblTerrainLayers.Text = summary;
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

        private void UnitPropertyChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUnitUi) return;
            MapCell cell;
            if (_selectedOffMapUnit != null)
                cell = new MapCell { Unit = _selectedOffMapUnit };
            else
            {
                if (_selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
                cell = mapCanvas.Cells[_selectedCellIdx];
            }
            if (cell.Unit == null) return;

            ushort? uId = U16(nudUnitType);
            FrontNav.SetAgentU16(cell.Unit, 3, uId);
            if (uId.HasValue && GameSettings.Units.TryGetValue(uId.Value, out var unitName))
                lblUnitTypeName.Text = "兵种名称: " + unitName;
            else
                lblUnitTypeName.Text = "兵种名称: " + (uId.HasValue ? "未知兵种" : "无");

            FrontNav.SetAgentU16(cell.Unit, 1, U16(nudUnitFaction));
            FrontNav.SetAgentU16(cell.Unit, 2, U16(nudUnitAgentId));
            FrontNav.SetAgentU16(cell.Unit, 6, U16(nudUnitHp));
            FrontNav.SetAgentU16(cell.Unit, 7, U16(nudUnitMaxHp));

            int? level = (int?)nudUnitLevel.NullableValue;
            int? stack = (int?)nudUnitStack.NullableValue;
            FrontNav.SetAgentU16(cell.Unit, 4, (ushort)((level ?? 0) | ((stack ?? 0) << 8)));

            int? mobility = (int?)nudUnitMobility.NullableValue;
            int? direction = (int?)nudUnitDirection.NullableValue;
            FrontNav.SetAgentU16(cell.Unit, 5, (ushort)(((mobility ?? 0) << 8) | ((direction ?? 0) & 0xFF)));
            FrontNav.SetAgentU16(cell.Unit, 8, U16(nudUnitVal8));
            FrontNav.SetAgentU16(cell.Unit, 9, U16(nudUnitVal9));

            if (_selectedOffMapUnit != null && lvReinforceUnits.SelectedIndices.Count > 0)
            {
                int ri = lvReinforceUnits.SelectedIndices[0];
                if (ri >= 0 && ri < lvReinforceUnits.Items.Count)
                    lvReinforceUnits.Items[ri].Text = FrontNav.AgentU16(_selectedOffMapUnit, 2).ToString(CultureInfo.InvariantCulture);
            }

            SetOpt(cell.Unit, 2, "u8", U8(nudUnitPlayMode));
            SetOpt(cell.Unit, 5, "u8", U8(nudUnitAiTarget));
            SetOpt(cell.Unit, 6, "u8", U8(nudUnitFactionExtra));

            if (chkUnitBehavior.Checked)
            {
                var beh = FrontNav.EnsureTable(cell.Unit, 3);
                SetOpt(beh, 0, "u8", U8(nudUnitBehaviorField0));
                SetOpt(beh, 1, "u16", U16(nudUnitBehaviorId));
                SetOpt(beh, 2, "i16", I16(nudUnitBehaviorField2));
                SetOpt(beh, 3, "u8", U8(nudUnitBehaviorRadius));
                SetOpt(beh, 4, "u8", U8(nudUnitBehaviorField4));
                SetOpt(beh, 5, "u16", U16(nudUnitBehaviorCenter));
                SetOpt(beh, 6, "i16", I16(nudUnitBehaviorField6));
            }
            else FrontNav.SetChild(cell.Unit, 3, null);

            if (chkUnitGeneral.Checked)
            {
                int? genId = (int?)nudUnitGeneralId.NullableValue;
                lblUnitGeneralName.Text = "将领姓名: " + (genId.HasValue ? GameSettings.GetGeneralName(genId.Value) : "无");
                var gen = FrontNav.EnsureTable(cell.Unit, 11);
                SetOpt(gen, 0, "u16", (ushort)(genId ?? 0));
                if (chkUnitGeneralActive.Checked || FrontNav.Has(gen, 1))
                    SetOpt(gen, 1, "bool", chkUnitGeneralActive.Checked);
                if (nudUnitGeneralParam2.NullableValue != null || FrontNav.Has(gen, 2))
                    SetOpt(gen, 2, "u8", U8(nudUnitGeneralParam2) ?? 0);
            }
            else
            {
                lblUnitGeneralName.Text = "将领姓名: 无";
                FrontNav.SetChild(cell.Unit, 11, null);
            }

            if (chkUnitExArmy.Checked)
            {
                int? exArmyId = (int?)nudUnitExArmyId.NullableValue;
                if (exArmyId.HasValue && GameSettings.Units.TryGetValue(exArmyId.Value, out var exName))
                    lblUnitExArmyName.Text = "名称: " + exName;
                else
                    lblUnitExArmyName.Text = "名称: " + (exArmyId.HasValue ? "未知特种" : "无");
                var ex = FrontNav.EnsureTable(cell.Unit, 10);
                SetOpt(ex, 0, "u16", (ushort)(exArmyId ?? 0));
                SetOpt(ex, 1, "u8", U8(nudUnitExArmyField1));
                SetOpt(ex, 2, "u16", U16(nudUnitExArmyHp));
                SetOpt(ex, 3, "u16", U16(nudUnitExArmyMaxHp));
                SetOpt(ex, 4, "u16", U16(nudUnitExArmyField4));
                SetOpt(ex, 5, "u16", U16(nudUnitExArmyField5));
            }
            else
            {
                lblUnitExArmyName.Text = "名称: 无";
                FrontNav.SetChild(cell.Unit, 10, null);
            }

            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void AddDeleteUnitClick(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.Unit != null)
                cell.Unit = null;
            else
            {
                ushort uId = (ushort)(nudUnitType.NullableValue ?? 101);
                ushort agentId = (ushort)(nudUnitAgentId.NullableValue ?? new Random().Next(100, 999));
                nudUnitAgentId.NullableValue = agentId;
                int? level = (int?)nudUnitLevel.NullableValue;
                int? stack = (int?)nudUnitStack.NullableValue;
                ushort stackCount = (ushort)((level ?? 0) | ((stack ?? 0) << 8));
                int? mobility = (int?)nudUnitMobility.NullableValue;
                int? direction = (int?)nudUnitDirection.NullableValue;
                ushort val5 = (ushort)(((mobility ?? 0) << 8) | ((direction ?? 0) & 0xFF));
                cell.Unit = FrontNav.NewAgent(
                    (ushort)_selectedCellIdx,
                    (ushort)(nudUnitFaction.NullableValue ?? 0),
                    agentId, uId, stackCount, val5,
                    (ushort)(nudUnitHp.NullableValue ?? 0),
                    (ushort)(nudUnitMaxHp.NullableValue ?? 0));
            }
            CellSelectedClick(_selectedCellIdx);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void BuildingPropertyChanged(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0 || _isUpdatingBuildingUi) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.TriggerBldg == null) return;
            var detail = FrontNav.EnsureTable(cell.TriggerBldg, 3);
            BtlStruct data;
            if (detail.F.TryGetValue(0, out var n) && n is BtlStruct st) data = st;
            else
            {
                data = FrontNav.StructFromFbs("BuildingData");
                detail.F[0] = data;
            }
            FrontNav.SetMember(data, 1, U16(nudBldgType) ?? 0);
            FrontNav.SetMember(data, 3, U8(nudBldgOwner) ?? 0);
            FrontNav.SetMember(data, 0, U16(nudBldgFlag) ?? 0);
            FrontNav.SetMember(data, 2, U8(nudBldgExtraFlag) ?? 0);
            FrontNav.SetMember(data, 4, I8(nudBldgDx) ?? 0);
            FrontNav.SetMember(data, 5, I8(nudBldgDy) ?? 0);
            SetOpt(detail, 6, "u8", U8(nudBldgField6));
            if (U16(nudBldgType) is ushort bId)
                lblBldgTypeName.Text = "名: " + GameSettings.GetBuildingName(bId);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void AddDeleteBldgClick(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.TriggerBldg != null)
                cell.TriggerBldg = null;
            else
            {
                cell.TriggerBldg = FrontNav.NewBuildingEvent(
                    (ushort)_selectedCellIdx,
                    (ushort)(nudBldgType.NullableValue ?? 101),
                    (ushort)(nudBldgFlag.NullableValue ?? 1),
                    (byte)(nudBldgExtraFlag.NullableValue ?? 1),
                    U8(nudBldgOwner), I8(nudBldgDx), I8(nudBldgDy), U8(nudBldgField6));
            }
            CellSelectedClick(_selectedCellIdx);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void FortPropertyChanged(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0 || _isUpdatingFortUi) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.TriggerFort == null) return;
            var fort = FrontNav.EnsureTable(cell.TriggerFort, 4);
            SetOpt(fort, 0, "u8", U8(nudFortType) ?? 0);
            SetOpt(cell.TriggerFort, 1, "u16", U16(nudFortField1) ?? 0);
            SetOpt(fort, 3, "u8", U8(nudFortField3) ?? 0);
            if (U8(nudFortType) is byte fId)
                lblFortTypeName.Text = "工事名称: " + GameSettings.GetFortName(fId);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void AddDeleteFortClick(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.TriggerFort != null)
                cell.TriggerFort = null;
            else
            {
                cell.TriggerFort = FrontNav.NewFortEvent(
                    (ushort)_selectedCellIdx,
                    (byte)(nudFortType.NullableValue ?? 2),
                    (byte)(nudFortField3.NullableValue ?? 0));
                SetOpt(cell.TriggerFort, 1, "u16", U16(nudFortField1) ?? 0);
            }
            CellSelectedClick(_selectedCellIdx);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        void HookFactionEvents(bool attach)
        {
            EventHandler h = FactionPropertyChanged;
            if (attach)
            {
                nudFactionId.ValueChanged += h; nudFactionCamp.ValueChanged += h; nudFactionCountry.ValueChanged += h;
                nudFactionIsAI.ValueChanged += h; nudFactionVal5.ValueChanged += h; nudFactionAlign1.ValueChanged += h;
                nudFactionGold.ValueChanged += h; nudFactionTech.ValueChanged += h; nudFactionIncomeMod.ValueChanged += h;
                nudFactionDamageMod.ValueChanged += h; nudFactionHpMod.ValueChanged += h; nudFactionColorR.ValueChanged += h;
                nudFactionColorG.ValueChanged += h; nudFactionColorB.ValueChanged += h; nudFactionColorA.ValueChanged += h;
                nudFactionAlign2.ValueChanged += h; nudFactionConfigId.ValueChanged += h; nudFactionGeneralFlag.ValueChanged += h;
                nudFactionConfigRef.ValueChanged += h;
            }
            else
            {
                nudFactionId.ValueChanged -= h; nudFactionCamp.ValueChanged -= h; nudFactionCountry.ValueChanged -= h;
                nudFactionIsAI.ValueChanged -= h; nudFactionVal5.ValueChanged -= h; nudFactionAlign1.ValueChanged -= h;
                nudFactionGold.ValueChanged -= h; nudFactionTech.ValueChanged -= h; nudFactionIncomeMod.ValueChanged -= h;
                nudFactionDamageMod.ValueChanged -= h; nudFactionHpMod.ValueChanged -= h; nudFactionColorR.ValueChanged -= h;
                nudFactionColorG.ValueChanged -= h; nudFactionColorB.ValueChanged -= h; nudFactionColorA.ValueChanged -= h;
                nudFactionAlign2.ValueChanged -= h; nudFactionConfigId.ValueChanged -= h; nudFactionGeneralFlag.ValueChanged -= h;
                nudFactionConfigRef.ValueChanged -= h;
            }
        }

        private void SelectedFactionIndexChanged(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.FactionList(Doc));
            if (lbFactions.SelectedIndex < 0 || lbFactions.SelectedIndex >= list.Count) return;
            var faction = list[lbFactions.SelectedIndex];
            var info = FrontNav.ChildStruct(faction, 0);
            HookFactionEvents(false);

            nudFactionId.NullableValue = info == null ? null : (decimal?)FrontNav.MemberU16(info, 0);
            nudFactionCamp.NullableValue = info == null ? null : (decimal?)FrontNav.MemberI64(info, 2);
            int countryId = info == null ? 0 : (int)FrontNav.MemberU16(info, 1);
            nudFactionCountry.NullableValue = countryId;
            lblFactionCountryName.Text = GameSettings.GetCountryName(countryId);
            nudFactionIsAI.NullableValue = info == null ? null : (decimal?)FrontNav.MemberI64(info, 3);
            nudFactionVal5.NullableValue = info == null ? null : (decimal?)FrontNav.MemberI64(info, 4);
            nudFactionAlign1.NullableValue = info == null ? null : (decimal?)FrontNav.MemberI64(info, 5);
            nudFactionGold.NullableValue = info == null ? null : (decimal?)FrontNav.MemberU32(info, 6);
            nudFactionTech.NullableValue = info == null ? null : (decimal?)FrontNav.MemberU32(info, 7);
            nudFactionIncomeMod.NullableValue = info == null ? null : (decimal?)FrontNav.MemberF32(info, 8);
            nudFactionDamageMod.NullableValue = info == null ? null : (decimal?)FrontNav.MemberF32(info, 9);
            nudFactionHpMod.NullableValue = info == null ? null : (decimal?)FrontNav.MemberF32(info, 10);

            if (info != null && FrontNav.MemberExists(info, 11))
            {
                uint val = FrontNav.MemberU32(info, 11);
                nudFactionColorR.NullableValue = (val >> 24) & 0xFF;
                nudFactionColorG.NullableValue = (val >> 16) & 0xFF;
                nudFactionColorB.NullableValue = (val >> 8) & 0xFF;
                nudFactionColorA.NullableValue = val & 0xFF;
            }
            else
            {
                nudFactionColorR.NullableValue = null;
                nudFactionColorG.NullableValue = null;
                nudFactionColorB.NullableValue = null;
                nudFactionColorA.NullableValue = null;
            }

            nudFactionAlign2.NullableValue = info == null ? null : (decimal?)FrontNav.MemberU16(info, 12);
            nudFactionConfigId.NullableValue = info == null ? null : (decimal?)FrontNav.MemberU16(info, 13);
            nudFactionGeneralFlag.NullableValue = Dec(FrontNav.ScalarI64N(faction, 7));
            nudFactionConfigRef.NullableValue = Dec(FrontNav.ScalarI64N(faction, 8));
            HookFactionEvents(true);
        }

        private void FactionPropertyChanged(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.FactionList(Doc));
            if (lbFactions.SelectedIndex < 0 || lbFactions.SelectedIndex >= list.Count) return;
            var faction = list[lbFactions.SelectedIndex];
            var info = FrontNav.EnsureFactionInfo(faction);
            FrontNav.SetMember(info, 0, U16(nudFactionId) ?? 0);
            FrontNav.SetMember(info, 2, U8(nudFactionCamp) ?? 0);
            int cId = (int)(nudFactionCountry.NullableValue ?? 0);
            FrontNav.SetMember(info, 1, (ushort)cId);
            lblFactionCountryName.Text = GameSettings.GetCountryName(cId);
            FrontNav.SetMember(info, 3, U8(nudFactionIsAI) ?? 0);
            FrontNav.SetMember(info, 4, U8(nudFactionVal5) ?? 0);
            FrontNav.SetMember(info, 5, U8(nudFactionAlign1) ?? 0);
            FrontNav.SetMember(info, 6, U32(nudFactionGold) ?? 0);
            FrontNav.SetMember(info, 7, U32(nudFactionTech) ?? 0);
            FrontNav.SetMember(info, 8, F32(nudFactionIncomeMod) ?? 1f);
            FrontNav.SetMember(info, 9, F32(nudFactionDamageMod) ?? 1f);
            FrontNav.SetMember(info, 10, F32(nudFactionHpMod) ?? 1f);

            if (nudFactionColorR.NullableValue.HasValue || nudFactionColorG.NullableValue.HasValue
                || nudFactionColorB.NullableValue.HasValue || nudFactionColorA.NullableValue.HasValue)
            {
                uint r = (uint)(nudFactionColorR.NullableValue ?? 0);
                uint g = (uint)(nudFactionColorG.NullableValue ?? 0);
                uint b = (uint)(nudFactionColorB.NullableValue ?? 0);
                uint a = (uint)(nudFactionColorA.NullableValue ?? 255);
                FrontNav.SetMember(info, 11, (r << 24) | (g << 16) | (b << 8) | a);
            }

            FrontNav.SetMember(info, 12, U16(nudFactionAlign2) ?? 0);
            FrontNav.SetMember(info, 13, U16(nudFactionConfigId) ?? 0);
            SetOpt(faction, 7, "u8", U8(nudFactionGeneralFlag));
            SetOpt(faction, 8, "u16", U16(nudFactionConfigRef));

            lbFactions.Items[lbFactions.SelectedIndex] = $"势力 {FrontNav.MemberU16(info, 0)}: {GameSettings.GetCountryName(cId)}";
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        /// <summary>新建势力：FactionMetadata 成员按 fbs 填，倍率默认 1、颜色默认白。</summary>
        private void AddFactionClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            var vec = FactionVec();
            ushort newFactionId = 0;
            foreach (var f in FrontNav.TableItems(vec))
            {
                ushort id = FrontNav.MemberU16(FrontNav.ChildStruct(f, 0), 0);
                if (id >= newFactionId) newFactionId = (ushort)(id + 1);
            }
            var tbl = BtlFrontJson.NewTable();
            tbl.F[0] = FrontNav.StructFromFbs("FactionMetadata",
                newFactionId, (ushort)1, (byte)1, (byte)0, (byte)0, (byte)0,
                100u, 0u, 1f, 1f, 1f, 0xFFFFFFFFu, (ushort)0, (ushort)0);
            SetOpt(tbl, 7, "u8", (byte)1);
            SetOpt(tbl, 8, "u16", (ushort)1);
            vec.V.Add(tbl);

            var cards = FrontNav.EnsureVec(FrontNav.EnsureTable(Doc.Root, 4), 1, "table");
            var card = BtlFrontJson.NewTable();
            SetOpt(card, 0, "u16", newFactionId);
            card.F[1] = BtlFrontJson.NewVector("u16");
            cards.V.Add(card);

            var limits = FrontNav.EnsureVec(FrontNav.EnsureTable(Doc.Root, 4), 3, "table");
            var lim = BtlFrontJson.NewTable();
            SetOpt(lim, 0, "u16", newFactionId);
            lim.F[1] = BtlFrontJson.NewVector("u16");
            limits.V.Add(lim);

            OnDocumentLoaded();
            lbFactions.SelectedIndex = lbFactions.Items.Count - 1;
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void DeleteFactionClick(object sender, EventArgs e)
        {
            var vec = FrontNav.FactionList(Doc);
            int idx = lbFactions.SelectedIndex;
            if (idx < 0 || vec?.V == null || idx >= vec.V.Count) return;
            var faction = vec.V[idx] as BtlTable;
            ushort factionId = FrontNav.MemberU16(FrontNav.ChildStruct(faction, 0), 0);
            var confirm = MessageBox.Show($"确定要删除 势力 {factionId} 吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            FrontNav.RemoveAt(vec, idx);

            void DropById(BtlVector rel)
            {
                if (rel?.V == null) return;
                for (int i = rel.V.Count - 1; i >= 0; i--)
                {
                    if (rel.V[i] is BtlTable t && FrontNav.ScalarI64(t, 0) == factionId)
                        rel.V.RemoveAt(i);
                }
            }
            DropById(FrontNav.FactionCards(Doc));
            DropById(FrontNav.FactionLimits(Doc));

            OnDocumentLoaded();
            if (lbFactions.Items.Count > 0)
                lbFactions.SelectedIndex = Math.Min(idx, lbFactions.Items.Count - 1);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void LbFactionsDragDrop(object sender, DragEventArgs e)
        {
            Point point = lbFactions.PointToClient(new Point(e.X, e.Y));
            int index = lbFactions.IndexFromPoint(point);
            if (index < 0) index = lbFactions.Items.Count - 1;
            object data = e.Data.GetData(typeof(string));
            if (data == null) return;
            int oldIndex = lbFactions.Items.IndexOf(data);
            if (oldIndex < 0 || oldIndex == index) return;

            var vec = FrontNav.FactionList(Doc);
            if (vec?.V != null && oldIndex < vec.V.Count && index < vec.V.Count)
            {
                var faction = vec.V[oldIndex];
                vec.V.RemoveAt(oldIndex);
                vec.V.Insert(index, faction);
                ushort factionId = FrontNav.MemberU16(FrontNav.ChildStruct(faction as BtlTable, 0), 0);

                void MoveRel(BtlVector rel)
                {
                    if (rel?.V == null) return;
                    int found = -1;
                    for (int i = 0; i < rel.V.Count; i++)
                    {
                        if (rel.V[i] is BtlTable t && FrontNav.ScalarI64(t, 0) == factionId) { found = i; break; }
                    }
                    if (found < 0) return;
                    var entry = rel.V[found];
                    rel.V.RemoveAt(found);
                    rel.V.Insert(Math.Min(index, rel.V.Count), entry);
                }
                MoveRel(FrontNav.FactionCards(Doc));
                MoveRel(FrontNav.FactionLimits(Doc));
            }

            OnDocumentLoaded();
            lbFactions.SelectedIndex = index;
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void LvTargetsSelectedIndexChanged(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.Targets(Doc));
            if (lvTargets.SelectedIndices.Count == 0) return;
            int idx = lvTargets.SelectedIndices[0];
            if (idx < 0 || idx >= list.Count) return;
            var t = list[idx];
            nudTargetType.NullableValue = Dec(FrontNav.ScalarI64N(t, 0));
            nudTargetValue.NullableValue = Dec(FrontNav.ScalarI64N(t, 1));
            nudTargetParam1.NullableValue = Dec(FrontNav.ScalarI64N(t, 2));
            nudTargetParam2.NullableValue = Dec(FrontNav.ScalarI64N(t, 3));
            nudTargetFlag.NullableValue = Dec(FrontNav.ScalarI64N(t, 4));
        }

        private void AddTargetClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            var tbl = BtlFrontJson.NewTable();
            SetOpt(tbl, 0, "u16", U16(nudTargetType));
            SetOpt(tbl, 1, "i16", I16(nudTargetValue));
            SetOpt(tbl, 2, "u16", U16(nudTargetParam1));
            SetOpt(tbl, 3, "u16", U16(nudTargetParam2));
            SetOpt(tbl, 4, "u8", U8(nudTargetFlag));
            TargetVec().V.Add(tbl);
            OnDocumentLoaded();
            if (lvTargets.Items.Count > 0)
            {
                lvTargets.Items[lvTargets.Items.Count - 1].Selected = true;
                lvTargets.Items[lvTargets.Items.Count - 1].EnsureVisible();
            }
            AddHistoryState();
        }

        private void UpdateTargetClick(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.Targets(Doc));
            if (lvTargets.SelectedIndices.Count == 0 || list.Count == 0)
            {
                MessageBox.Show("请先在列表中选中一个目标条件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int idx = lvTargets.SelectedIndices[0];
            if (idx < 0 || idx >= list.Count) return;
            var t = list[idx];
            SetOpt(t, 0, "u16", U16(nudTargetType));
            SetOpt(t, 1, "i16", I16(nudTargetValue));
            SetOpt(t, 2, "u16", U16(nudTargetParam1));
            SetOpt(t, 3, "u16", U16(nudTargetParam2));
            SetOpt(t, 4, "u8", U8(nudTargetFlag));
            OnDocumentLoaded();
            if (idx < lvTargets.Items.Count)
            {
                lvTargets.Items[idx].Selected = true;
                lvTargets.Items[idx].EnsureVisible();
            }
            AddHistoryState();
        }

        private void DeleteTargetClick(object sender, EventArgs e)
        {
            var vec = FrontNav.Targets(Doc);
            if (lvTargets.SelectedIndices.Count == 0 || vec?.V == null)
            {
                MessageBox.Show("请先在列表中选中一个要删除的目标条件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int idx = lvTargets.SelectedIndices[0];
            if (idx < 0 || idx >= vec.V.Count) return;
            if (MessageBox.Show("确定要删除选中的目标条件吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            FrontNav.RemoveAt(vec, idx);
            OnDocumentLoaded();
            if (lvTargets.Items.Count > 0)
            {
                int nextSel = Math.Min(idx, lvTargets.Items.Count - 1);
                lvTargets.Items[nextSel].Selected = true;
                lvTargets.Items[nextSel].EnsureVisible();
            }
            AddHistoryState();
        }

        private void LvWeathersSelectedIndexChanged(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.Weathers(Doc));
            if (lvWeathers.SelectedIndices.Count == 0) return;
            int idx = lvWeathers.SelectedIndices[0];
            if (idx < 0 || idx >= list.Count) return;
            var w = list[idx];
            nudWeatherType.NullableValue = FrontNav.ScalarI64(w, 0);
            nudWeatherStart.NullableValue = FrontNav.ScalarI64(w, 1);
            nudWeatherDuration.NullableValue = FrontNav.ScalarI64(w, 2);
        }

        private void AddWeatherClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            var tbl = BtlFrontJson.NewTable();
            SetOpt(tbl, 0, "u8", U8(nudWeatherType) ?? 0);
            SetOpt(tbl, 1, "u16", U16(nudWeatherStart) ?? 0);
            SetOpt(tbl, 2, "u16", U16(nudWeatherDuration) ?? 0);
            WeatherVec().V.Add(tbl);
            OnDocumentLoaded();
            if (lvWeathers.Items.Count > 0)
            {
                lvWeathers.Items[lvWeathers.Items.Count - 1].Selected = true;
                lvWeathers.Items[lvWeathers.Items.Count - 1].EnsureVisible();
            }
            AddHistoryState();
        }

        private void UpdateWeatherClick(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.Weathers(Doc));
            if (lvWeathers.SelectedIndices.Count == 0 || list.Count == 0)
            {
                MessageBox.Show("请先在列表中选中一个天气配置！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int idx = lvWeathers.SelectedIndices[0];
            if (idx < 0 || idx >= list.Count) return;
            var w = list[idx];
            SetOpt(w, 0, "u8", U8(nudWeatherType) ?? 0);
            SetOpt(w, 1, "u16", U16(nudWeatherStart) ?? 0);
            SetOpt(w, 2, "u16", U16(nudWeatherDuration) ?? 0);
            OnDocumentLoaded();
            if (idx < lvWeathers.Items.Count)
            {
                lvWeathers.Items[idx].Selected = true;
                lvWeathers.Items[idx].EnsureVisible();
            }
            AddHistoryState();
        }

        private void DeleteWeatherClick(object sender, EventArgs e)
        {
            var vec = FrontNav.Weathers(Doc);
            if (lvWeathers.SelectedIndices.Count == 0 || vec?.V == null)
            {
                MessageBox.Show("请先在列表中选中一个要删除的天气配置！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int idx = lvWeathers.SelectedIndices[0];
            if (idx < 0 || idx >= vec.V.Count) return;
            if (MessageBox.Show("确定要删除选中的天气配置吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            FrontNav.RemoveAt(vec, idx);
            OnDocumentLoaded();
            if (lvWeathers.Items.Count > 0)
            {
                int nextSel = Math.Min(idx, lvWeathers.Items.Count - 1);
                lvWeathers.Items[nextSel].Selected = true;
                lvWeathers.Items[nextSel].EnsureVisible();
            }
            AddHistoryState();
        }

        private void LvReinforcesSelectedIndexChanged(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.ReinforcePoints(Doc));
            if (lvReinforces.SelectedIndices.Count == 0) return;
            int idx = lvReinforces.SelectedIndices[0];
            if (idx < 0 || idx >= list.Count) return;
            var rp = list[idx];
            nudRpCellIdx.NullableValue = Dec(FrontNav.ScalarI64N(rp, 0));
            nudRpFactionId.NullableValue = Dec(FrontNav.ScalarI64N(rp, 1));
            chkRpIsKey.Checked = FrontNav.AsBool(FrontNav.ScalarV(rp, 2));
            nudRpFlag.NullableValue = Dec(FrontNav.ScalarI64N(rp, 3));
        }

        private void AddReinforceClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            var tbl = BtlFrontJson.NewTable();
            SetOpt(tbl, 0, "u16", U16(nudRpCellIdx));
            SetOpt(tbl, 1, "u8", U8(nudRpFactionId));
            SetOpt(tbl, 2, "bool", chkRpIsKey.Checked);
            SetOpt(tbl, 3, "u8", U8(nudRpFlag));
            ReinforceVec().V.Add(tbl);
            OnDocumentLoaded();
            if (lvReinforces.Items.Count > 0)
            {
                lvReinforces.Items[lvReinforces.Items.Count - 1].Selected = true;
                lvReinforces.Items[lvReinforces.Items.Count - 1].EnsureVisible();
            }
            AddHistoryState();
        }

        private void UpdateReinforceClick(object sender, EventArgs e)
        {
            var list = FrontNav.TableItems(FrontNav.ReinforcePoints(Doc));
            if (lvReinforces.SelectedIndices.Count == 0 || list.Count == 0)
            {
                MessageBox.Show("请先在列表中选中一个增兵部署点！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int idx = lvReinforces.SelectedIndices[0];
            if (idx < 0 || idx >= list.Count) return;
            var rp = list[idx];
            SetOpt(rp, 0, "u16", U16(nudRpCellIdx));
            SetOpt(rp, 1, "u8", U8(nudRpFactionId));
            SetOpt(rp, 2, "bool", chkRpIsKey.Checked);
            SetOpt(rp, 3, "u8", U8(nudRpFlag));
            OnDocumentLoaded();
            if (idx < lvReinforces.Items.Count)
            {
                lvReinforces.Items[idx].Selected = true;
                lvReinforces.Items[idx].EnsureVisible();
            }
            AddHistoryState();
        }

        private void DeleteReinforceClick(object sender, EventArgs e)
        {
            var vec = FrontNav.ReinforcePoints(Doc);
            if (lvReinforces.SelectedIndices.Count == 0 || vec?.V == null)
            {
                MessageBox.Show("请先在列表中选中一个要删除的增兵部署点！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int idx = lvReinforces.SelectedIndices[0];
            if (idx < 0 || idx >= vec.V.Count) return;
            if (MessageBox.Show("确定要删除选中的增兵部署点吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            FrontNav.RemoveAt(vec, idx);
            OnDocumentLoaded();
            if (lvReinforces.Items.Count > 0)
            {
                int nextSel = Math.Min(idx, lvReinforces.Items.Count - 1);
                lvReinforces.Items[nextSel].Selected = true;
                lvReinforces.Items[nextSel].EnsureVisible();
            }
            AddHistoryState();
        }

        private void ResizeMapClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            var size = FrontNav.MapSize(Doc);
            if (size == null) return;
            ushort oldW = FrontNav.MemberU16(size, 0);
            ushort oldH = FrontNav.MemberU16(size, 1);
            int expandLeft = (int)nudExpandLeft.Value;
            int expandRight = (int)nudExpandRight.Value;
            int expandUp = (int)nudExpandUp.Value;
            int expandDown = (int)nudExpandDown.Value;
            int newW = oldW + expandLeft + expandRight;
            int newH = oldH + expandUp + expandDown;
            if (expandLeft == 0 && expandRight == 0 && expandUp == 0 && expandDown == 0)
            {
                MessageBox.Show("请至少填写一个方向的扩展格数！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (newW > 500 || newH > 500)
            {
                MessageBox.Show($"扩展后地图尺寸为 {newW}x{newH}，宽高均不能超过 500！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show($"确定要将地图从 {oldW}x{oldH} 扩展为 {newW}x{newH} 吗？\n将分别向左、右、上、下扩展 {expandLeft}、{expandRight}、{expandUp}、{expandDown} 格，新地块将被初始化为平地。", "确认扩展", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            try
            {
                Cursor = Cursors.WaitCursor;
                FrontNav.SyncGrid(Doc, mapCanvas.Cells);
                var cells = FrontNav.RebuildCells(Doc);
                var newCells = new List<MapCell>();
                for (int idx = 0; idx < newW * newH; idx++)
                {
                    int x = idx % newW;
                    int y = idx / newW;
                    int oldX = x - expandLeft;
                    int oldY = y - expandUp;
                    if (oldX >= 0 && oldX < oldW && oldY >= 0 && oldY < oldH)
                    {
                        var oldCell = cells[oldY * oldW + oldX];
                        oldCell.Index = idx;
                        oldCell.X = x;
                        oldCell.Y = y;
                        newCells.Add(oldCell);
                    }
                    else
                    {
                        newCells.Add(new MapCell
                        {
                            Index = idx, X = x, Y = y, Terrain = 0,
                            Attr = FrontNav.NewAttr(0, 0, 0, 0)
                        });
                    }
                }

                FrontNav.SetMember(size, 0, (ushort)newW);
                FrontNav.SetMember(size, 1, (ushort)newH);
                ushort lm = FrontNav.MemberU16(size, 2);
                ushort tm = FrontNav.MemberU16(size, 3);
                ushort pw = FrontNav.MemberU16(size, 4);
                ushort ph = FrontNav.MemberU16(size, 5);
                FrontNav.SetMember(size, 4, (ushort)Math.Min(pw, Math.Max(0, newW - lm)));
                FrontNav.SetMember(size, 5, (ushort)Math.Min(ph, Math.Max(0, newH - tm)));

                foreach (var ev in FrontNav.TableItems(FrontNav.Events(Doc)))
                {
                    if (FrontNav.Child(ev, 3) != null || FrontNav.Child(ev, 4) != null) continue;
                    var mapped = FrontNav.RemapCellIndex((int)FrontNav.ScalarI64(ev, 0), oldW, expandLeft, expandUp, newW, newH);
                    if (mapped != null) SetOpt(ev, 0, "u16", mapped.Value);
                }
                var keptRp = new List<object>();
                foreach (var rp in FrontNav.TableItems(FrontNav.ReinforcePoints(Doc)))
                {
                    var mapped = FrontNav.RemapCellIndex((int)FrontNav.ScalarI64(rp, 0), oldW, expandLeft, expandUp, newW, newH);
                    if (mapped == null) continue;
                    SetOpt(rp, 0, "u16", mapped.Value);
                    keptRp.Add(rp);
                }
                var rpVec = FrontNav.ReinforcePoints(Doc);
                if (rpVec != null)
                {
                    rpVec.V.Clear();
                    foreach (var rp in keptRp) rpVec.V.Add(rp);
                }
                foreach (var sub in FrontNav.TableItems(FrontNav.SubRegions(Doc)))
                {
                    foreach (var kv in sub.F.ToList())
                    {
                        if (kv.Value is not BtlVector tiles || tiles.Elem != "u16") continue;
                        var mappedTiles = new List<ushort>();
                        foreach (var raw in FrontNav.U16Items(tiles))
                        {
                            var mapped = FrontNav.RemapCellIndex(raw, oldW, expandLeft, expandUp, newW, newH);
                            if (mapped != null) mappedTiles.Add(mapped.Value);
                        }
                        FrontNav.SetU16Items(tiles, mappedTiles);
                    }
                }

                FrontNav.SyncGrid(Doc, newCells);
                mapCanvas.Document = Doc;
                AddHistoryState();
                _selectedCellIdx = -1;
                lblCellCoords.Text = "未选中地块";
                lblCellCoordsUnit.Text = "未选中地块";
                lblCellCoordsBuilding.Text = "未选中地块";
                nudPlayWidth.Value = FrontNav.MemberU16(size, 4);
                nudPlayHeight.Value = FrontNav.MemberU16(size, 5);
                nudMapW.Value = newW;
                nudMapH.Value = newH;
                nudExpandLeft.Value = 0;
                nudExpandRight.Value = 0;
                nudExpandUp.Value = 0;
                nudExpandDown.Value = 0;
                MessageBox.Show($"地图尺寸成功扩展为: {newW}x{newH}！\n已自动重映射全部部队、地标及事件地块编号。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"调整尺寸失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private ushort GetNextUniqueAgentId()
        {
            ushort maxId = 0;
            foreach (var agent in FrontNav.TableItems(FrontNav.Agents(Doc)))
            {
                ushort id = FrontNav.AgentU16(agent, 2);
                if (id > maxId) maxId = id;
            }
            if (mapCanvas.Cells != null)
            {
                foreach (var cell in mapCanvas.Cells)
                {
                    if (cell.Unit == null) continue;
                    ushort id = FrontNav.AgentU16(cell.Unit, 2);
                    if (id > maxId) maxId = id;
                }
            }
            return (ushort)(maxId + 1);
        }

        BtlTable CloneAgent(BtlTable src) => FrontNav.CloneTable(src);
        BtlTable CloneEvent(BtlTable src) => FrontNav.CloneTable(src);

        private void PerformCopyAction()
        {
            if (_selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (tabControlRight.SelectedTab == tabTerrainEdit)
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
            _copiedUnit = cell.Unit != null ? CloneAgent(cell.Unit) : null;
            _copiedTriggerBldg = CloneEvent(cell.TriggerBldg);
            _copiedTriggerFort = CloneEvent(cell.TriggerFort);
            statusLabel.Text = $"复制成功：已将地块 #{_selectedCellIdx} 的完整数据（地形/部队/建筑/工事）存入画笔/剪贴板";
        }

        void PasteTerrainOnto(MapCell cell)
        {
            if (!_copiedTerrain.HasValue) return;
            cell.Terrain = _copiedTerrain.Value;
            cell.Attr = FrontNav.CloneStruct(_copiedAttr);
            cell.AttrA2 = FrontNav.CloneStruct(_copiedAttrA2);
            cell.AttrA3 = FrontNav.CloneStruct(_copiedAttrA3);
        }

        void PasteUnitOnto(MapCell cell, int cellIdx)
        {
            if (_copiedUnit == null) { cell.Unit = null; return; }
            cell.Unit = CloneAgent(_copiedUnit);
            FrontNav.SetAgentU16(cell.Unit, 0, (ushort)cellIdx);
            FrontNav.SetAgentU16(cell.Unit, 2, GetNextUniqueAgentId());
        }

        void PasteLandmarksOnto(MapCell cell, int cellIdx)
        {
            if (_copiedTriggerBldg != null)
            {
                cell.TriggerBldg = CloneEvent(_copiedTriggerBldg);
                SetOpt(cell.TriggerBldg, 0, "u16", (ushort)cellIdx);
            }
            else cell.TriggerBldg = null;
            if (_copiedTriggerFort != null)
            {
                cell.TriggerFort = CloneEvent(_copiedTriggerFort);
                SetOpt(cell.TriggerFort, 0, "u16", (ushort)cellIdx);
            }
            else cell.TriggerFort = null;
        }

        private void PerformPasteAction()
        {
            if (_selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (tabControlRight.SelectedTab == tabTerrainEdit)
            {
                if (_terrainBrush != null)
                {
                    ApplyPaletteToCell(_selectedCellIdx, _terrainBrush.Item, _terrainBrush.Target, recordHistory: false);
                    CellSelectedClick(_selectedCellIdx);
                    mapCanvas.Invalidate();
                    statusLabel.Text = $"已将「{_terrainBrush.Item.Label}」应用到地块 #{_selectedCellIdx}";
                }
                else if (_copiedTerrain.HasValue)
                {
                    PasteTerrainOnto(cell);
                    CellSelectedClick(_selectedCellIdx);
                    mapCanvas.Invalidate();
                    statusLabel.Text = $"粘贴成功：已将地形与属性粘贴至地块 #{_selectedCellIdx}";
                }
                else statusLabel.Text = "粘贴失败：剪贴板中无地形数据";
            }
            else if (tabControlRight.SelectedTab == tabUnitEdit)
            {
                PasteUnitOnto(cell, _selectedCellIdx);
                CellSelectedClick(_selectedCellIdx);
                mapCanvas.Invalidate();
                statusLabel.Text = $"粘贴成功：已将部队数据粘贴至地块 #{_selectedCellIdx}";
            }
            else if (tabControlRight.SelectedTab == tabBuildingEdit)
            {
                PasteLandmarksOnto(cell, _selectedCellIdx);
                CellSelectedClick(_selectedCellIdx);
                mapCanvas.Invalidate();
                statusLabel.Text = $"粘贴成功：已将地标建筑与工事数据粘贴至地块 #{_selectedCellIdx}";
            }
            AddHistoryState();
        }

        private void PaintCell(int cellIdx)
        {
            if (cellIdx < 0 || cellIdx >= mapCanvas.Cells.Count) return;
            var cell = mapCanvas.Cells[cellIdx];
            if (tabControlRight.SelectedTab == tabTerrainEdit)
            {
                if (_terrainBrush != null)
                    ApplyPaletteToCell(cellIdx, _terrainBrush.Item, _terrainBrush.Target, recordHistory: false);
                else if (_copiedTerrain.HasValue)
                    PasteTerrainOnto(cell);
            }
            else if (tabControlRight.SelectedTab == tabUnitEdit)
                PasteUnitOnto(cell, cellIdx);
            else if (tabControlRight.SelectedTab == tabBuildingEdit)
                PasteLandmarksOnto(cell, cellIdx);
        }
    }
}
