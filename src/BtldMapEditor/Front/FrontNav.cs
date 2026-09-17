/*
 * FrontNav.cs
 *
 * 编辑器读写 BtlFront 的入口。画布、右侧面板、行为树页都通过这里碰 Document。
 *
 * 约定：
 *   - 文档仍是 table/struct/vector，没有 StageModel。下面 Map()/Agents() 只是 field id 快捷方式
 *     （数字来自 battle.fbs 的 Root/MapTerrain/AIInfo…），方便 WinForms 绑定，不是第二份数据。
 *   - 出现过的槽保留（包括值 0）；用户没改过的缺省槽不要用 0 填进去。
 *   - SetScalar(value:null) 删除该槽，等于恢复 FlatBuffers 缺省。
 *   - 新建 AgentInfo / Size / TileAttr 等 struct 的成员个数和类型跟 fbs 走。
 *
 * MapCell 是画布工作视图：Terrain 存在格子上，Unit/建筑是 Document 里同一张表的引用。
 * 保存前必须 SyncGrid，把格子上的地形、部队、建筑收进向量。
 */
using System.Globalization;
using BtlCore.Fb;
using BtlCore.Front;

namespace BtldMapEditor.Front
{
    /// <summary>按 field id 导航、确保子表存在、以及格子 ↔ 向量同步。</summary>
    public static class FrontNav
    {
        public static BtlTable Root(BtlFrontDocument doc) => doc?.Root;

        public static BtlTable Child(BtlTable tbl, int id)
        {
            if (tbl == null) return null;
            return tbl.F.TryGetValue(id, out var n) ? n as BtlTable : null;
        }

        public static BtlStruct ChildStruct(BtlTable tbl, int id)
        {
            if (tbl == null) return null;
            return tbl.F.TryGetValue(id, out var n) ? n as BtlStruct : null;
        }

        public static BtlVector ChildVec(BtlTable tbl, int id)
        {
            if (tbl == null) return null;
            return tbl.F.TryGetValue(id, out var n) ? n as BtlVector : null;
        }

        /// <summary>没有这张子表就新建空表并挂上。用户点「加一项」时会走到这里。</summary>
        public static BtlTable EnsureTable(BtlTable parent, int id)
        {
            if (parent == null) return null;
            if (parent.F.TryGetValue(id, out var n) && n is BtlTable t) return t;
            var created = BtlFrontJson.NewTable();
            parent.F[id] = created;
            return created;
        }

        public static BtlVector EnsureVec(BtlTable parent, int id, string elem)
        {
            if (parent == null) return null;
            if (parent.F.TryGetValue(id, out var n) && n is BtlVector v)
            {
                if (string.IsNullOrEmpty(v.Elem)) v.Elem = elem;
                return v;
            }
            var created = BtlFrontJson.NewVector(elem);
            parent.F[id] = created;
            return created;
        }

        // —— Root 及常用子表：数字与 schema/battle.fbs 的 id 一致 ——
        public static BtlTable Map(BtlFrontDocument doc) => Child(Root(doc), 1);
        public static BtlTable Meta(BtlFrontDocument doc) => Child(Root(doc), 2);
        public static BtlTable Battle(BtlFrontDocument doc) => Child(Root(doc), 3);
        public static BtlTable Factions(BtlFrontDocument doc) => Child(Root(doc), 4);
        public static BtlTable Triggers(BtlFrontDocument doc) => Child(Root(doc), 5);
        public static BtlTable Ai(BtlFrontDocument doc) => Child(Root(doc), 6);

