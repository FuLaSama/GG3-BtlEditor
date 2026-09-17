/*
 * BtlSimLoader.cs
 * 
 * 本文件是 BTL 模拟加载与崩溃检测校验器（Simulator & Validator）：
 * - 规则引擎: 模拟游戏引擎在载入关卡 BTL 时的底层操作步骤（例如边界检查、数组分配），验证各区块指针健康状况。
 * - 缺陷识别 (Simulate): 对加载的数据进行细致检测：包括 Tiles 地图网格总数验证、激活槽位的 Attributes 个数是否越界（越界将导致引擎闪退）、部队 AgentId 的合理性及唯一性、部队堆叠层数校验（1-4）、最大 HP 不为 0 验证以及触发事件格子索引范围检查。
 * - 崩溃评估: 评估配置是否存在严重的致命缺陷并输出格式化的诊断报告，帮助地图制作者 100% 规避进游戏闪退的问题。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BtlVerifier
{
    /// <summary>
    /// BTL 模拟加载与崩溃检测环境 (Simulator & Validator)
    /// </summary>
    public static class BtlSimLoader
    {
        public class LogItem
        {
            public string Type { get; set; } // "INFO", "WARN", "ERROR", "FATAL"
            public string Component { get; set; } // "MapTerrain", "AIInfo", "TriggerInfo", etc.
            public string Message { get; set; }
        }

        public static List<LogItem> Simulate(StageModel stage, out bool hasCrash)
        {
            var logs = new List<LogItem>();
            bool isCrash = false;

            void LogInfo(string comp, string msg) => logs.Add(new LogItem { Type = "INFO", Component = comp, Message = msg });
            void LogWarn(string comp, string msg) => logs.Add(new LogItem { Type = "WARN", Component = comp, Message = msg });
            void LogError(string comp, string msg) { logs.Add(new LogItem { Type = "ERROR", Component = comp, Message = msg }); isCrash = true; }
            void LogFatal(string comp, string msg) { logs.Add(new LogItem { Type = "FATAL", Component = comp, Message = msg }); isCrash = true; }

            if (stage == null)
            {
                LogFatal("Root", "StageModel 为空，反编译解析失败");
                hasCrash = isCrash;
                return logs;
            }

            LogInfo("Root", $"开始模拟加载关卡 BTL: {stage.File ?? "未指定路径"}, 引擎版本: {stage.Version}");

            // 1. 版本检查
            if (stage.Version != 1)
            {
                LogError("Root", $"不支持的 BTL 版本: {stage.Version} (游戏引擎仅支持 version = 1)");
            }

            // 2. 基础配置与元数据检查
            if (stage.StageMetadata == null)
            {
                LogError("StageMetadata", "未找到 stage_metadata 节点");
            }
            else
            {
                LogInfo("StageMetadata", "stage_metadata 节点加载成功");
            }

            if (stage.BattleInfo == null)
            {
                LogError("BattleInfo", "未找到 battle_info 节点");
            }
            else
            {
                LogInfo("BattleInfo", "battle_info 节点加载成功");
            }

            // 3. 地图与地形配置 (MapTerrain)
            int width = 0, height = 0, totalCells = 0;
            if (stage.MapTerrain == null)
            {
                LogFatal("MapTerrain", "未找到 map_terrain 节点，地形加载中断");
                hasCrash = isCrash;
                return logs;
            }

            var size = stage.MapTerrain.Size;
            if (size == null)
            {
                LogFatal("MapTerrain", "未找到 map_terrain.size 节点，地图大小未定义");
                hasCrash = isCrash;
                return logs;
            }

            width = size.Width;
            height = size.Height;
            totalCells = width * height;
            LogInfo("MapTerrain", $"地图物理大小: {width} x {height} = {totalCells} 格");

            // 区域合理性
            if (size.PlayableWidth > width || size.PlayableHeight > height)
            {
                LogError("MapTerrain", $"可玩区域大小 ({size.PlayableWidth}x{size.PlayableHeight}) 超出地图物理大小 ({width}x{height})");
            }
            if (size.LeftMargin + size.PlayableWidth > width)
            {
                LogError("MapTerrain", $"左侧留白 LeftMargin ({size.LeftMargin}) + PlayableWidth ({size.PlayableWidth}) 超出地图总宽度 ({width})");
            }
            if (size.TopMargin + size.PlayableHeight > height)
            {
                LogError("MapTerrain", $"顶部留白 TopMargin ({size.TopMargin}) + PlayableHeight ({size.PlayableHeight}) 超出地图总高度 ({height})");
            }

            // 地形 Tiles 长度检查
            if (stage.MapTerrain.Tiles == null)
            {
                LogFatal("MapTerrain", "地形地表数据 tiles 数组为空");
            }
            else if (stage.MapTerrain.Tiles.Count != totalCells)
            {
                LogFatal("MapTerrain", $"地表数据 tiles 数组长度 ({stage.MapTerrain.Tiles.Count}) 与地图计算面积 ({totalCells}) 不匹配！游戏加载必闪退");
            }
            else
            {
                LogInfo("MapTerrain", $"地表 tiles 数组验证成功 (长度: {stage.MapTerrain.Tiles.Count})");
            }

            // 地形 Attributes 长度检查
            if (stage.MapTerrain.Attributes == null)
            {
                LogError("MapTerrain", "地表属性数据 attributes 数组为空");
            }
            else
            {
                // 计算 tiles 中所有地块激活的槽位总数 (A1, A2, A3)
                int requiredAttrCount = 0;
                if (stage.MapTerrain.Tiles != null)
                {
                    foreach (var tile in stage.MapTerrain.Tiles)
                    {
                        byte high = (byte)(tile >> 8);
                        if ((high & 4) != 0) requiredAttrCount++;
                        if ((high & 8) != 0) requiredAttrCount++;
                        if ((high & 0x10) != 0) requiredAttrCount++;
                    }
                }

                if (stage.MapTerrain.Attributes.Count < requiredAttrCount)
                {
                    LogFatal("MapTerrain", $"地表属性 attributes 数组长度 ({stage.MapTerrain.Attributes.Count}) 小于地块激活的槽位总数 ({requiredAttrCount})！游戏读取越界必闪退");
                }
                else
                {
                    LogInfo("MapTerrain", $"地表 attributes 数组验证成功 (长度: {stage.MapTerrain.Attributes.Count}, 地块激活槽位: {requiredAttrCount})");
                }
            }

            // 4. 势力配置 (FactionInfo)
            var activeFactions = new HashSet<ushort>();
            if (stage.FactionInfo == null || stage.FactionInfo.Factions == null)
            {
                LogError("FactionInfo", "未找到 faction_info 节点或势力列表为空");
            }
            else
            {
                LogInfo("FactionInfo", $"共加载 {stage.FactionInfo.Factions.Count} 个阵营势力");
                for (int i = 0; i < stage.FactionInfo.Factions.Count; i++)
                {
                    var f = stage.FactionInfo.Factions[i];
                    if (f.Info == null)
                    {
                        LogError("FactionInfo", $"阵营 #{i} 的 Info 数据为空");
                        continue;
                    }
                    ushort fid = f.Info.FactionId;
                    if (activeFactions.Contains(fid))
                    {
                        LogError("FactionInfo", $"发现重复的 FactionId: {fid}");
                    }
                    activeFactions.Add(fid);

                    LogInfo("FactionInfo", $"阵营 #{i} -> ID: {fid}, 国家ID: {f.Info.CountryId}, AI控制: {f.Info.IsAI == 1}, 初始金币: {f.Info.InitialGold}");
                }
            }

            // 4.5 战斗初始化与增员部署点检查 (BattleInfo.ReinforcePoints)
            if (stage.BattleInfo == null || stage.BattleInfo.ReinforcePoints == null)
            {
                LogWarn("BattleInfo", "未找到 battle_info.reinforce_points 节点或增员列表为空");
            }
            else
            {
                LogInfo("BattleInfo", $"共加载 {stage.BattleInfo.ReinforcePoints.Count} 个增员部署点");
                for (int i = 0; i < stage.BattleInfo.ReinforcePoints.Count; i++)
                {
                    var pt = stage.BattleInfo.ReinforcePoints[i];
                    if (pt == null)
                    {
                        LogError("BattleInfo", $"增员部署点 #{i} 为空");
                        continue;
                    }

                    if (pt.CellIdx >= totalCells)
                    {
                        LogFatal("BattleInfo", $"增员部署点 #{i} 的 CellIdx ({pt.CellIdx}) 超出地图边界 ({totalCells - 1})！游戏加载必闪退");
                    }
                }
            }

            // 5. 部队与 AI 部署 (AIInfo)
            if (stage.AIInfo == null)
            {
                LogWarn("AIInfo", "未找到 ai_info 节点，关卡无 AI/玩家部队部署数据");
            }
            else
            {
                if (stage.AIInfo.Agents == null)
                {
                    LogInfo("AIInfo", "AIAgents 部队列表为空");
                }
                else
                {
                    LogInfo("AIInfo", $"共加载 {stage.AIInfo.Agents.Count} 支部队");
                    var agentIds = new HashSet<ushort>();
                    for (int i = 0; i < stage.AIInfo.Agents.Count; i++)
                    {
                        var agent = stage.AIInfo.Agents[i];
                        if (agent.AgentInfo == null)
                        {
                            LogError("AIInfo", $"部队 #{i} 的 agent_info 结构体为空");
                            continue;
                        }

                        var info = agent.AgentInfo;
                        // 校验坐标索引范围 (65535 代表非部署状态/预备队)
                        if (info.CellIdx != 65535 && info.CellIdx >= totalCells)
                        {
                            LogFatal("AIInfo", $"部队 #{i} (AgentId: {info.AgentId}) 的 CellIdx ({info.CellIdx}) 超出地图边界 ({totalCells - 1})！游戏加载必闪退");
                        }

                        // 校验 AgentId 唯一性与合理性
                        if (agentIds.Contains(info.AgentId))
                        {
                            LogError("AIInfo", $"部队 #{i} 发现重复 of AgentId: {info.AgentId}，会导致行为控制状态覆盖！");
                        }
                        agentIds.Add(info.AgentId);

                        // 警告偏大的 AgentId (可能引起部分引擎数组溢出)
                        if (info.AgentId > 300)
                        {
                            LogWarn("AIInfo", $"部队 #{i} 分配的 AgentId ({info.AgentId}) 偏大。某些旧版本游戏会对此建立固定大小数组，请尽量控制在 255 以内");
                        }

                        // 编队堆叠层数校验 (限制在 1-4 层之间)
                        if (info.StackCount < 1 || info.StackCount > 4)
                        {
                            LogFatal("AIInfo", $"部队 #{i} (AgentId: {info.AgentId}) 的编队层数 stack_count ({info.StackCount}) 无效！游戏仅支持 1 至 4 层，否则加载必闪退");
                        }

                        // 血量校验
                        if (info.MaxHP == 0)
                        {
                            LogFatal("AIInfo", $"部队 #{i} (AgentId: {info.AgentId}) 的最大生命值 MaxHP 不能为 0！游戏除法计算会溢出闪退");
                        }
                        if (info.HP > info.MaxHP)
                        {
                            LogWarn("AIInfo", $"部队 #{i} (AgentId: {info.AgentId}) 当前生命值 HP ({info.HP}) 大于最大生命值 MaxHP ({info.MaxHP})");
                        }
                        if (info.HP == 0)
                        {
                            LogWarn("AIInfo", $"部队 #{i} (AgentId: {info.AgentId}) 当前生命值为 0 (进游戏会直接判定死亡)");
                        }
                    }
                }
            }

            // 6. 触发器事件 (TriggerInfo.events)
            if (stage.TriggerEvents == null || stage.TriggerEvents.Count == 0)
            {
                LogInfo("TriggerInfo", "未找到触发器事件 (TriggerEvents)");
            }
            else
            {
                LogInfo("TriggerInfo", $"共加载 {stage.TriggerEvents.Count} 个触发地块事件 (建筑/工事)");
                for (int i = 0; i < stage.TriggerEvents.Count; i++)
                {
                    var evt = stage.TriggerEvents[i];
                    // 格子索引越界校验
                    if (evt.TileIndex >= totalCells)
                    {
                        LogFatal("TriggerInfo", $"触发事件 #{i} 的格子索引 TileIndex ({evt.TileIndex}) 超出地图边界 ({totalCells - 1})！游戏加载必闪退");
                    }

                    if (evt.EventType == 4) // 建筑
                    {
                        LogInfo("TriggerInfo", $"地块 #{evt.TileIndex} 建筑 -> ID: {evt.BuildingId}, 所有者阵营: {evt.Owner}, 偏移量: ({evt.Dx}, {evt.Dy})");
                    }
                    else if (evt.EventType == 0) // 工事
                    {
                        LogInfo("TriggerInfo", $"地块 #{evt.TileIndex} 工事 -> ID: {evt.FortId}");
                    }
                }
            }

            if (isCrash)
            {
                LogFatal("Root", "❌ 发现严重的配置缺陷！如果直接导入游戏，游戏将会 100% 出现读取闪退！请在编辑器中修正后重新导出");
            }
            else
            {
                LogInfo("Root", "✅ 模拟加载验证通过！未检测到任何会导致游戏崩溃的结构性异常");
            }

            hasCrash = isCrash;
            return logs;
        }
    }
}
