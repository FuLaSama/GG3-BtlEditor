/*
 * BtlBridge.cs
 * 
 * 本文件作为 BTL 二进制数据加载与物理地址绑定的适配桥接器：
 * - BTL 物理数据反序列化 (LoadBtl): 调用 FlatBufferDecompiler 将 BTL 字节流转换为未归一化的字典树，利用 BtlSchema 动态类型重构将其标准化，并通过 JSON 序列化反序列化得到 StageModel 强类型关卡数据。
 * - 物理寻址与对象映射: 自动消费地表 RLE 重复属性，顺次计算出每个地块在 BTL 二进制中的绝对偏移量，并从 TriggerEvents 表中直接根据字节结构解析出相应的建筑和工事信息，将其与 Hex 网格一一关联。
 * - IDA 反汇编流生成 (GenerateDisasm): 递归遍历各段 Table 结构、偏移和关联标号引用，生成带 XREF 物理指向关系的 IDA 风格等宽反汇编伪代码项列表，方便视图双向跳转。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BtlVerifier
{
    // BTL 文件加载与物理地址绑定的适配桥接器 (中文注释版)
    public static class BtlBridge
    {
        private class AttrMappingInfo
        {
            public int TileX { get; set; }
            public int TileY { get; set; }
            public string SlotName { get; set; }
            public bool IsFirstOfTile { get; set; }
        }

        [ThreadStatic]
        private static Dictionary<int, AttrMappingInfo> _currentAttrMapping;

        public static StageModel LoadBtl(string btlFilePath)
        {
            if (!File.Exists(btlFilePath))
                throw new FileNotFoundException("BTL 文件未找到", btlFilePath);

            // 读取原始二进制字节数据
            byte[] data = File.ReadAllBytes(btlFilePath);

            // 调用 C# 原生反解器反编译为字典树
            var decompiler = new FlatBufferDecompiler(data);
            var rootDict = decompiler.Decompile();

            if (rootDict == null)
                throw new Exception("BTL 反编译解析失败，生成的数据为空。");

            // 保存未归一化的原始字典结构为 JSON，保留外部 fbs 和 sym 定义的原始字段名
            string rawJson = JsonSerializer.Serialize(rootDict);

            // Normalize dictionary back to standard schema names for C# deserialization!
            if (BtlSchema.UseDynamic)
            {
                rootDict = NormalizeDict(rootDict) as Dictionary<string, object>;
            }

            // 通过序列化/反序列化为 StageModel 强类型结构
            string jsonContent = JsonSerializer.Serialize(rootDict);
            StageModel stage = JsonSerializer.Deserialize<StageModel>(jsonContent);
            stage.File = btlFilePath;

            using (JsonDocument doc = JsonDocument.Parse(rawJson))
            {
                stage.RawRoot = doc.RootElement.Clone();
            }

            // 解析 stage_config 里的城市和港口状态数据，方便渲染引擎快速定位
            var cities = new Dictionary<string, CityStatusModel>();
            var harbors = new Dictionary<string, CityStatusModel>();

            if (stage.StageConfig.HasValue && stage.StageConfig.Value.ValueKind == JsonValueKind.Object)
            {
                var config = stage.StageConfig.Value;
                if (config.TryGetProperty("cities_status", out var citiesProp) && citiesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cityVal in citiesProp.EnumerateArray())
                    {
                        ushort x = 0, y = 0, owner = 0, level = 0;
                        if (cityVal.TryGetProperty("x", out var xProp)) x = xProp.GetUInt16();
                        if (cityVal.TryGetProperty("y", out var yProp)) y = yProp.GetUInt16();
                        if (cityVal.TryGetProperty("owner", out var ownerProp)) owner = ownerProp.GetUInt16();
                        if (cityVal.TryGetProperty("level", out var lvlProp)) level = lvlProp.GetUInt16();

                        var model = new CityStatusModel { X = x, Y = y, Owner = owner, Level = level };
                        cities[$"{x},{y}"] = model;
                    }
                }

                if (config.TryGetProperty("harbors_status", out var harborsProp) && harborsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var harborVal in harborsProp.EnumerateArray())
                    {
                        ushort x = 0, y = 0, owner = 0, level = 0;
                        if (harborVal.TryGetProperty("x", out var xProp)) x = xProp.GetUInt16();
                        if (harborVal.TryGetProperty("y", out var yProp)) y = yProp.GetUInt16();
                        if (harborVal.TryGetProperty("owner", out var ownerProp)) owner = ownerProp.GetUInt16();
                        if (harborVal.TryGetProperty("level", out var lvlProp)) level = lvlProp.GetUInt16();

                        var model = new CityStatusModel { X = x, Y = y, Owner = owner, Level = level };
                        harbors[$"{x},{y}"] = model;
                    }
                }
            }

            stage.CitiesStatus = cities;
            stage.HarborsStatus = harbors;

            // 解析 trigger_info.events 中的建筑和工事数据（直接从二进制读取）
            stage.TriggerEvents = ParseTriggerEvents(data, stage.MapTerrain?.Size?.Width ?? 28);

            // 提取关键向量数据的物理起始文件偏移量
            stage.TilesOffset = decompiler.TilesOffset;
            stage.AttributesOffset = decompiler.AttributesOffset;
            stage.ReinforcePointsOffset = decompiler.ReinforcePointsOffset;

            // 解析每个增员点（ReinforcePoint）在二进制文件中的绝对位置
            if (stage.BattleInfo?.ReinforcePoints != null)
            {
                foreach (var rp in stage.BattleInfo.ReinforcePoints)
                {
                    if (!string.IsNullOrEmpty(rp.PosStr) && rp.PosStr.StartsWith("0x"))
                    {
                        try
                        {
                            rp.Offset = Convert.ToInt32(rp.PosStr.Substring(2), 16);
                        }
                        catch
                        {
                            rp.Offset = 0;
                        }
                    }
                }
            }

            // 解析每个挑战目标（StageTarget）的绝对位置
            if (stage.StageMetadata?.Targets != null)
            {
                foreach (var target in stage.StageMetadata.Targets)
                {
                    if (!string.IsNullOrEmpty(target.PosStr) && target.PosStr.StartsWith("0x"))
                    {
                        try
                        {
                            target.Offset = Convert.ToInt32(target.PosStr.Substring(2), 16);
                        }
                        catch
                        {
                            target.Offset = 0;
                        }
                    }
                }
            }

            // 解析每个 AI 代理部队（AIAgent）的物理绝对位置
            if (stage.AIInfo?.Agents != null)
            {
                foreach (var agent in stage.AIInfo.Agents)
                {
                    if (!string.IsNullOrEmpty(agent.PosStr) && agent.PosStr.StartsWith("0x"))
                    {
                        try
                        {
                            agent.Offset = Convert.ToInt32(agent.PosStr.Substring(2), 16);
                        }
                        catch
                        {
                            agent.Offset = 0;
                        }
                    }
                }
            }

            // 建立触发器事件到格子索引的快速映射
            var triggerBldgByCell = new Dictionary<int, TriggerEventModel>();
            var triggerFortByCell = new Dictionary<int, TriggerEventModel>();
            foreach (var evt in stage.TriggerEvents)
            {
                if (evt.BuildingId > 0)
                    triggerBldgByCell[evt.TileIndex] = evt;
                if (evt.FortId > 0)
                    triggerFortByCell[evt.TileIndex] = evt;
            }

            // 获取数据列表以供重构
            ushort width = stage.MapTerrain.Size.Width;
            ushort height = stage.MapTerrain.Size.Height;
            List<ushort> tiles = stage.MapTerrain.Tiles;
            List<List<byte>> attributes = stage.MapTerrain.Attributes;

            // 建立单元格到 AI 代理的快速索引映射
            var agentsByCell = new Dictionary<int, (AIAgentModel agent, int index)>();
            if (stage.AIInfo?.Agents != null)
            {
                for (int idx = 0; idx < stage.AIInfo.Agents.Count; idx++)
                {
                    var agent = stage.AIInfo.Agents[idx];
                    if (agent.AgentInfo != null)
                    {
                        agentsByCell[agent.AgentInfo.CellIdx] = (agent, idx);
                    }
                }
            }

            int attrIndex = 0;
            int unitDisplayIndex = 0;
            stage.Cells = new List<CellModel>();

            for (int i = 0; i < width * height; i++)
            {
                int x = i % width;
                int y = i / width;
                ushort terrain = i < tiles.Count ? tiles[i] : (ushort)9001;
                byte v6 = (byte)(terrain >> 8);

                List<byte> attrA1 = null;
                List<byte> attrA2 = null;
                List<byte> attrA3 = null;

                int a1Offset = 0;
                int a2Offset = 0;
                int a3Offset = 0;

                // 按照 RLE 条件消费队列顺次分配属性块并计算文件物理地址
                if ((v6 & 4) != 0 && attrIndex < attributes.Count)
                {
                    attrA1 = attributes[attrIndex];
                    a1Offset = stage.AttributesOffset + 4 + attrIndex * 4;
                    attrIndex++;
                    while (attrA1.Count < 4) attrA1.Add(0);
                }
                if ((v6 & 8) != 0 && attrIndex < attributes.Count)
                {
                    attrA2 = attributes[attrIndex];
                    a2Offset = stage.AttributesOffset + 4 + attrIndex * 4;
                    attrIndex++;
                    while (attrA2.Count < 4) attrA2.Add(0);
                }
                if ((v6 & 0x10) != 0 && attrIndex < attributes.Count)
                {
                    attrA3 = attributes[attrIndex];
                    a3Offset = stage.AttributesOffset + 4 + attrIndex * 4;
                    attrIndex++;
                    while (attrA3.Count < 4) attrA3.Add(0);
                }

                // 如果格子本身无 A1 属性，默认分配空容器做前端展示
                if (attrA1 == null)
                {
                    attrA1 = new List<byte> { 0, 0, 0, 0 };
                }

                // 从 AI 代理数据中查找部署的部队
                UnitModel cellUnit = null;
                int unitTableOffset = 0;

                if (agentsByCell.TryGetValue(i, out var pair))
                {
                    var agent = pair.agent;
                    ushort generalId = agent.ExtraTable != null ? agent.ExtraTable.GeneralId : (ushort)65535;
                    cellUnit = new UnitModel
                    {
                        UnitId = agent.AgentInfo.UnitId,
                        CellIdx = agent.AgentInfo.CellIdx,
                        FactionId = (byte)agent.AgentInfo.FactionId,
                        Flag = (byte)(generalId != 65535 ? 1 : 0),
                        State = (byte)agent.AgentInfo.StackCount, // 原有State用来存放编队数
                        GeneralId = generalId,
                        StackCount = agent.AgentInfo.StackCount,
                        HP = agent.AgentInfo.HP,
                        MaxHP = agent.AgentInfo.MaxHP,
                        Offset = agent.Offset,
                        AgentIndex = pair.index,
                        DisplayIndex = unitDisplayIndex++
                    };
                    unitTableOffset = agent.Offset;
                }

                // 从触发器事件映射中查找关联的建筑和工事
                TriggerEventModel cellBldg = null;
                TriggerEventModel cellFort = null;
                triggerBldgByCell.TryGetValue(i, out cellBldg);
                triggerFortByCell.TryGetValue(i, out cellFort);

                stage.Cells.Add(new CellModel
                {
                    Index = i,
                    X = x,
                    Y = y,
                    Terrain = terrain,
                    Attr = attrA1,
                    AttrA2 = attrA2,
                    AttrA3 = attrA3,
                    Unit = cellUnit,

                    // 填充物理文件偏移位置参数
                    TileFileOffset = stage.TilesOffset + 4 + i * 2,
                    AttrA1FileOffset = a1Offset,
                    AttrA2FileOffset = a2Offset,
                    AttrA3FileOffset = a3Offset,
                    UnitTableFileOffset = unitTableOffset,

                    // 关联触发器事件
                    TriggerBuilding = cellBldg,
                    TriggerFort = cellFort
                });
            }

            return stage;
        }

        // 从原始二进制数据中直接解析 TriggerInfo.events 的建筑/工事数据
        private static List<TriggerEventModel> ParseTriggerEvents(byte[] data, int mapWidth)
        {
            var result = new List<TriggerEventModel>();
            if (data == null || data.Length < 4 || mapWidth <= 0)
                return result;

            try
            {
                int rootPos = BitConverter.ToInt32(data, 0);
                if (rootPos <= 0 || rootPos >= data.Length)
                    return result;

                // Root.trigger_info (field_5, vtable slot = 4 + 5*2 = 14)
                int triggerInfoPos = GetTableFieldPointer(data, rootPos, 14);
                if (triggerInfoPos <= 0 || triggerInfoPos >= data.Length)
                    return result;

                // TriggerInfo.events (field_0, vtable slot = 4)
                int eventsVecPos = GetTableFieldPointer(data, triggerInfoPos, 4);
                if (eventsVecPos <= 0 || eventsVecPos + 4 > data.Length)
                    return result;

                int eventCount = BitConverter.ToInt32(data, eventsVecPos);
                int elemStart = eventsVecPos + 4;

                for (int i = 0; i < eventCount; i++)
                {
                    int offsetSlot = elemStart + i * 4;
                    if (offsetSlot + 4 > data.Length) break;

                    int rel = BitConverter.ToInt32(data, offsetSlot);
                    int eventPos = offsetSlot + rel;
                    if (eventPos <= 0 || eventPos + 4 > data.Length) continue;

                    int evtVtableRel = BitConverter.ToInt32(data, eventPos);
                    int evtVtablePos = eventPos - evtVtableRel;
                    if (evtVtablePos < 0 || evtVtablePos + 4 > data.Length) continue;

                    ushort evtVtableSize = BitConverter.ToUInt16(data, evtVtablePos);

                    var evt = new TriggerEventModel();
                    evt.FileOffset = eventPos;

                    // field_0: tile_index (uint16), vtable slot 4
                    ushort tileIndex = ReadFieldUInt16(data, eventPos, evtVtablePos, evtVtableSize, 4);
                    evt.TileIndex = tileIndex;
                    evt.TileX = tileIndex % mapWidth;
                    evt.TileY = tileIndex / mapWidth;

                    // field_3: detail_bldg 子表, vtable slot = 4+3*2 = 10
                    // field_4: detail_fort 子表, vtable slot = 4+4*2 = 12
                    int bldgSubPos = GetTableFieldPointer(data, eventPos, 10);
                    int fortSubPos = GetTableFieldPointer(data, eventPos, 12);

                    bool hasBldg = false;
                    bool hasFort = false;

                    if (bldgSubPos > 0 && bldgSubPos < data.Length)
                    {
                        hasBldg = true;
                        // TriggerEventBldg 的 field_0: building_data struct (8字节内联)
                        int bldgVtableRel = BitConverter.ToInt32(data, bldgSubPos);
                        int bldgVtablePos = bldgSubPos - bldgVtableRel;
                        if (bldgVtablePos >= 0 && bldgVtablePos + 6 <= data.Length)
                        {
                            ushort bldgVtableSize = BitConverter.ToUInt16(data, bldgVtablePos);
                            if (4 < bldgVtableSize)
                            {
                                ushort structOff = BitConverter.ToUInt16(data, bldgVtablePos + 4);
                                if (structOff > 0)
                                {
                                    int structPos = bldgSubPos + structOff;
                                    if (structPos + 8 <= data.Length)
                                    {
                                        evt.BuildingId = BitConverter.ToUInt16(data, structPos + 2);
                                        evt.Owner = data[structPos + 5];
                                        evt.Dx = (sbyte)data[structPos + 6];
                                        evt.Dy = (sbyte)data[structPos + 7];
                                    }
                                }
                            }
                        }
                    }

                    if (fortSubPos > 0 && fortSubPos < data.Length)
                    {
                        hasFort = true;
                        int fortVtableRel = BitConverter.ToInt32(data, fortSubPos);
                        int fortVtablePos = fortSubPos - fortVtableRel;
                        if (fortVtablePos >= 0 && fortVtablePos + 4 <= data.Length)
                        {
                            ushort fortVtableSize = BitConverter.ToUInt16(data, fortVtablePos);
                            ushort rawFortVal = ReadFieldUInt16(data, fortSubPos, fortVtablePos, fortVtableSize, 4);
                            evt.FortId = (ushort)(rawFortVal & 0xFF);
                        }
                    }

                    if (hasBldg && hasFort)
                    {
                        evt.EventType = 4; // 同时存在时归为建筑事件，但通过属性地图可双向引用
                    }
                    else if (hasBldg)
                    {
                        evt.EventType = 4;
                    }
                    else if (hasFort)
                    {
                        evt.EventType = 0;
                    }
                    else
                    {
                        evt.EventType = -1;
                    }

                    result.Add(evt);
                }
            }
            catch { }

            return result;
        }

        private static int GetTableFieldPointer(byte[] data, int tablePos, int vtableSlot)
        {
            if (tablePos < 0 || tablePos + 4 > data.Length) return 0;
            int vtableRel = BitConverter.ToInt32(data, tablePos);
            int vtablePos = tablePos - vtableRel;
            if (vtablePos < 0 || vtablePos + 2 > data.Length) return 0;
            ushort vtableSize = BitConverter.ToUInt16(data, vtablePos);
            if (vtableSlot >= vtableSize || vtablePos + vtableSlot + 2 > data.Length) return 0;
            ushort off = BitConverter.ToUInt16(data, vtablePos + vtableSlot);
            if (off == 0) return 0;
            int fieldPos = tablePos + off;
            if (fieldPos < 0 || fieldPos + 4 > data.Length) return 0;
            int val32 = BitConverter.ToInt32(data, fieldPos);
            if (val32 == 0) return 0;
            return fieldPos + val32;
        }

        private static ushort ReadFieldUInt16(byte[] data, int tablePos, int vtablePos, ushort vtableSize, int vtableSlot)
        {
            if (vtableSlot >= vtableSize || vtablePos + vtableSlot + 2 > data.Length) return 0;
            ushort off = BitConverter.ToUInt16(data, vtablePos + vtableSlot);
            if (off == 0) return 0;
            int fieldPos = tablePos + off;
            if (fieldPos + 2 > data.Length) return 0;
            return BitConverter.ToUInt16(data, fieldPos);
        }

        // 通用 WalkTask 定义，用于扁平化递归树行走向
        private struct WalkTask
        {
            public int Offset;
            public string TypeName;
            public string Path;
        }

        // 生成 IDA 风格的反汇编属性视图行数据 (通用动态 FBS Walking 版本)
        public static List<DisasmItem> GenerateDisasm(byte[] data, StageModel stage)
        {
            _currentAttrMapping = new Dictionary<int, AttrMappingInfo>();
            if (stage != null && stage.MapTerrain?.Tiles != null && stage.MapTerrain?.Size != null)
            {
                var tiles = stage.MapTerrain.Tiles;
                int width = stage.MapTerrain.Size.Width;
                int attrIdx = 0;
                for (int tileIdx = 0; tileIdx < tiles.Count; tileIdx++)
                {
                    ushort val = tiles[tileIdx];
                    byte high = (byte)(val >> 8);
                    bool a1 = (high & 4) != 0;
                    bool a2 = (high & 8) != 0;
                    bool a3 = (high & 0x10) != 0;

                    if (a1 || a2 || a3)
                    {
                        int x = tileIdx % width;
                        int y = tileIdx / width;
                        bool isFirst = true;

                        if (a1)
                        {
                            _currentAttrMapping[attrIdx] = new AttrMappingInfo
                            {
                                TileX = x,
                                TileY = y,
                                SlotName = "Slot 1 (A1)",
                                IsFirstOfTile = isFirst
                            };
                            isFirst = false;
                            attrIdx++;
                        }
                        if (a2)
                        {
                            _currentAttrMapping[attrIdx] = new AttrMappingInfo
                            {
                                TileX = x,
                                TileY = y,
                                SlotName = "Slot 2 (A2)",
                                IsFirstOfTile = isFirst
                            };
                            isFirst = false;
                            attrIdx++;
                        }
                        if (a3)
                        {
                            _currentAttrMapping[attrIdx] = new AttrMappingInfo
                            {
                                TileX = x,
                                TileY = y,
                                SlotName = "Slot 3 (A3)",
                                IsFirstOfTile = isFirst
                            };
                            isFirst = false;
                            attrIdx++;
                        }
                    }
                }
            }

            var items = new List<DisasmItem>();
            var visited = new HashSet<int>();

            int tilesOffset = 0;
            int attributesOffset = 0;
            int reinforcePointsOffset = 0;

            if (data.Length >= 4)
            {
                int root_pos = BitConverter.ToInt32(data, 0);
                items.Add(new DisasmItem
                {
                    Offset = 0,
                    Length = 4,
                    Description = $"entry_point: RootTable at 0x{root_pos:X8}".PadRight(70) + "; BTL 文件入口指针 (指向 Root Table 的绝对地址)",
                    TargetOffset = root_pos
                });

                var queue = new Queue<WalkTask>();
                queue.Enqueue(new WalkTask { Offset = root_pos, TypeName = "Root", Path = "Root" });

                while (queue.Count > 0)
                {
                    var task = queue.Dequeue();
                    if (task.Offset == 0 || task.Offset >= data.Length || !visited.Add(task.Offset))
                        continue;

                    WalkTable(data, task.Offset, task.TypeName, task.Path, queue, items, ref tilesOffset, ref attributesOffset, ref reinforcePointsOffset);
                }
            }

            // 更新 stage 的动态物理偏移以供 Hex Map 渲染
            if (stage != null)
            {
                if (tilesOffset != 0) stage.TilesOffset = tilesOffset;
                if (attributesOffset != 0) stage.AttributesOffset = attributesOffset;
                if (reinforcePointsOffset != 0) stage.ReinforcePointsOffset = reinforcePointsOffset;
            }

            // 建立 XREF 的正向与反向引用关系
            var forwardRefs = new Dictionary<int, int>();
            foreach (var item in items)
            {
                if (item.TargetOffset.HasValue)
                {
                    forwardRefs[item.Offset] = item.TargetOffset.Value;
                }
            }

            var backRefs = new Dictionary<int, List<int>>();
            foreach (var kvp in forwardRefs)
            {
                int src = kvp.Key;
                int dst = kvp.Value;
                if (!backRefs.ContainsKey(dst))
                {
                    backRefs[dst] = new List<int>();
                }
                backRefs[dst].Add(src);
            }

            foreach (var item in items)
            {
                if (backRefs.TryGetValue(item.Offset, out var srcs))
                {
                    if (!item.TargetOffset.HasValue && srcs.Count > 0)
                    {
                        item.TargetOffset = srcs[0];
                    }
                }
            }

            // 插入分隔区段与定位标签
            var sepItems = new List<DisasmItem>();
            var addedLocs = new HashSet<int>();
            var addedBlocks = new HashSet<int>();

            foreach (var item in items)
            {
                if (backRefs.TryGetValue(item.Offset, out var srcs) && srcs.Count > 0 && !item.Description.StartsWith("; ========================"))
                {
                    bool isTableStart = item.Description.Contains("vtable_offset") || 
                                         item.Description.Contains(".length") || 
                                         item.Description.Contains("loc_");

                    if (isTableStart)
                    {
                        if (addedBlocks.Add(item.Offset))
                        {
                            int srcOffset = srcs[0];
                            string blockName = GetDisasmBlockName(item.Description, item.Offset);
                            string srcDesc = "Unknown Pointer";
                            foreach (var searchItem in items)
                            {
                                if (searchItem.Offset == srcOffset)
                                {
                                    srcDesc = searchItem.Description;
                                    int commaIdx = srcDesc.IndexOf(';');
                                    if (commaIdx > 0) srcDesc = srcDesc.Substring(0, commaIdx).Trim();
                                    break;
                                }
                            }

                            bool isAgentSub = blockName.Contains("agents[") || blockName.Contains("AIAgent");
                            if (isAgentSub)
                            {
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = $"; --- [AI Sub-Struct] {blockName} at 0x{item.Offset:X8} (XREFs: 0x{srcOffset:X8} {srcDesc}) ---",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = $"loc_{item.Offset:X8}:",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                addedLocs.Add(item.Offset);
                            }
                            else
                            {
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = "; ===========================================================================",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = $"; Segment/Table: {blockName} at 0x{item.Offset:X8}",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = $"; XREFs:         0x{srcOffset:X8} ({srcDesc})",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = "; ===========================================================================",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                sepItems.Add(new DisasmItem
                                {
                                    Offset = item.Offset,
                                    Length = 0,
                                    HexBytes = "",
                                    Description = $"loc_{item.Offset:X8}:",
                                    Section = item.Section,
                                    TargetOffset = srcOffset
                                });
                                addedLocs.Add(item.Offset);
                            }
                        }
                    }
                    else
                    {
                        if (addedLocs.Add(item.Offset))
                        {
                            sepItems.Add(new DisasmItem
                            {
                                Offset = item.Offset,
                                Length = 0,
                                HexBytes = "",
                                Description = $"loc_{item.Offset:X8}:",
                                Section = item.Section,
                                TargetOffset = srcs[0]
                            });
                        }
                    }
                }
            }
            items.AddRange(sepItems);

            // 序列化编号以确保排序的稳定性
            for (int i = 0; i < items.Count; i++)
            {
                items[i].SequenceNo = i;
            }

            // 排序与填充物理地址空白
            items.Sort((a, b) => {
                if (a.Offset != b.Offset)
                    return a.Offset.CompareTo(b.Offset);
                
                int GetCategory(DisasmItem item)
                {
                    if (item.Description.StartsWith(";")) return 0;
                    if (item.Description.StartsWith("loc_")) return 1;
                    return 2;
                }
                
                int catA = GetCategory(a);
                int catB = GetCategory(b);
                if (catA != catB)
                    return catA.CompareTo(catB);
                
                return a.SequenceNo.CompareTo(b.SequenceNo);
            });

            var finalItems = new List<DisasmItem>();
            int currPos = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                // [DEFAULT] 虚拟条目：无物理字节，直接透传到最终列表
                if (item.Length == 0 && item.Description.StartsWith("[DEFAULT]"))
                {
                    finalItems.Add(item);
                    continue;
                }

                if (item.Offset < currPos)
                    continue;

                // 缝合物理间隙 Gap (db/align 4)
                if (item.Offset > currPos)
                {
                    int gapLen = item.Offset - currPos;
                    int p = currPos;

                    while (gapLen > 0)
                    {
                        int chunkLen = (gapLen >= 4 && p % 4 == 0) ? 4 : 1;
                        byte[] chunk = new byte[chunkLen];
                        Array.Copy(data, p, chunk, 0, chunkLen);

                        string hexStr = "";
                        string codeStr = "";
                        if (chunkLen == 4)
                        {
                            hexStr = $"{chunk[0]:X2} {chunk[1]:X2} {chunk[2]:X2} {chunk[3]:X2}";
                            codeStr = $"align 4    // db {chunk[0]:X2}, {chunk[1]:X2}, {chunk[2]:X2}, {chunk[3]:X2}";
                        }
                        else
                        {
                            hexStr = $"{chunk[0]:X2}";
                            codeStr = $"db {chunk[0]:X2}";
                        }

                        finalItems.Add(new DisasmItem
                        {
                            Offset = p,
                            Length = chunkLen,
                            HexBytes = hexStr,
                            Description = codeStr
                        });

                        p += chunkLen;
                        gapLen -= chunkLen;
                    }
                }

                // 填充当前解析块的 Hex 物理数据
                byte[] itemBytes = new byte[item.Length];
                Array.Copy(data, item.Offset, itemBytes, 0, item.Length);

                if (item.Length <= 12)
                {
                    var sb = new StringBuilder();
                    for (int b = 0; b < itemBytes.Length; b++)
                    {
                        sb.Append($"{itemBytes[b]:X2} ");
                    }
                    item.HexBytes = sb.ToString().Trim();
                    finalItems.Add(item);
                }
                else
                {
                    int totalLen = item.Length;
                    int bytesProcessed = 0;
                    bool isFirst = true;

                    while (bytesProcessed < totalLen)
                    {
                        int chunkLen = Math.Min(12, totalLen - bytesProcessed);
                        var sb = new StringBuilder();
                        for (int b = 0; b < chunkLen; b++)
                        {
                            sb.Append($"{itemBytes[bytesProcessed + b]:X2} ");
                        }

                        var subItem = new DisasmItem
                        {
                            Offset = item.Offset + bytesProcessed,
                            Length = chunkLen,
                            HexBytes = sb.ToString().Trim(),
                            Description = isFirst ? item.Description : "",
                            Section = item.Section,
                            TargetOffset = isFirst ? item.TargetOffset : null
                        };

                        finalItems.Add(subItem);
                        bytesProcessed += chunkLen;
                        isFirst = false;
                    }
                }

                currPos = item.Offset + item.Length;
            }

            // 补齐尾部
            if (currPos < data.Length)
            {
                int gapLen = data.Length - currPos;
                int p = currPos;
                while (gapLen > 0)
                {
                    int chunkLen = (gapLen >= 4 && p % 4 == 0) ? 4 : 1;
                    byte[] chunk = new byte[chunkLen];
                    Array.Copy(data, p, chunk, 0, chunkLen);

                    string hexStr = "";
                    string codeStr = "";
                    if (chunkLen == 4)
                    {
                        hexStr = $"{chunk[0]:X2} {chunk[1]:X2} {chunk[2]:X2} {chunk[3]:X2}";
                        codeStr = $"align 4    // db {chunk[0]:X2}, {chunk[1]:X2}, {chunk[2]:X2}, {chunk[3]:X2}";
                    }
                    else
                    {
                        hexStr = $"{chunk[0]:X2}";
                        codeStr = $"db {chunk[0]:X2}";
                    }

                    finalItems.Add(new DisasmItem
                    {
                        Offset = p,
                        Length = chunkLen,
                        HexBytes = hexStr,
                        Description = codeStr
                    });

                    p += chunkLen;
                    gapLen -= chunkLen;
                }
            }

            // 为所有行分配物理代码段，通过向后填充确保每个物理块和空隙都能继承正确的段前缀
            string currentSection = ".header";
            foreach (var item in finalItems)
            {
                if (!string.IsNullOrEmpty(item.Section) && item.Section != ".unknown")
                {
                    currentSection = item.Section;
                }
                else
                {
                    item.Section = currentSection;
                }
            }

            _currentAttrMapping = null;
            return finalItems;
        }

        // 通用 Table Walk 解析方法
        private static void WalkTable(byte[] data, int pos, string typeName, string path, Queue<WalkTask> queue, List<DisasmItem> items, ref int tilesOffset, ref int attributesOffset, ref int reinforcePointsOffset)
        {
            if (pos < 0 || pos + 4 > data.Length)
                return;

            int vtableRel = BitConverter.ToInt32(data, pos);
            int vtablePos = pos - vtableRel;
            if (vtablePos < 0 || vtablePos + 4 > data.Length)
                return;

            ushort vtableSize = BitConverter.ToUInt16(data, vtablePos);
            ushort tableSize = BitConverter.ToUInt16(data, vtablePos + 2);

            string section = GetSectionPrefix(pos, path);

            // 记录 VTable 元数据
            items.Add(new DisasmItem
            {
                Offset = vtablePos,
                Length = 2,
                Description = $"{typeName}_vtable.size: {vtableSize}".PadRight(70) + "; 虚拟表大小 (字段表头长度)",
                Section = section
            });
            items.Add(new DisasmItem
            {
                Offset = vtablePos + 2,
                Length = 2,
                Description = $"{typeName}_vtable.table_size: {tableSize}".PadRight(70) + "; 实例数据区大小",
                Section = section
            });

            // 记录表自身的 vtable 指针
            items.Add(new DisasmItem
            {
                Offset = pos,
                Length = 4,
                Description = $"{path}.vtable_offset: {vtableRel} (vtable at 0x{vtablePos:X8})",
                TargetOffset = vtablePos,
                Section = section
            });

            var schema = BtlSchema.GetSchema(typeName);
            int numFields = (vtableSize - 4) / 2;

            for (int i = 0; i < numFields; i++)
            {
                int vtableFieldOffset = 4 + i * 2;
                if (vtablePos + vtableFieldOffset + 2 > data.Length)
                    break;

                ushort off = BitConverter.ToUInt16(data, vtablePos + vtableFieldOffset);
                string fieldName = $"field_{i}";
                string fieldType = "unknown";
                string comment = "";
                string enumName = "";

                if (schema.ContainsKey(i))
                {
                    fieldName = schema[i].Name;
                    fieldType = schema[i].Type;
                    comment = schema[i].Comment;
                    enumName = schema[i].EnumName;
                }

                // 渲染 VTable 槽描述
                string commentSuffix = string.IsNullOrEmpty(comment) ? "" : " ; " + comment;
                items.Add(new DisasmItem
                {
                    Offset = vtablePos + vtableFieldOffset,
                    Length = 2,
                    Description = $"{typeName}_vtable.{fieldName}_offset: {off}{commentSuffix}",
                    Section = section
                });

                if (off == 0)
                    continue;

                int absPos = pos + off;
                string fieldPath = $"{path}.{fieldName}";

                // 物理向量地址动态追踪 (基于路径，不再依赖硬编码字段ID)
                if (fieldPath.Equals("Root.map_terrain.tiles", StringComparison.OrdinalIgnoreCase))
                {
                    tilesOffset = GetFieldPointer(data, pos, vtableFieldOffset);
                }
                else if (fieldPath.Equals("Root.map_terrain.attributes", StringComparison.OrdinalIgnoreCase))
                {
                    attributesOffset = GetFieldPointer(data, pos, vtableFieldOffset);
                }
                else if (fieldPath.Equals("Root.battle_info.reinforce_points", StringComparison.OrdinalIgnoreCase))
                {
                    reinforcePointsOffset = GetFieldPointer(data, pos, vtableFieldOffset);
                }

                WalkField(data, absPos, fieldType, fieldName, fieldPath, enumName, pos, vtableFieldOffset, queue, items, section);
            }

            // === 补全省略字段：FlatBuffers 会省略值为 0/false/null 的字段 ===
            // 遍历 schema 中所有已定义的字段，找出在 vtable 中不存在或 offset == 0 的条目
            if (schema.Count > 0)
            {
                foreach (var kvp in schema)
                {
                    int fid = kvp.Key;
                    var fi = kvp.Value;
                    int vtSlot = 4 + fid * 2;

                    // 判断该字段是否在 vtable 中实际存在且 offset != 0
                    bool present = false;
                    if (vtSlot + 2 <= vtableSize && vtablePos + vtSlot + 2 <= data.Length)
                    {
                        ushort slotOff = BitConverter.ToUInt16(data, vtablePos + vtSlot);
                        if (slotOff != 0)
                            present = true;
                    }

                    if (!present)
                    {
                        // 生成默认值描述
                        string defVal = "0";
                        if (fi.Type == "bool") defVal = "false";
                        else if (fi.Type == "string") defVal = "null";
                        else if (fi.Type.StartsWith("vector_") || fi.Type.StartsWith("["))
                            defVal = "[] (empty)";
                        else if (BtlSchema.IsTableType(fi.Type))
                            defVal = "null";

                        string commentSuffix = string.IsNullOrEmpty(fi.Comment) ? "" : " ; " + fi.Comment;

                        items.Add(new DisasmItem
                        {
                            Offset = pos, // 附着在表起始位置
                            Length = 0,   // 无实际字节
                            HexBytes = "",
                            Description = $"[DEFAULT] {path}.{fi.Name}: {defVal} ({fi.Type}, id:{fid}){commentSuffix}",
                            Section = section
                        });
                    }
                }
            }
        }

        // 统一获取字段物理相对偏移指针对应的绝对地址
        private static int GetFieldPointer(byte[] data, int tablePos, int vtableFieldOffset)
        {
            if (tablePos < 0 || tablePos + 4 > data.Length)
                return 0;

            int vtableRel = BitConverter.ToInt32(data, tablePos);
            int vtablePos = tablePos - vtableRel;
            if (vtablePos < 0 || vtablePos + 2 > data.Length)
                return 0;

            ushort vtableSize = BitConverter.ToUInt16(data, vtablePos);
            if (vtableFieldOffset >= vtableSize || vtablePos + vtableFieldOffset + 2 > data.Length)
                return 0;

            ushort off = BitConverter.ToUInt16(data, vtablePos + vtableFieldOffset);
            if (off == 0)
                return 0;

            int fieldPos = tablePos + off;
            if (fieldPos < 0 || fieldPos + 4 > data.Length)
                return 0;

            int val32 = BitConverter.ToInt32(data, fieldPos);
            if (val32 == 0)
                return 0;
            return fieldPos + val32;
        }

        // 解析并解析单个 Table 中的字段
        private static void WalkField(byte[] data, int absPos, string fieldType, string fieldName, string fieldPath, string enumName, int tablePos, int vtableFieldOffset, Queue<WalkTask> queue, List<DisasmItem> items, string section)
        {
            if (BtlSchema.IsTableType(fieldType))
            {
                if (absPos + 4 <= data.Length)
                {
                    int val32 = BitConverter.ToInt32(data, absPos);
                    int targetPos = absPos + val32;
                    items.Add(new DisasmItem
                    {
                        Offset = absPos,
                        Length = 4,
                        Description = $"{fieldPath}_offset: {val32}".PadRight(70) + $"; 指向 {fieldName} 物理起始地址",
                        TargetOffset = targetPos,
                        Section = section
                    });
                    queue.Enqueue(new WalkTask { Offset = targetPos, TypeName = fieldType, Path = fieldPath });
                }
            }
            else if (BtlSchema.IsStructType(fieldType))
            {
                WalkStruct(data, absPos, fieldType, fieldPath, items, section);
            }
            else if (fieldType.StartsWith("vector_"))
            {
                string elemType = fieldType.Substring(7);
                WalkVector(data, absPos, elemType, fieldName, fieldPath, queue, items, section);
            }
            else if (fieldType == "string")
            {
                if (absPos + 4 <= data.Length)
                {
                    int val32 = BitConverter.ToInt32(data, absPos);
                    int targetPos = absPos + val32;
                    string strVal = "";
                    if (targetPos + 4 <= data.Length)
                    {
                        int strLen = BitConverter.ToInt32(data, targetPos);
                        if (targetPos + 4 + strLen <= data.Length)
                        {
                            strVal = Encoding.UTF8.GetString(data, targetPos + 4, strLen);
                        }
                    }
                    items.Add(new DisasmItem
                    {
                        Offset = absPos,
                        Length = 4,
                        Description = $"{fieldPath}_offset: {val32}".PadRight(70) + $"; 指向 string \"{strVal}\"",
                        TargetOffset = targetPos,
                        Section = section
                    });
                    
                    if (targetPos + 4 <= data.Length)
                    {
                        int strLen = BitConverter.ToInt32(data, targetPos);
                        items.Add(new DisasmItem
                        {
                            Offset = targetPos,
                            Length = 4 + strLen + 1, // 包含4字节长度和尾部0
                            Description = $"{fieldPath}: \"{strVal}\"",
                            Section = section
                        });
                    }
                }
            }
            else
            {
                int size = BtlSchema.GetTypeSize(fieldType);
                if (absPos + size <= data.Length)
                {
                    object val = ReadScalar(data, absPos, fieldType);
                    string descVal = val?.ToString() ?? "0";

                    if (!string.IsNullOrEmpty(enumName) && val != null)
                    {
                        int intVal = Convert.ToInt32(val);
                        string enumDesc = BtlSchema.GetEnumDescription(enumName, intVal, "");
                        if (!string.IsNullOrEmpty(enumDesc))
                        {
                            descVal = $"{intVal} ({enumDesc})";
                        }
                    }

                    items.Add(new DisasmItem
                    {
                        Offset = absPos,
                        Length = size,
                        Description = $"{fieldPath}: {descVal}",
                        Section = section
                    });
                }
            }
        }

        // 解析内联结构体 (Walk Struct)
        private static void WalkStruct(byte[] data, int absPos, string structType, string path, List<DisasmItem> items, string section)
        {
            var schema = BtlSchema.GetSchema(structType);
            if (schema == null || schema.Count == 0)
                return;

            int currentOffset = 0;
            var sortedIds = new List<int>(schema.Keys);
            sortedIds.Sort();

            foreach (int fid in sortedIds)
            {
                var field = schema[fid];
                string fieldName = field.Name;
                string fieldType = field.Type;
                string comment = field.Comment;
                string enumName = field.EnumName;

                int size = BtlSchema.GetTypeSize(fieldType);
                currentOffset = ((currentOffset + size - 1) / size) * size;

                int fieldAbsPos = absPos + currentOffset;
                if (fieldAbsPos + size > data.Length)
                    return;

                object val = ReadScalar(data, fieldAbsPos, fieldType);
                
                // 对 StackCount 进行 RLE 高位偏移还原适配 (对应 AgentInfo)
                if (structType == "AgentInfo" && fieldName == "stack_count" && val is ushort)
                {
                    val = (ushort)((ushort)val >> 8);
                }

                string descVal = val?.ToString() ?? "0";
                if (!string.IsNullOrEmpty(enumName) && val != null)
                {
                    int intVal = Convert.ToInt32(val);
                    string enumDesc = BtlSchema.GetEnumDescription(enumName, intVal, "");
                    if (!string.IsNullOrEmpty(enumDesc))
                    {
                        descVal = $"{intVal} ({enumDesc})";
                    }
                }

                string commentSuffix = string.IsNullOrEmpty(comment) ? "" : " ; " + comment;
                items.Add(new DisasmItem
                {
                    Offset = fieldAbsPos,
                    Length = size,
                    Description = $"{path}.{fieldName}: {descVal}{commentSuffix}",
                    Section = section
                });

                currentOffset += size;
            }
        }

        // 解析数组向量 (Walk Vector)
        private static void WalkVector(byte[] data, int absPos, string elemType, string fieldName, string fieldPath, Queue<WalkTask> queue, List<DisasmItem> items, string section)
        {
            if (absPos + 4 > data.Length)
                return;

            int val32 = BitConverter.ToInt32(data, absPos);
            int targetPos = absPos + val32;

            items.Add(new DisasmItem
            {
                Offset = absPos,
                Length = 4,
                Description = $"{fieldPath}_offset: {val32}".PadRight(70) + $"; 指向 {fieldName} 数组首地址",
                TargetOffset = targetPos,
                Section = section
            });

            if (targetPos + 4 > data.Length)
                return;

            int count = BitConverter.ToInt32(data, targetPos);
            items.Add(new DisasmItem
            {
                Offset = targetPos,
                Length = 4,
                Description = $"{fieldPath}.length: {count}",
                Section = section
            });

            int elemPos = targetPos + 4;
            bool isTable = BtlSchema.IsTableType(elemType);
            bool isStruct = BtlSchema.IsStructType(elemType);

            for (int i = 0; i < count; i++)
            {
                string elemPath = $"{fieldPath}[{i}]";
                if (isTable)
                {
                    if (elemPos + i * 4 + 4 > data.Length)
                        break;
                    int rel = BitConverter.ToInt32(data, elemPos + i * 4);
                    int tblPos = elemPos + i * 4 + rel;

                    items.Add(new DisasmItem
                    {
                        Offset = elemPos + i * 4,
                        Length = 4,
                        Description = $"{elemPath}_offset_ptr",
                        TargetOffset = tblPos,
                        Section = section
                    });

                    queue.Enqueue(new WalkTask { Offset = tblPos, TypeName = elemType, Path = elemPath });
                }
                else if (isStruct)
                {
                    int size = BtlSchema.GetStructSize(elemType);
                    int structPos = elemPos + i * size;
                    if (structPos + size > data.Length)
                        break;

                    WalkStruct(data, structPos, elemType, elemPath, items, section);
                }
                else if (elemType == "attr")
                {
                    if (elemPos + i * 4 + 4 > data.Length)
                        break;

                    if (_currentAttrMapping != null && _currentAttrMapping.TryGetValue(i, out var mapping))
                    {
                        if (mapping.IsFirstOfTile)
                        {
                            items.Add(new DisasmItem
                            {
                                Offset = elemPos + i * 4,
                                Length = 0,
                                HexBytes = "",
                                Description = "; ---------------------------------------------------------------------------",
                                Section = section
                            });
                            items.Add(new DisasmItem
                            {
                                Offset = elemPos + i * 4,
                                Length = 0,
                                HexBytes = "",
                                Description = $"; Tile ({mapping.TileX}, {mapping.TileY}) Attributes Group",
                                Section = section
                            });
                            items.Add(new DisasmItem
                            {
                                Offset = elemPos + i * 4,
                                Length = 0,
                                HexBytes = "",
                                Description = "; ---------------------------------------------------------------------------",
                                Section = section
                            });
                        }

                        items.Add(new DisasmItem
                        {
                            Offset = elemPos + i * 4,
                            Length = 4,
                            Description = $"{elemPath}: [{data[elemPos + i * 4]}, {data[elemPos + i * 4 + 1]}, {data[elemPos + i * 4 + 2]}, {data[elemPos + i * 4 + 3]}]".PadRight(70) + $"; {mapping.SlotName} (Tile: {mapping.TileX}, {mapping.TileY})",
                            Section = section
                        });
                    }
                    else
                    {
                        items.Add(new DisasmItem
                        {
                            Offset = elemPos + i * 4,
                            Length = 4,
                            Description = $"{elemPath}: [{data[elemPos + i * 4]}, {data[elemPos + i * 4 + 1]}, {data[elemPos + i * 4 + 2]}, {data[elemPos + i * 4 + 3]}]",
                            Section = section
                        });
                    }
                }
                else
                {
                    int size = BtlSchema.GetTypeSize(elemType);
                    int scalarPos = elemPos + i * size;
                    if (scalarPos + size > data.Length)
                        break;

                    object val = ReadScalar(data, scalarPos, elemType);
                    string description;
                    if (fieldPath.Equals("Root.map_terrain.tiles", StringComparison.OrdinalIgnoreCase) && val != null)
                    {
                        ushort usVal = Convert.ToUInt16(val);
                        byte low = (byte)(usVal & 0xFF);
                        byte high = (byte)(usVal >> 8);
                        int t = low & 7;
                        int v = low >> 3;
                        int a1 = (high & 4) != 0 ? 1 : 0;
                        int a2 = (high & 8) != 0 ? 1 : 0;
                        int a3 = (high & 0x10) != 0 ? 1 : 0;
                        description = $"{elemPath}: 0x{usVal:X4} (A1={a1}, A2={a2}, A3={a3}, T={t}, V={v})";
                    }
                    else
                    {
                        description = $"{elemPath}: {val}";
                    }

                    items.Add(new DisasmItem
                    {
                        Offset = scalarPos,
                        Length = size,
                        Description = description,
                        Section = section
                    });
                }
            }
        }

        // 读取通用标量字节值
        private static object ReadScalar(byte[] data, int pos, string type)
        {
            if (pos < 0 || pos >= data.Length)
                return null;

            if (type == "uint8" || type == "ubyte")
                return data[pos];
            if (type == "int8" || type == "byte")
                return (sbyte)data[pos];
            if (type == "bool")
                return data[pos] != 0;

            if (type == "uint16" || type == "ushort")
            {
                if (pos + 2 <= data.Length)
                    return BitConverter.ToUInt16(data, pos);
            }
            if (type == "int16" || type == "short")
            {
                if (pos + 2 <= data.Length)
                    return BitConverter.ToInt16(data, pos);
            }

            if (type == "uint32" || type == "uint")
            {
                if (pos + 4 <= data.Length)
                    return BitConverter.ToUInt32(data, pos);
            }
            if (type == "int32" || type == "int")
            {
                if (pos + 4 <= data.Length)
                    return BitConverter.ToInt32(data, pos);
            }
            if (type == "float")
            {
                if (pos + 4 <= data.Length)
                    return BitConverter.ToSingle(data, pos);
            }

            if (type == "uint64")
            {
                if (pos + 8 <= data.Length)
                    return BitConverter.ToUInt64(data, pos);
            }
            if (type == "int64")
            {
                if (pos + 8 <= data.Length)
                    return BitConverter.ToInt64(data, pos);
            }
            if (type == "double")
            {
                if (pos + 8 <= data.Length)
                    return BitConverter.ToDouble(data, pos);
            }

            return null;
        }

        // 根据当前的路径和偏移动态推导代码区段
        public static string GetSectionPrefix(int offset, string path)
        {
            if (offset < 0x50 || path.Contains("entry_point") || path == "Root")
                return ".header";

            string topField = "";
            var parts = path.Split('.');
            if (parts.Length > 1)
            {
                topField = parts[1];
                int bracketIdx = topField.IndexOf('[');
                if (bracketIdx > 0)
                {
                    topField = topField.Substring(0, bracketIdx);
                }
            }

            if (string.IsNullOrEmpty(topField))
                return ".unknown";

            switch (topField.ToLower())
            {
                case "map_terrain":
                    return ".terrain";
                case "stage_metadata":
                    return ".metadata";
                case "battle_info":
                    return ".battle";
                case "faction_info":
                    return ".faction";
                case "trigger_info":
                    return ".trigger";
                case "ai_info":
                    return ".ai";
                case "region_info":
                    return ".region";
                case "stage_config":
                    return ".rules";
                case "decal_info":
                    return ".decal";
                case "view_boundary":
                    return ".boundary";
                default:
                    return "." + topField.ToLower();
            }
        }

        // 提取反汇编块助记名
        private static string GetDisasmBlockName(string desc, int offset)
        {
            if (desc.Contains("vtable_offset"))
            {
                int dotIdx = desc.IndexOf(".vtable_offset");
                if (dotIdx > 0)
                {
                    return desc.Substring(0, dotIdx).Trim();
                }
            }
            if (desc.Contains(".length"))
            {
                int dotIdx = desc.IndexOf(".length");
                if (dotIdx > 0)
                {
                    return desc.Substring(0, dotIdx).Trim() + " Vector";
                }
            }
            return "Table/Segment";
        }


        public static object NormalizeDict(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("_type"))
                {
                    string type = dict["_type"] as string;
                    var normalizedDict = new Dictionary<string, object>();
                    
                    foreach (var kvp in dict)
                    {
                        if (kvp.Key.StartsWith("_"))
                        {
                            normalizedDict[kvp.Key] = NormalizeDict(kvp.Value);
                        }
                    }

                    var activeSchema = BtlSchema.GetSchema(type);
                    var standardSchema = BtlSchema.Schema.ContainsKey(type) ? BtlSchema.Schema[type] : new Dictionary<int, BtlSchema.FieldInfo>();

                    foreach (var kvp in dict)
                    {
                        if (kvp.Key.StartsWith("_")) continue;

                        int fieldId = -1;
                        foreach (var fKvp in activeSchema)
                        {
                            if (fKvp.Value.Name == kvp.Key)
                            {
                                fieldId = fKvp.Key;
                                break;
                            }
                        }

                        string targetKey = kvp.Key;
                        if (fieldId != -1 && standardSchema.ContainsKey(fieldId))
                        {
                            targetKey = standardSchema[fieldId].Name;
                        }

                        normalizedDict[targetKey] = NormalizeDict(kvp.Value);
                    }
                    return normalizedDict;
                }
                else
                {
                    var newDict = new Dictionary<string, object>();
                    foreach (var kvp in dict)
                    {
                        newDict[kvp.Key] = NormalizeDict(kvp.Value);
                    }
                    return newDict;
                }
            }
            else if (obj is System.Collections.IList list)
            {
                var newList = new List<object>();
                foreach (var item in list)
                {
                    newList.Add(NormalizeDict(item));
                }
                return newList;
            }
            return obj;
        }

        // =====================================================================
        // === 编辑器核心功能: 偏移收集、空间插入与重算、就地十六进制修改 ===
        // =====================================================================

        /// <summary>
        /// 偏移字段描述结构体，记录文件中每个偏移指针的位置、目标和类型。
        /// </summary>
        public struct OffsetFieldRecord
        {
            /// <summary>偏移字段在文件中的绝对地址</summary>
            public int FieldPos;
            /// <summary>偏移字段指向的目标绝对地址</summary>
            public int TargetPos;
            /// <summary>偏移类型: "absolute"=绝对指针, "vtable"=虚表回退指针, "relative"=正向相对指针</summary>
            public string OffsetType;
        }

        /// <summary>
        /// 遍历 FlatBuffer 二进制文件，收集所有偏移指针字段。
        /// 返回列表中的每个 OffsetFieldRecord 记录了：字段地址(F)、目标地址(T)和偏移类型。
        /// </summary>
        public static List<OffsetFieldRecord> CollectAllOffsetFields(byte[] data)
        {
            var records = new List<OffsetFieldRecord>();
            if (data == null || data.Length < 4)
                return records;

            var visited = new HashSet<int>();

            // 1. 文件头: 绝对偏移，指向 Root Table
            int rootPos = BitConverter.ToInt32(data, 0);
            records.Add(new OffsetFieldRecord
            {
                FieldPos = 0,
                TargetPos = rootPos,
                OffsetType = "absolute"
            });

            // 2. BFS 遍历所有表
            var queue = new Queue<(int pos, string typeName)>();
            queue.Enqueue((rootPos, "Root"));

            while (queue.Count > 0)
            {
                var (pos, typeName) = queue.Dequeue();
                if (pos <= 0 || pos >= data.Length || !visited.Add(pos))
                    continue;

                if (pos + 4 > data.Length)
                    continue;

                // 表自身的 vtable 指针 (反向相对偏移)
                int vtableRel = BitConverter.ToInt32(data, pos);
                int vtablePos = pos - vtableRel;
                if (vtablePos < 0 || vtablePos + 4 > data.Length)
                    continue;

                records.Add(new OffsetFieldRecord
                {
                    FieldPos = pos,
                    TargetPos = vtablePos,
                    OffsetType = "vtable"
                });

                ushort vtableSize = BitConverter.ToUInt16(data, vtablePos);
                int numFields = (vtableSize - 4) / 2;

                var schema = BtlSchema.GetSchema(typeName);

                for (int i = 0; i < numFields; i++)
                {
                    int vtableFieldOffset = 4 + i * 2;
                    if (vtablePos + vtableFieldOffset + 2 > data.Length)
                        break;

                    ushort off = BitConverter.ToUInt16(data, vtablePos + vtableFieldOffset);
                    if (off == 0)
                        continue;

                    int absPos = pos + off;
                    string fieldType = "unknown";
                    if (schema.ContainsKey(i))
                        fieldType = schema[i].Type;

                    if (BtlSchema.IsTableType(fieldType))
                    {
                        // 子表: 正向相对偏移
                        if (absPos + 4 <= data.Length)
                        {
                            int val32 = BitConverter.ToInt32(data, absPos);
                            int targetPos = absPos + val32;
                            records.Add(new OffsetFieldRecord
                            {
                                FieldPos = absPos,
                                TargetPos = targetPos,
                                OffsetType = "relative"
                            });
                            queue.Enqueue((targetPos, fieldType));
                        }
                    }
                    else if (fieldType == "string")
                    {
                        // 字符串: 正向相对偏移
                        if (absPos + 4 <= data.Length)
                        {
                            int val32 = BitConverter.ToInt32(data, absPos);
                            int targetPos = absPos + val32;
                            records.Add(new OffsetFieldRecord
                            {
                                FieldPos = absPos,
                                TargetPos = targetPos,
                                OffsetType = "relative"
                            });
                        }
                    }
                    else if (fieldType.StartsWith("vector_"))
                    {
                        string elemType = fieldType.Substring(7);
                        // 向量指针: 正向相对偏移
                        if (absPos + 4 <= data.Length)
                        {
                            int val32 = BitConverter.ToInt32(data, absPos);
                            int vecPos = absPos + val32;
                            records.Add(new OffsetFieldRecord
                            {
                                FieldPos = absPos,
                                TargetPos = vecPos,
                                OffsetType = "relative"
                            });

                            // 如果向量元素是表类型，还需要遍历每个元素的偏移
                            if (BtlSchema.IsTableType(elemType) && vecPos + 4 <= data.Length)
                            {
                                int count = BitConverter.ToInt32(data, vecPos);
                                int elemStart = vecPos + 4;
                                for (int j = 0; j < count; j++)
                                {
                                    int elemSlot = elemStart + j * 4;
                                    if (elemSlot + 4 > data.Length)
                                        break;
                                    int rel = BitConverter.ToInt32(data, elemSlot);
                                    int tblPos = elemSlot + rel;
                                    records.Add(new OffsetFieldRecord
                                    {
                                        FieldPos = elemSlot,
                                        TargetPos = tblPos,
                                        OffsetType = "relative"
                                    });
                                    queue.Enqueue((tblPos, elemType));
                                }
                            }
                        }
                    }
                    // 标量和结构体类型不包含偏移指针，无需处理
                }
            }

            return records;
        }

        /// <summary>
        /// 在 insertOffset 处插入 insertLength 个 fillByte 字节，并自动重算所有 FlatBuffer 偏移值。
        /// 返回完整的新字节数组。
        /// </summary>
        public static byte[] RecalculateOffsetsAndInsert(byte[] originalData, int insertOffset, int insertLength, byte fillByte = 0)
        {
            if (originalData == null || originalData.Length < 4)
                throw new Exception("原始数据为空或太短");
            if (insertOffset < 0 || insertOffset > originalData.Length)
                throw new Exception($"插入偏移 0x{insertOffset:X} 超出文件范围 [0, 0x{originalData.Length:X}]");
            if (insertLength <= 0)
                throw new Exception("插入长度必须大于 0");

            // 1. 收集所有偏移字段
            var records = CollectAllOffsetFields(originalData);

            // 2. 创建新的字节数组，插入空白区域
            int newLength = originalData.Length + insertLength;
            byte[] newData = new byte[newLength];

            // 复制插入点之前的数据
            if (insertOffset > 0)
                Array.Copy(originalData, 0, newData, 0, insertOffset);

            // 填充插入的空白区域
            for (int i = 0; i < insertLength; i++)
                newData[insertOffset + i] = fillByte;

            // 复制插入点之后的数据
            if (insertOffset < originalData.Length)
                Array.Copy(originalData, insertOffset, newData, insertOffset + insertLength, originalData.Length - insertOffset);

            // 3. 重算所有偏移值
            foreach (var rec in records)
            {
                int F = rec.FieldPos;
                int T = rec.TargetPos;

                // 计算新的字段地址和目标地址
                int Fp = (F >= insertOffset) ? F + insertLength : F;
                int Tp = (T >= insertOffset) ? T + insertLength : T;

                switch (rec.OffsetType)
                {
                    case "absolute":
                        // 文件头绝对指针: 直接写入新目标地址
                        BitConverter.GetBytes(Tp).CopyTo(newData, Fp);
                        break;

                    case "vtable":
                        // 虚拟表回退指针: 存储值 = table_pos - vtable_pos = Fp - Tp
                        int vtableRelNew = Fp - Tp;
                        BitConverter.GetBytes(vtableRelNew).CopyTo(newData, Fp);
                        break;

                    case "relative":
                        // 正向相对偏移: 存储值 = target_pos - field_pos = Tp - Fp
                        int relNew = Tp - Fp;
                        BitConverter.GetBytes(relNew).CopyTo(newData, Fp);
                        break;
                }
            }

            return newData;
        }

        /// <summary>
        /// 就地修改文件数据中指定偏移处的字节。
        /// </summary>
        public static void UpdateHexValue(byte[] fileData, int offset, byte[] newBytes)
        {
            if (fileData == null)
                throw new Exception("文件数据为空");
            if (offset < 0 || offset + newBytes.Length > fileData.Length)
                throw new Exception($"修改范围 [0x{offset:X}, 0x{offset + newBytes.Length:X}) 超出文件范围 [0, 0x{fileData.Length:X})");

            Array.Copy(newBytes, 0, fileData, offset, newBytes.Length);
        }

        /// <summary>
        /// 根据路径在 Schema 中解析字段类型、注释和枚举名。
        /// </summary>
        public static void ResolveFieldTypeAndComment(string pathAndField, out string type, out string comment, out string enumName)
        {
            type = "unknown";
            comment = "";
            enumName = "";

            // 移除路径中的数组下标 (例如 [0], [12])
            string cleanPath = System.Text.RegularExpressions.Regex.Replace(pathAndField, @"\[\d+\]", "");
            string[] parts = cleanPath.Split('.');

            if (parts.Length == 0 || parts[0] != "Root")
                return;

            string currentType = "Root";
            for (int i = 1; i < parts.Length; i++)
            {
                string fieldName = parts[i];
                var schema = BtlSchema.GetSchema(currentType);
                bool found = false;

                foreach (var kvp in schema)
                {
                    if (kvp.Value.Name == fieldName)
                    {
                        currentType = kvp.Value.Type;
                        if (i == parts.Length - 1)
                        {
                            type = currentType;
                            comment = kvp.Value.Comment;
                            enumName = kvp.Value.EnumName;
                        }
                        else if (currentType.StartsWith("vector_"))
                        {
                            currentType = currentType.Substring(7);
                        }
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return;
            }
        }

        /// <summary>
        /// 通用的克隆并追加向量元素方法。支持表向量、结构体向量和标量向量。
        /// </summary>
        public static byte[] CloneAndAppendVectorElement(byte[] originalData, int vectorOffset, int templateElementIndex, string elemType)
        {
            if (originalData == null || originalData.Length < 4)
                throw new Exception("文件数据无效");

            int count = BitConverter.ToInt32(originalData, vectorOffset);
            if (count <= 0)
                throw new Exception("不能克隆空向量中的元素");

            bool isTable = BtlSchema.IsTableType(elemType);
            bool isStruct = BtlSchema.IsStructType(elemType);

            // 1. 在做任何修改前，收集原文件中的所有偏移字段记录
            var records = CollectAllOffsetFields(originalData);

            byte[] newData = null;
            int insertPos = 0;
            int insertLength = 0;

            if (isTable)
            {
                // 获取模板元素的 VTable 和 Table 偏移和大小
                int slotPos = vectorOffset + 4 + templateElementIndex * 4;
                int rel = BitConverter.ToInt32(originalData, slotPos);
                int tablePos = slotPos + rel;

                int vtableRel = BitConverter.ToInt32(originalData, tablePos);
                int vtablePos = tablePos - vtableRel;

                ushort vtableSize = BitConverter.ToUInt16(originalData, vtablePos);
                ushort tableSize = BitConverter.ToUInt16(originalData, vtablePos + 2);

                insertPos = vectorOffset + 4 + count * 4;
                insertLength = 4;

                // 创建新数组：原大小 + 插入的4字节指针 + 追加的 VTable + 追加的 Table
                newData = new byte[originalData.Length + insertLength + vtableSize + tableSize];

                // 复制插入点之前的数据
                Array.Copy(originalData, 0, newData, 0, insertPos);
                // 复制插入点之后的数据到新文件的移位位置
                Array.Copy(originalData, insertPos, newData, insertPos + insertLength, originalData.Length - insertPos);

                // 在文件尾追加 VTable 和 Table
                int newVtablePos = originalData.Length + insertLength;
                int newTablePos = newVtablePos + vtableSize;

                Array.Copy(originalData, vtablePos, newData, newVtablePos, vtableSize);
                Array.Copy(originalData, tablePos, newData, newTablePos, tableSize);

                // 更新新 Table 中的 vtable 相对偏移量
                int newVtableRel = newTablePos - newVtablePos;
                BitConverter.GetBytes(newVtableRel).CopyTo(newData, newTablePos);

                // 更新新追加的向量元素的相对偏移量
                int newElementRel = newTablePos - insertPos;
                BitConverter.GetBytes(newElementRel).CopyTo(newData, insertPos);
            }
            else
            {
                int elementSize = isStruct ? BtlSchema.GetStructSize(elemType) : BtlSchema.GetTypeSize(elemType);
                if (elementSize <= 0)
                    throw new Exception($"无法识别的元素类型大小: {elemType}");

                // 获取模板元素字节
                int templatePos = vectorOffset + 4 + templateElementIndex * elementSize;
                byte[] elementBytes = new byte[elementSize];
                Array.Copy(originalData, templatePos, elementBytes, 0, elementSize);

                insertPos = vectorOffset + 4 + count * elementSize;
                insertLength = elementSize;

                // 创建新数组：原大小 + 插入的数据字节
                newData = new byte[originalData.Length + insertLength];
                Array.Copy(originalData, 0, newData, 0, insertPos);
                Array.Copy(elementBytes, 0, newData, insertPos, elementSize);
                Array.Copy(originalData, insertPos, newData, insertPos + insertLength, originalData.Length - insertPos);
            }

            // 更新新数组中的向量元素 count（向量首地址）
            int newCount = count + 1;
            int newVectorOffset = (vectorOffset >= insertPos) ? vectorOffset + insertLength : vectorOffset;
            BitConverter.GetBytes(newCount).CopyTo(newData, newVectorOffset);

            // 2. 根据收集到的旧偏移记录，重算并写入所有偏移到新数组
            foreach (var rec in records)
            {
                int F = rec.FieldPos;
                int T = rec.TargetPos;

                int Fp = (F >= insertPos) ? F + insertLength : F;
                int Tp = (T >= insertPos) ? T + insertLength : T;

                switch (rec.OffsetType)
                {
                    case "absolute":
                        BitConverter.GetBytes(Tp).CopyTo(newData, Fp);
                        break;
                    case "vtable":
                        int vtableRelNew = Fp - Tp;
                        BitConverter.GetBytes(vtableRelNew).CopyTo(newData, Fp);
                        break;
                    case "relative":
                        int relNew = Tp - Fp;
                        BitConverter.GetBytes(relNew).CopyTo(newData, Fp);
                        break;
                }
            }

            return newData;
        }
    }
}