        public static BtlStruct MapSize(BtlFrontDocument doc) => ChildStruct(Map(doc), 0);
        public static BtlVector Tiles(BtlFrontDocument doc) => ChildVec(Map(doc), 2);
        public static BtlVector Attrs(BtlFrontDocument doc) => ChildVec(Map(doc), 3);
        public static BtlVector Agents(BtlFrontDocument doc) => ChildVec(Ai(doc), 0);
        public static BtlVector Behaviors(BtlFrontDocument doc) => ChildVec(Ai(doc), 1);
        public static BtlVector Events(BtlFrontDocument doc) => ChildVec(Triggers(doc), 0);
        public static BtlVector FactionList(BtlFrontDocument doc) => ChildVec(Factions(doc), 0);
        public static BtlVector FactionCards(BtlFrontDocument doc) => ChildVec(Factions(doc), 1);
        public static BtlVector FactionLimits(BtlFrontDocument doc) => ChildVec(Factions(doc), 3);
        public static BtlVector ReinforcePoints(BtlFrontDocument doc) => ChildVec(Battle(doc), 1);
        public static BtlVector Targets(BtlFrontDocument doc) => ChildVec(Meta(doc), 0);
        public static BtlTable Weather(BtlFrontDocument doc) => Child(Root(doc), 9);
        public static BtlVector Weathers(BtlFrontDocument doc) => ChildVec(Weather(doc), 0);
        public static BtlVector CountryAiBt(BtlFrontDocument doc) => ChildVec(Root(doc), 10);
        public static BtlVector SubRegions(BtlFrontDocument doc) => ChildVec(Child(Root(doc), 7), 1);

        public static int MapWidth(BtlFrontDocument doc) => (int)MemberU16(MapSize(doc), 0);
        public static int MapHeight(BtlFrontDocument doc) => (int)MemberU16(MapSize(doc), 1);

