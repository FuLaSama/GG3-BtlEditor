using System.Globalization;
using BtlCore.Front;
using BtlCore.Scripting;
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
            if (lvTargets != null)
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
            }

            if (lvReinforces != null)
            {
            lvReinforces.Items.Clear();
            foreach (var rp in FrontNav.TableItems(FrontNav.ReinforcePoints(Doc)))
            {
                var item = new ListViewItem(FrontNav.ScalarI64N(rp, 0)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.ScalarI64N(rp, 1)?.ToString() ?? "");
                item.SubItems.Add(FrontNav.AsBool(FrontNav.ScalarV(rp, 2)) ? "是" : "否");
                item.SubItems.Add(FrontNav.ScalarI64N(rp, 3)?.ToString() ?? "");
                lvReinforces.Items.Add(item);
            }
            }

            if (lvWeathers != null)
            {
            lvWeathers.Items.Clear();
            foreach (var w in FrontNav.TableItems(FrontNav.Weathers(Doc)))
            {
                var item = new ListViewItem(FrontNav.ScalarI64(w, 0).ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(FrontNav.ScalarI64(w, 1).ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(FrontNav.ScalarI64(w, 2).ToString(CultureInfo.InvariantCulture));
                lvWeathers.Items.Add(item);
            }
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
            if (!RunEdit("add_route", new ScriptArgs
            {
                Input = EditInput(
                    ("flag", nudAiBehaviorActionFlag.NullableValue == null ? null : (ushort)nudAiBehaviorActionFlag.NullableValue.Value),
                    ("cells", EditList(ParseTargetCellsInput(txtAiBehaviorTargetCells.Text))))
            })) return;
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
            if (!RunEdit("set_route", new ScriptArgs
            {
                ObjectPath = "Root.ai_info.behaviors",
                ObjectIndex = selIdx,
                Input = EditInput(
                    ("flag", nudAiBehaviorActionFlag.NullableValue == null ? null : (ushort)nudAiBehaviorActionFlag.NullableValue.Value),
                    ("cells", EditList(ParseTargetCellsInput(txtAiBehaviorTargetCells.Text))))
            })) return;
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
            if (!RunEdit("delete_route", new ScriptArgs { Index = selIdx })) return;
            RefreshAiBehaviorsUi();
            if (lvFactionAiBehaviors.Items.Count > 0)
            {
                int nextSel = Math.Min(selIdx, lvFactionAiBehaviors.Items.Count - 1);
                lvFactionAiBehaviors.Items[nextSel].Selected = true;
                lvFactionAiBehaviors.Items[nextSel].EnsureVisible();
            }
            AddHistoryState();
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
            if (!RunEdit("set_offset", new ScriptArgs
            {
                CellIndex = _selectedCellIdx,
                Input = EditInput(
                    ("layer", TerrainLayerKey(target)),
                    ("dx", trkTerrainDx.Value),
                    ("dy", trkTerrainDy.Value))
            })) return;
            cell = mapCanvas.Cells[_selectedCellIdx];
            var attr = GetLayerAttr(cell, target);
            lblTerrainDxVal.Text = (attr == null ? 0 : FrontNav.AttrI8(attr, 2)).ToString();
            lblTerrainDyVal.Text = (attr == null ? 0 : FrontNav.AttrI8(attr, 3)).ToString();
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
            RefreshTerrainOffsetUi(mapCanvas.Cells[_selectedCellIdx]);
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
            int? genId = (int?)nudUnitGeneralId.NullableValue;
            int? exArmyId = (int?)nudUnitExArmyId.NullableValue;
            int index = EditIndex(FrontNav.Agents(Doc), cell.Unit);
            if (index < 0) return;
            if (!RunEdit("apply_unit", new ScriptArgs
            {
                ObjectPath = "Root.ai_info.agents",
                ObjectIndex = index,
                Input = EditInput(
                    ("faction", U16(nudUnitFaction)),
                    ("agent", U16(nudUnitAgentId)),
                    ("unit", uId),
                    ("hp", U16(nudUnitHp)),
                    ("max_hp", U16(nudUnitMaxHp)),
                    ("level", (int?)nudUnitLevel.NullableValue),
                    ("stack", (int?)nudUnitStack.NullableValue),
                    ("mobility", (int?)nudUnitMobility.NullableValue),
                    ("direction", (int?)nudUnitDirection.NullableValue),
                    ("val8", U16(nudUnitVal8)),
                    ("val9", U16(nudUnitVal9)),
                    ("play_mode", U8(nudUnitPlayMode)),
                    ("ai_target", U8(nudUnitAiTarget)),
                    ("faction_extra", U8(nudUnitFactionExtra)),
                    ("behavior", chkUnitBehavior.Checked),
                    ("behavior_0", U8(nudUnitBehaviorField0)),
                    ("behavior_id", U16(nudUnitBehaviorId)),
                    ("behavior_2", I16(nudUnitBehaviorField2)),
                    ("behavior_radius", U8(nudUnitBehaviorRadius)),
                    ("behavior_4", U8(nudUnitBehaviorField4)),
                    ("behavior_center", U16(nudUnitBehaviorCenter)),
                    ("behavior_6", I16(nudUnitBehaviorField6)),
                    ("general", chkUnitGeneral.Checked),
                    ("general_id", genId),
                    ("general_active", chkUnitGeneralActive.Checked),
                    ("general_param2", U8(nudUnitGeneralParam2)),
                    ("ex", chkUnitExArmy.Checked),
                    ("ex_id", exArmyId),
                    ("ex_1", U8(nudUnitExArmyField1)),
                    ("ex_hp", U16(nudUnitExArmyHp)),
                    ("ex_max_hp", U16(nudUnitExArmyMaxHp)),
                    ("ex_4", U16(nudUnitExArmyField4)),
                    ("ex_5", U16(nudUnitExArmyField5)))
            })) return;

            if (uId.HasValue && GameSettings.Units.TryGetValue(uId.Value, out var unitName))
                lblUnitTypeName.Text = "兵种名称: " + unitName;
            else
                lblUnitTypeName.Text = "兵种名称: " + (uId.HasValue ? "未知兵种" : "无");

            if (_selectedOffMapUnit != null && lvReinforceUnits.SelectedIndices.Count > 0)
            {
                int ri = lvReinforceUnits.SelectedIndices[0];
                if (ri >= 0 && ri < lvReinforceUnits.Items.Count)
                    lvReinforceUnits.Items[ri].Text = FrontNav.AgentU16(_selectedOffMapUnit, 2).ToString(CultureInfo.InvariantCulture);
            }

            if (chkUnitGeneral.Checked)
                lblUnitGeneralName.Text = "将领姓名: " + (genId.HasValue ? GameSettings.GetGeneralName(genId.Value) : "无");
            else
                lblUnitGeneralName.Text = "将领姓名: 无";

            if (chkUnitExArmy.Checked)
            {
                if (exArmyId.HasValue && GameSettings.Units.TryGetValue(exArmyId.Value, out var exName))
                    lblUnitExArmyName.Text = "名称: " + exName;
                else
                    lblUnitExArmyName.Text = "名称: " + (exArmyId.HasValue ? "未知特种" : "无");
            }
            else
                lblUnitExArmyName.Text = "名称: 无";

            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void AddDeleteUnitClick(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.Unit != null)
            {
                if (!RunEdit("delete_unit", new ScriptArgs { CellIndex = _selectedCellIdx })) return;
            }
            else
            {
                ushort uId = (ushort)(nudUnitType.NullableValue ?? 101);
                ushort agentId = (ushort)(nudUnitAgentId.NullableValue ?? new Random().Next(100, 999));
                nudUnitAgentId.NullableValue = agentId;
                if (!RunEdit("place_unit", new ScriptArgs
                {
                    CellIndex = _selectedCellIdx,
                    Input = EditInput(
                        ("faction", (ushort)(nudUnitFaction.NullableValue ?? 0)),
                        ("agent", agentId),
                        ("unit", uId),
                        ("level", (int?)nudUnitLevel.NullableValue),
                        ("stack", (int?)nudUnitStack.NullableValue),
                        ("mobility", (int?)nudUnitMobility.NullableValue),
                        ("direction", (int?)nudUnitDirection.NullableValue),
                        ("hp", U16(nudUnitHp)),
                        ("max_hp", U16(nudUnitMaxHp)))
                })) return;
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
            int index = EditIndex(FrontNav.Events(Doc), cell.TriggerBldg);
            if (index < 0) return;
            if (!RunEdit("apply_building", new ScriptArgs
            {
                ObjectPath = "Root.trigger_info.events",
                ObjectIndex = index,
                Input = EditInput(
                    ("type", U16(nudBldgType)),
                    ("owner", U8(nudBldgOwner)),
                    ("flag", U16(nudBldgFlag)),
                    ("extra", U8(nudBldgExtraFlag)),
                    ("dx", I8(nudBldgDx)),
                    ("dy", I8(nudBldgDy)),
                    ("field6", U8(nudBldgField6)))
            })) return;
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
            {
                int index = EditIndex(FrontNav.Events(Doc), cell.TriggerBldg);
                if (index < 0 || !RunEdit("delete_event", new ScriptArgs { Index = index })) return;
            }
            else if (!RunEdit("place_building", new ScriptArgs
            {
                CellIndex = _selectedCellIdx,
                Input = EditInput(
                    ("type", (ushort)(nudBldgType.NullableValue ?? 101)),
                    ("flag", (ushort)(nudBldgFlag.NullableValue ?? 1)),
                    ("extra", (byte)(nudBldgExtraFlag.NullableValue ?? 1)),
                    ("owner", U8(nudBldgOwner)),
                    ("dx", I8(nudBldgDx)),
                    ("dy", I8(nudBldgDy)),
                    ("field6", U8(nudBldgField6)))
            })) return;
            CellSelectedClick(_selectedCellIdx);
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        private void FortPropertyChanged(object sender, EventArgs e)
        {
            if (_selectedCellIdx < 0 || _isUpdatingFortUi) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            if (cell.TriggerFort == null) return;
            int index = EditIndex(FrontNav.Events(Doc), cell.TriggerFort);
            if (index < 0) return;
            if (!RunEdit("apply_fort", new ScriptArgs
            {
                ObjectPath = "Root.trigger_info.events",
                ObjectIndex = index,
                Input = EditInput(
                    ("fort_id", U8(nudFortType)),
                    ("field1", U16(nudFortField1)),
                    ("field3", U8(nudFortField3)))
            })) return;
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
            {
                int index = EditIndex(FrontNav.Events(Doc), cell.TriggerFort);
                if (index < 0 || !RunEdit("delete_event", new ScriptArgs { Index = index })) return;
            }
            else if (!RunEdit("place_fort", new ScriptArgs
            {
                CellIndex = _selectedCellIdx,
                Input = EditInput(
                    ("fort_id", (byte)(nudFortType.NullableValue ?? 2)),
                    ("field3", (byte)(nudFortField3.NullableValue ?? 0)),
                    ("field1", U16(nudFortField1) ?? (ushort)0))
            })) return;
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
            int cId = (int)(nudFactionCountry.NullableValue ?? 0);
            lblFactionCountryName.Text = GameSettings.GetCountryName(cId);
            if (!RunEdit("apply_faction", new ScriptArgs
            {
                ObjectPath = "Root.faction_info.factions",
                ObjectIndex = lbFactions.SelectedIndex,
                Input = EditInput(
                    ("id", U16(nudFactionId)),
                    ("camp", U8(nudFactionCamp)),
                    ("country", cId),
                    ("is_ai", U8(nudFactionIsAI)),
                    ("val5", U8(nudFactionVal5)),
                    ("align1", U8(nudFactionAlign1)),
                    ("gold", U32(nudFactionGold)),
                    ("tech", U32(nudFactionTech)),
                    ("income", F32(nudFactionIncomeMod)),
                    ("damage", F32(nudFactionDamageMod)),
                    ("hp", F32(nudFactionHpMod)),
                    ("r", nudFactionColorR.NullableValue.HasValue ? (byte)nudFactionColorR.NullableValue.Value : null),
                    ("g", nudFactionColorG.NullableValue.HasValue ? (byte)nudFactionColorG.NullableValue.Value : null),
                    ("b", nudFactionColorB.NullableValue.HasValue ? (byte)nudFactionColorB.NullableValue.Value : null),
                    ("a", nudFactionColorA.NullableValue.HasValue ? (byte)nudFactionColorA.NullableValue.Value : null),
                    ("align2", U16(nudFactionAlign2)),
                    ("config_id", U16(nudFactionConfigId)),
                    ("general_flag", U8(nudFactionGeneralFlag)),
                    ("config_ref", U16(nudFactionConfigRef)))
            })) return;
            var faction = FrontNav.TableItems(FrontNav.FactionList(Doc))[lbFactions.SelectedIndex];
            var info = FrontNav.ChildStruct(faction, 0);

            lbFactions.Items[lbFactions.SelectedIndex] = $"势力 {FrontNav.MemberU16(info, 0)}: {GameSettings.GetCountryName(cId)}";
            mapCanvas.Invalidate();
            AddHistoryState();
        }

        /// <summary>新建势力：FactionMetadata 成员按 fbs 填，倍率默认 1、颜色默认白。</summary>
        private void AddFactionClick(object sender, EventArgs e)
        {
            if (!HasDoc) return;
            if (!RunEdit("add_faction", new ScriptArgs())) return;

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
            if (!RunEdit("delete_faction", new ScriptArgs { Index = idx })) return;

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
                if (!RunEdit("move_faction", new ScriptArgs
                {
                    Input = EditInput(("from", oldIndex), ("to", index))
                })) return;

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
            if (!RunEdit("add_target", new ScriptArgs
            {
                Input = EditInput(
                    ("type", U16(nudTargetType)),
                    ("value", I16(nudTargetValue)),
                    ("param1", U16(nudTargetParam1)),
                    ("param2", U16(nudTargetParam2)),
                    ("flag", U8(nudTargetFlag)))
            })) return;
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
            if (!RunEdit("update_target", new ScriptArgs
            {
                ObjectPath = "Root.stage_metadata.targets",
                ObjectIndex = idx,
                Input = EditInput(
                    ("type", U16(nudTargetType)),
                    ("value", I16(nudTargetValue)),
                    ("param1", U16(nudTargetParam1)),
                    ("param2", U16(nudTargetParam2)),
                    ("flag", U8(nudTargetFlag)))
            })) return;
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
            if (!RunEdit("delete_target", new ScriptArgs { Index = idx })) return;
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
            if (!RunEdit("add_weather", new ScriptArgs
            {
                Input = EditInput(
                    ("type", U8(nudWeatherType)),
                    ("start", U16(nudWeatherStart)),
                    ("duration", U16(nudWeatherDuration)))
            })) return;
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
            if (!RunEdit("update_weather", new ScriptArgs
            {
                ObjectPath = "Root.decal_info.decals",
                ObjectIndex = idx,
                Input = EditInput(
                    ("type", U8(nudWeatherType)),
                    ("start", U16(nudWeatherStart)),
                    ("duration", U16(nudWeatherDuration)))
            })) return;
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
            if (!RunEdit("delete_weather", new ScriptArgs { Index = idx })) return;
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
            if (!RunEdit("add_reinforce", new ScriptArgs
            {
                Input = EditInput(
                    ("cell", U16(nudRpCellIdx)),
                    ("faction", U8(nudRpFactionId)),
                    ("is_key", chkRpIsKey.Checked),
                    ("flag", U8(nudRpFlag)))
            })) return;
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
            if (!RunEdit("update_reinforce", new ScriptArgs
            {
                ObjectPath = "Root.battle_info.reinforce_points",
                ObjectIndex = idx,
                Input = EditInput(
                    ("cell", U16(nudRpCellIdx)),
                    ("faction", U8(nudRpFactionId)),
                    ("is_key", chkRpIsKey.Checked),
                    ("flag", U8(nudRpFlag)))
            })) return;
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
            if (!RunEdit("delete_reinforce", new ScriptArgs { Index = idx })) return;
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
                if (!RunEdit("resize_map", new ScriptArgs
                {
                    Input = EditInput(
                        ("left", expandLeft),
                        ("right", expandRight),
                        ("up", expandUp),
                        ("down", expandDown))
                })) return;
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
            if (tabControlRight.SelectedTab == tabTerrainEdit)
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
            else if (tabControlRight.SelectedTab == tabUnitEdit)
            {
                if (!PasteUnitOnto(_selectedCellIdx)) return;
                CellSelectedClick(_selectedCellIdx);
                mapCanvas.Invalidate();
                statusLabel.Text = $"粘贴成功：已将部队数据粘贴至地块 #{_selectedCellIdx}";
            }
            else if (tabControlRight.SelectedTab == tabBuildingEdit)
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
            if (tabControlRight.SelectedTab == tabTerrainEdit)
            {
                if (_terrainBrush != null)
                    ApplyPaletteToCell(cellIdx, _terrainBrush.Item, _terrainBrush.Target, recordHistory: false);
                else if (_copiedTerrain.HasValue)
                    PasteTerrainOnto(cellIdx);
            }
            else if (tabControlRight.SelectedTab == tabUnitEdit)
                PasteUnitOnto(cellIdx);
            else if (tabControlRight.SelectedTab == tabBuildingEdit)
                PasteLandmarksOnto(cellIdx);
        }
    }
}
