/*
 * UnitEdits.cs
 *
 * 画布投影：按 cell_idx 把部队摊到格子上，保存前再写回 agents。
 * 改属性、放置和删除在 scripts/unit.lua。
 * 场上部队按 cell_idx 对上格子；cell_idx 落在地图外的留在向量里。
 */
using System.Globalization;

namespace BtlCore.Front
{
    public static class UnitEdits
    {
        public static BtlTable Create(ushort cellIdx, ushort faction, ushort agentId, ushort unitId,
            ushort stack, ushort val5, ushort hp, ushort maxHp)
        {
            var tbl = BtlFrontJson.NewTable();
            tbl.F[0] = FrontEdit.StructFromFbs("AgentInfo", cellIdx, faction, agentId, unitId, stack, val5, hp, maxHp);
            return tbl;
        }

        /// <summary>与 RebuildCells 一样，后出现的部队占用该格。地图外的不在这张表里。</summary>
        public static List<BtlTable> Project(BtlFrontDocument doc, int cellCount)
        {
            var cells = new List<BtlTable>(cellCount);
            for (int i = 0; i < cellCount; i++) cells.Add(null);
            var vec = Agents(doc);
            if (vec?.V == null || cellCount <= 0) return cells;
            foreach (var item in vec.V)
            {
                if (item is not BtlTable agent) continue;
                int idx = CellIndex(agent);
                if (idx >= 0 && idx < cellCount) cells[idx] = agent;
            }
            return cells;
        }

        /// <summary>与 SyncGrid 重写 agents 的那一段相同。格子上的写回 cell_idx，地图外的原样保留。</summary>
        public static void SyncAgents(BtlFrontDocument doc, IList<BtlTable> unitByCell)
        {
            if (doc?.Root == null || unitByCell == null) return;
            var ai = FrontEdit.EnsureTable(doc.Root, 6);
            var vec = FrontEdit.EnsureVec(ai, 0, "table");
            int total = unitByCell.Count;
            var onGrid = new HashSet<BtlTable>();
            var ordered = new List<BtlTable>();
            for (int i = 0; i < unitByCell.Count; i++)
            {
                var unit = unitByCell[i];
                if (unit == null) continue;
                SetU16(unit, 0, (ushort)i);
                onGrid.Add(unit);
            }
            foreach (var item in vec.V.ToList())
            {
                if (item is not BtlTable agent) continue;
                bool keep = onGrid.Contains(agent);
                bool offMap = CellIndex(agent) >= total;
                if (keep || offMap)
                {
                    ordered.Add(agent);
                    onGrid.Remove(agent);
                }
            }
            foreach (var extra in onGrid) ordered.Add(extra);
            vec.V.Clear();
            foreach (var agent in ordered) vec.V.Add(agent);
        }

        static BtlVector Agents(BtlFrontDocument doc)
        {
            if (doc?.Root == null) return null;
            if (doc.Root.F.TryGetValue(6, out var aiNode) && aiNode is BtlTable ai
                && ai.F.TryGetValue(0, out var vecNode) && vecNode is BtlVector vec)
                return vec;
            return null;
        }

        static int CellIndex(BtlTable agent)
        {
            if (agent == null || !agent.F.TryGetValue(0, out var node) || node is not BtlStruct st) return 0;
            if (st.V.Count <= 0 || st.V[0] == null) return 0;
            try { return (int)Convert.ToInt64(st.V[0], CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        static void SetU16(BtlTable agent, int member, ushort? value)
        {
            var st = EnsureAgentInfo(agent);
            FrontEdit.SetMember(st, member, value ?? (ushort)0);
        }

        static BtlStruct EnsureAgentInfo(BtlTable agent)
        {
            if (agent.F.TryGetValue(0, out var node) && node is BtlStruct st)
            {
                if (BtlCore.Fb.SoftSchema.TryStructMembers("AgentInfo", out var members))
                {
                    while (st.V.Count < members.Length)
                        st.V.Add(BtlCore.Fb.SoftSchema.DefaultValue(members[st.V.Count].T));
                }
                return st;
            }
            st = FrontEdit.StructFromFbs("AgentInfo");
            FrontEdit.SetField(agent, 0, st);
            return st;
        }
    }
}