        public static long ToI64(object v)
        {
            if (v == null) return 0;
            if (v is bool b) return b ? 1 : 0;
            if (v is BtlScalar s) return ToI64(s.V);
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        public static ushort MemberU16(BtlStruct st, int i) => (ushort)MemberI64(st, i);
        public static uint MemberU32(BtlStruct st, int i) => (uint)MemberI64(st, i);

        public static long MemberI64(BtlStruct st, int i)
        {
            if (st == null || i < 0 || i >= st.V.Count) return 0;
            return ToI64(st.V[i]);
        }

        public static float MemberF32(BtlStruct st, int i)
        {
            if (st == null || i < 0 || i >= st.V.Count || st.V[i] == null) return 0;
            object v = st.V[i] is BtlScalar s ? s.V : st.V[i];
            try { return Convert.ToSingle(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        public static bool MemberExists(BtlStruct st, int i) => st != null && i >= 0 && i < st.V.Count;

        public static void SetMember(BtlStruct st, int i, object value)
        {
            if (st == null || i < 0) return;
            while (st.V.Count <= i) st.V.Add(0);
            st.V[i] = value ?? 0;
        }

        public static bool Has(BtlTable tbl, int id) => tbl != null && tbl.F.ContainsKey(id);

        public static object ScalarV(BtlTable tbl, int id)
        {
            if (tbl == null || !tbl.F.TryGetValue(id, out var n)) return null;
            return n is BtlScalar s ? s.V : null;
        }

        public static long ScalarI64(BtlTable tbl, int id, long missing = 0)
        {
            var v = ScalarV(tbl, id);
            return v == null ? missing : ToI64(v);
        }

        public static long? ScalarI64N(BtlTable tbl, int id)
        {
            var v = ScalarV(tbl, id);
            return v == null ? null : ToI64(v);
        }

        /// <summary>用户改过才写入。value==null 则删槽（恢复 FB 缺省）。写成 0 也会留下这个槽。</summary>
        public static void SetScalar(BtlTable tbl, int id, string t, object value)
        {
            if (tbl == null) return;
            if (value == null)
            {
                tbl.F.Remove(id);
                return;
            }
            tbl.F[id] = BtlFrontJson.Scalar(t, value);
        }

        public static void SetChild(BtlTable tbl, int id, BtlNode node)
        {
            if (tbl == null) return;
            if (node == null) tbl.F.Remove(id);
            else tbl.F[id] = node;
        }

        public static BtlStruct EnsureAgentInfo(BtlTable agent)
        {
            if (agent == null) return null;
            if (agent.F.TryGetValue(0, out var n) && n is BtlStruct st)
            {
                PadStruct(st, "AgentInfo");
                return st;
            }
            st = StructFromFbs("AgentInfo");
            agent.F[0] = st;
            return st;
        }

        public static ushort AgentU16(BtlTable agent, int member) =>
            MemberU16(ChildStruct(agent, 0), member);

        public static ushort? AgentU16N(BtlTable agent, int member)
        {
            var st = ChildStruct(agent, 0);
            if (!MemberExists(st, member)) return null;
            return MemberU16(st, member);
        }

        public static void SetAgentU16(BtlTable agent, int member, ushort? value)
        {
            var st = EnsureAgentInfo(agent);
            SetMember(st, member, value ?? 0);
        }

        public static List<ushort> U16Items(BtlVector vec)
        {
            var list = new List<ushort>();
            if (vec?.V == null) return list;
            foreach (var item in vec.V) list.Add((ushort)ToI64(item));
            return list;
        }

        public static void SetU16Items(BtlVector vec, IEnumerable<ushort> items)
        {
            if (vec == null) return;
            vec.V.Clear();
            if (items == null) return;
            foreach (var x in items) vec.V.Add(x);
        }

        public static void RemoveAt(BtlVector vec, int index)
        {
            if (vec?.V == null || index < 0 || index >= vec.V.Count) return;
            vec.V.RemoveAt(index);
        }

        public static void Insert(BtlVector vec, int index, object item)
        {
            if (vec?.V == null || item == null) return;
            if (index < 0 || index > vec.V.Count) vec.V.Add(item);
            else vec.V.Insert(index, item);
        }

        static void PadStruct(BtlStruct st, string structName)
        {
            if (st == null || !SoftSchema.TryStructMembers(structName, out var members)) return;
            while (st.V.Count < members.Length)
                st.V.Add(SoftSchema.DefaultValue(members[st.V.Count].T));
        }

        /// <summary>按 battle.fbs 的 struct 成员列表建节点；values 不足的用类型零值补齐。</summary>
        public static BtlStruct StructFromFbs(string structName, params object[] values)
        {
            if (SoftSchema.TryStructMembers(structName, out var members))
            {
                var st = BtlFrontJson.NewStruct();
                for (int i = 0; i < members.Length; i++)
                {
                    object v = i < values.Length && values[i] != null
                        ? values[i]
                        : SoftSchema.DefaultValue(members[i].T);
                    st.V.Add(v);
                }
                return st;
            }
            return BtlFrontJson.NewStruct(values);
        }

        public static BtlStruct CloneStruct(BtlStruct st) =>
            st == null ? null : BtlFrontJson.Clone(st) as BtlStruct;

        public static BtlTable CloneTable(BtlTable tbl) =>
            tbl == null ? null : BtlFrontJson.Clone(tbl) as BtlTable;

        public static BtlStruct EnsureFactionInfo(BtlTable faction)
        {
            if (faction == null) return null;
            if (faction.F.TryGetValue(0, out var n) && n is BtlStruct st)
            {
                PadStruct(st, "FactionMetadata");
                return st;
            }
            st = StructFromFbs("FactionMetadata");
            faction.F[0] = st;
            return st;
        }

        public static bool AsBool(object v)
        {
            if (v == null) return false;
            if (v is bool b) return b;
            if (v is BtlScalar s) return AsBool(s.V);
            return ToI64(v) != 0;
        }

        public static ushort? RemapCellIndex(int oldIdx, int oldW, int expandLeft, int expandUp, int newW, int newH)
        {
            if (oldIdx < 0 || oldW <= 0) return null;
            int x = oldIdx % oldW + expandLeft;
            int y = oldIdx / oldW + expandUp;
            if (x < 0 || y < 0 || x >= newW || y >= newH) return null;
            return (ushort)(y * newW + x);
        }

        public static byte AttrU8(BtlStruct st, int i) => (byte)MemberI64(st, i);
        public static sbyte AttrI8(BtlStruct st, int i) => unchecked((sbyte)(byte)MemberI64(st, i));

        public static BtlStruct NewAttr(byte b0, byte b1, sbyte dx, sbyte dy) =>
            StructFromFbs("TileAttr", b0, b1, dx, dy);

        public static BtlTable NewAgent(ushort cellIdx, ushort faction, ushort agentId, ushort unitId,
            ushort stack, ushort val5, ushort hp, ushort maxHp)
        {
            var tbl = BtlFrontJson.NewTable();
            tbl.F[0] = StructFromFbs("AgentInfo", cellIdx, faction, agentId, unitId, stack, val5, hp, maxHp);
            return tbl;
        }

        public static BtlTable NewBuildingEvent(ushort tile, ushort buildingId, ushort val1, byte val2,
            byte? owner, sbyte? dx, sbyte? dy, byte? field6)
        {
            var ev = BtlFrontJson.NewTable();
            SetScalar(ev, 0, "u16", tile);
            var detail = BtlFrontJson.NewTable();
            detail.F[0] = StructFromFbs("BuildingData", val1, buildingId, val2, owner ?? 0, dx ?? 0, dy ?? 0);
            detail.F[2] = BtlFrontJson.NewTable();
            detail.F[4] = BtlFrontJson.NewTable();
            if (field6 != null) SetScalar(detail, 6, "u8", field6);
            ev.F[3] = detail;
            return ev;
        }

        public static BtlTable NewFortEvent(ushort tile, byte fortId, byte field3)
        {
            var ev = BtlFrontJson.NewTable();
            SetScalar(ev, 0, "u16", tile);
            var detail = BtlFrontJson.NewTable();
            SetScalar(detail, 0, "u8", fortId);
            SetScalar(detail, 3, "u8", field3);
            ev.F[4] = detail;
            return ev;
        }

        public static BtlTable BuildingDetail(BtlTable ev) => Child(ev, 3);
        public static BtlTable FortDetail(BtlTable ev) => Child(ev, 4);
        public static BtlStruct BuildingData(BtlTable ev) => ChildStruct(BuildingDetail(ev), 0);

        public static List<BtlTable> TableItems(BtlVector vec)
        {
            var list = new List<BtlTable>();
            if (vec?.V == null) return list;
            foreach (var item in vec.V)
                if (item is BtlTable t) list.Add(t);
            return list;
        }

        /// <summary>
        /// 新建空白战役：地图尺寸、全 0 地块、空的目标/势力/触发/AI 向量。
        /// 不写 Root/10（行为树）；区域名默认 NewMap。version 为 0 则省略 version 槽。
        /// </summary>
        public static BtlFrontDocument NewMap(ushort w, ushort h, ushort lm, ushort tm, ushort pw, ushort ph,
            ushort roundLimit, ushort version)
        {
            int n = w * h;
            var tiles = BtlFrontJson.NewVector("u16");
            for (int i = 0; i < n; i++) tiles.V.Add((ushort)0);

            var map = BtlFrontJson.NewTable();
            map.F[0] = StructFromFbs("Size", w, h, lm, tm, pw, ph);
            map.F[1] = BtlFrontJson.Scalar("u8", (byte)1);
            map.F[2] = tiles;
            map.F[3] = BtlFrontJson.NewVector("struct");

            var meta = BtlFrontJson.NewTable();
            meta.F[0] = BtlFrontJson.NewVector("table");
            if (roundLimit != 0) meta.F[1] = BtlFrontJson.Scalar("u16", roundLimit);

            var battle = BtlFrontJson.NewTable();
            battle.F[1] = BtlFrontJson.NewVector("table");

            var faction = BtlFrontJson.NewTable();
            faction.F[0] = BtlFrontJson.NewVector("table");
            faction.F[1] = BtlFrontJson.NewVector("table");
            faction.F[3] = BtlFrontJson.NewVector("table");

            var trigger = BtlFrontJson.NewTable();
            trigger.F[0] = BtlFrontJson.NewVector("table");

            var ai = BtlFrontJson.NewTable();
            ai.F[0] = BtlFrontJson.NewVector("table");

            var region = BtlFrontJson.NewTable();
            region.F[0] = BtlFrontJson.Scalar("string", "NewMap");
            region.F[1] = BtlFrontJson.NewVector("table");
            var ev = BtlFrontJson.NewVector("u8");
            foreach (byte b in new byte[] { 0, 0, 40, 1 }) ev.V.Add(b);
            region.F[2] = ev;
            var props = BtlFrontJson.NewVector("u8");
            foreach (byte b in new byte[] { 0, 0, 40, 1 }) props.V.Add(b);
            region.F[3] = props;

            var cfg = BtlFrontJson.NewTable();
            cfg.F[7] = BtlFrontJson.NewVector("u16");

            var weather = BtlFrontJson.NewTable();
            weather.F[0] = BtlFrontJson.NewVector("table");

            var view = BtlFrontJson.NewTable();

            var root = BtlFrontJson.NewTable();
            if (version != 0) root.F[0] = BtlFrontJson.Scalar("u16", version);
            root.F[1] = map;
            root.F[2] = meta;
            root.F[3] = battle;
            root.F[4] = faction;
            root.F[5] = trigger;
            root.F[6] = ai;
            root.F[7] = region;
            root.F[8] = cfg;
            root.F[9] = weather;
            root.F[11] = view;

            return new BtlFrontDocument { FormatVersion = 1, Root = root };
        }

        /// <summary>
        /// 从 Document 展开六角格工作视图。属性槽按 tiles 高字节 A1/A2/A3 标志顺序消费 attrs 向量。
        /// Unit / TriggerBldg / TriggerFort 指向原表，改格子就是改文档。
        /// </summary>
        public static List<MapCell> RebuildCells(BtlFrontDocument doc)
        {
            var cells = new List<MapCell>();
            var size = MapSize(doc);
            if (size == null) return cells;
            int width = (int)MemberU16(size, 0);
            int height = (int)MemberU16(size, 1);
            if (width <= 0 || height <= 0) return cells;

            var tiles = Tiles(doc);
            var attrs = Attrs(doc);
            var agents = TableItems(Agents(doc));
            var events = TableItems(Events(doc));

            var agentsByCell = new Dictionary<int, BtlTable>();
            foreach (var a in agents)
            {
                int idx = (int)AgentU16(a, 0);
                agentsByCell[idx] = a;
            }

            var bldgByCell = new Dictionary<int, BtlTable>();
            var fortByCell = new Dictionary<int, BtlTable>();
            foreach (var ev in events)
            {
                int idx = (int)ScalarI64(ev, 0);
                if (Child(ev, 3) != null) bldgByCell[idx] = ev;
                if (Child(ev, 4) != null) fortByCell[idx] = ev;
            }

            int attrIndex = 0;
            int attrCount = attrs?.V?.Count ?? 0;
            int tileCount = tiles?.V?.Count ?? 0;
            int total = width * height;
            for (int i = 0; i < total; i++)
            {
                ushort terrain = 9001;
                if (i < tileCount) terrain = (ushort)ToI64(tiles.V[i]);
                byte v6 = (byte)(terrain >> 8);

                BtlStruct a1 = null, a2 = null, a3 = null;
                if ((v6 & 4) != 0 && attrIndex < attrCount)
                    a1 = attrs.V[attrIndex++] as BtlStruct;
                if ((v6 & 8) != 0 && attrIndex < attrCount)
                    a2 = attrs.V[attrIndex++] as BtlStruct;
                if ((v6 & 0x10) != 0 && attrIndex < attrCount)
                    a3 = attrs.V[attrIndex++] as BtlStruct;
                if (a1 == null) a1 = NewAttr(0, 0, 0, 0);

                agentsByCell.TryGetValue(i, out var unit);
                bldgByCell.TryGetValue(i, out var bldg);
                fortByCell.TryGetValue(i, out var fort);

                cells.Add(new MapCell
                {
                    Index = i,
                    X = i % width,
                    Y = i / width,
                    Terrain = terrain,
                    Attr = a1,
                    AttrA2 = a2,
                    AttrA3 = a3,
                    Unit = unit,
                    TriggerBldg = bldg,
                    TriggerFort = fort
                });
            }
            return cells;
        }

        /// <summary>
        /// 保存前三步：tiles/attrs 按格子重写；在格部队写回 cell_idx 并保留离图部队；
        /// 建筑/工事事件写回格子索引，脚本事件（没有 3/4 子表）原样保留。
        /// </summary>
        public static void SyncGrid(BtlFrontDocument doc, IList<MapCell> cells)
        {
            if (doc?.Root == null || cells == null || cells.Count == 0) return;
            var map = EnsureTable(doc.Root, 1);
            var tiles = EnsureVec(map, 2, "u16");
            var attrs = EnsureVec(map, 3, "struct");
            tiles.V.Clear();
            attrs.V.Clear();
            foreach (var cell in cells)
            {
                tiles.V.Add(cell.Terrain);
                byte v6 = (byte)(cell.Terrain >> 8);
                if ((v6 & 4) != 0) attrs.V.Add(cell.Attr ?? NewAttr(0, 0, 0, 0));
                if ((v6 & 8) != 0) attrs.V.Add(cell.AttrA2 ?? NewAttr(0, 0, 0, 0));
                if ((v6 & 0x10) != 0) attrs.V.Add(cell.AttrA3 ?? NewAttr(0, 0, 0, 0));
            }

            var ai = EnsureTable(doc.Root, 6);
            var agentsVec = EnsureVec(ai, 0, "table");
            int total = cells.Count;
            var onGrid = new HashSet<BtlTable>();
            var ordered = new List<BtlTable>();
            foreach (var cell in cells)
            {
                if (cell.Unit == null) continue;
                SetAgentU16(cell.Unit, 0, (ushort)cell.Index);
                onGrid.Add(cell.Unit);
            }
            foreach (var existing in TableItems(agentsVec))
            {
                bool keep = onGrid.Contains(existing);
                bool offMap = AgentU16(existing, 0) >= total;
                if (keep || offMap)
                {
                    ordered.Add(existing);
                    onGrid.Remove(existing);
                }
            }
            foreach (var extra in onGrid) ordered.Add(extra);
            agentsVec.V.Clear();
            foreach (var a in ordered) agentsVec.V.Add(a);

            var trig = EnsureTable(doc.Root, 5);
            var eventsVec = EnsureVec(trig, 0, "table");
            var keptEvents = new List<BtlTable>();
            var gridEvents = new HashSet<BtlTable>();
            foreach (var cell in cells)
            {
                if (cell.TriggerBldg != null)
                {
                    SetScalar(cell.TriggerBldg, 0, "u16", (ushort)cell.Index);
                    gridEvents.Add(cell.TriggerBldg);
                }
                if (cell.TriggerFort != null)
                {
                    SetScalar(cell.TriggerFort, 0, "u16", (ushort)cell.Index);
                    gridEvents.Add(cell.TriggerFort);
                }
            }
            foreach (var ev in TableItems(eventsVec))
            {
                bool on = gridEvents.Contains(ev);
                bool script = Child(ev, 3) == null && Child(ev, 4) == null;
                if (on || script)
                {
                    keptEvents.Add(ev);
                    gridEvents.Remove(ev);
                }
            }
            foreach (var extra in gridEvents) keptEvents.Add(extra);
            eventsVec.V.Clear();
            foreach (var ev in keptEvents) eventsVec.V.Add(ev);
        }
    }

    /// <summary>
    /// 格子工作视图（不属于 BtlFront 文档本身）。
    /// Terrain 暂存在这里直到 SyncGrid；Unit / 建筑表是 Document 里的同一引用。
    /// </summary>
    public sealed class MapCell
    {
        public int Index { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public ushort Terrain { get; set; }
        public BtlStruct Attr { get; set; }
        public BtlStruct AttrA2 { get; set; }
        public BtlStruct AttrA3 { get; set; }
        public BtlTable Unit { get; set; }
        public BtlTable TriggerBldg { get; set; }
        public BtlTable TriggerFort { get; set; }
    }
}
