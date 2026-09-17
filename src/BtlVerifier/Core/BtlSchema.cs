/*
 * BtlSchema.cs
 * 
 * 本文件是 BtlVerifier 中用于解析 FlatBuffers 模式定义映射的类：
 * - 提供静态内置和动态外部两种 FBS 架构加载方式。
 * - 动态载入 (LoadFromFbs): 解析 FlatBuffers 结构声明，生成与 BtlToolchain 一致的 `UniqueToFieldName`/`FieldToUniqueName` 动态类型双向映射字典，用于在对 BTL 二进制反编译之后做字段可读化名称翻译。
 */
using System;
using System.Collections.Generic;

namespace BtlVerifier
{
    // 定义 FlatBuffers 中的模式定义映射，帮助底层反序列化程序查找字段名与类型
    public static class BtlSchema
    {
        private static readonly Dictionary<string, string> _displayTypeMap = new Dictionary<string, string>
        {
            {"uint8", "ubyte"}, {"int8", "byte"}, {"uint16", "ushort"}, {"int16", "short"},
            {"uint32", "uint"}, {"int32", "int"}, {"uint64", "ulong"}, {"int64", "long"},
            {"float", "float"}, {"double", "double"}, {"bool", "bool"}, {"string", "string"}
        };

        private static string MapToDisplayType(string fbsType)
        {
            if (_displayTypeMap.TryGetValue(fbsType, out var mapped)) return mapped;
            if (fbsType.StartsWith("vector_")) return "vector";
            return "table";
        }

        public static Dictionary<string, Dictionary<string, string>> FieldToUniqueName { get; private set; }
        public static Dictionary<string, Dictionary<string, string>> UniqueToFieldName { get; private set; }
        public static Dictionary<string, string> GlobalUniqueToField { get; private set; }
        public static Dictionary<string, string> GlobalFieldToUnique { get; private set; }

        static BtlSchema()
        {
            FieldToUniqueName = new Dictionary<string, Dictionary<string, string>>();
            UniqueToFieldName = new Dictionary<string, Dictionary<string, string>>();
            GlobalUniqueToField = new Dictionary<string, string>();
            GlobalFieldToUnique = new Dictionary<string, string>();

            WorkspacePath = FindWorkspaceRoot();
            DefaultFbsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "battle.fbs");

            BuildUniqueNameMappings();
        }

        public static void BuildUniqueNameMappings()
        {
            FieldToUniqueName.Clear();
            UniqueToFieldName.Clear();
            GlobalUniqueToField.Clear();
            GlobalFieldToUnique.Clear();

            var allSchemas = UseDynamic ? DynamicSchema : Schema;
            var globalTableCounters = new Dictionary<string, int>();

            // Sort keys to guarantee deterministic order
            foreach (var tableName in System.Linq.Enumerable.OrderBy(allSchemas.Keys, k => k))
            {
                if (tableName.StartsWith("_")) continue;
                var fields = allSchemas[tableName];

                var fieldMap = new Dictionary<string, string>();
                var uniqueMap = new Dictionary<string, string>();

                var activeCounters = IsStructType(tableName) ? new Dictionary<string, int>() : globalTableCounters;

                var sortedIds = System.Linq.Enumerable.ToList(fields.Keys);
                sortedIds.Sort();

                foreach (int fid in sortedIds)
                {
                    var fi = fields[fid];
                    string displayType = MapToDisplayType(fi.Type);

                    if (!activeCounters.ContainsKey(displayType))
                        activeCounters[displayType] = 0;

                    string uniqueName = $"{displayType}_{activeCounters[displayType]}";
                    activeCounters[displayType]++;

                    fieldMap[fi.Name] = uniqueName;
                    uniqueMap[uniqueName] = fi.Name;

                    GlobalUniqueToField[uniqueName] = fi.Name;
                    GlobalFieldToUnique[fi.Name] = uniqueName;
                }

                FieldToUniqueName[tableName] = fieldMap;
                UniqueToFieldName[tableName] = uniqueMap;
            }
        }

        public struct FieldInfo
        {
            public string Name;
            public string Type;
            public string Comment;
            public string EnumName;

            public FieldInfo(string name, string type, string comment = "", string enumName = "")
            {
                Name = name;
                Type = type;
                Comment = comment ?? "";
                EnumName = enumName ?? "";
            }
        }

        public static readonly Dictionary<string, Dictionary<int, FieldInfo>> Schema = new Dictionary<string, Dictionary<int, FieldInfo>>
        {
            { "Root", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("version", "uint16") },
                { 1, new FieldInfo("map_terrain", "MapTerrain") },
                { 2, new FieldInfo("stage_metadata", "StageMetadata") },
                { 3, new FieldInfo("battle_info", "BattleInfo") },
                { 4, new FieldInfo("faction_info", "FactionInfo") },
                { 5, new FieldInfo("trigger_info", "TriggerInfo") },
                { 6, new FieldInfo("ai_info", "AIInfo") },
                { 7, new FieldInfo("region_info", "RegionInfo") },
                { 8, new FieldInfo("stage_config", "StageConfig") },
                { 9, new FieldInfo("decal_info", "DecalInfo") },
                { 10, new FieldInfo("rle_metadata", "vector_uint8") },
                { 11, new FieldInfo("view_boundary", "ViewBoundary") }
            }},
            { "MapTerrain", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("size", "Size") },
                { 1, new FieldInfo("playable_flag", "uint8") },
                { 2, new FieldInfo("tiles", "vector_uint16") },
                { 3, new FieldInfo("attributes", "vector_TileAttr") },
                { 4, new FieldInfo("fog_or_visibility", "bool") }
            }},
            { "Size", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("width", "uint16") },
                { 1, new FieldInfo("height", "uint16") },
                { 2, new FieldInfo("left_margin", "uint16") },
                { 3, new FieldInfo("top_margin", "uint16") },
                { 4, new FieldInfo("playable_width", "uint16") },
                { 5, new FieldInfo("playable_height", "uint16") }
            }},
            { "StageMetadata", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("targets", "vector_StageTarget") },
                { 1, new FieldInfo("stage_num", "uint16") }
            }},
            { "StageTarget", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("target_type", "uint16") },
                { 1, new FieldInfo("target_value", "int16") },
                { 2, new FieldInfo("param1", "uint16") },
                { 3, new FieldInfo("param2", "uint16") },
                { 4, new FieldInfo("flag", "uint8") }
            }},
            { "BattleInfo", new Dictionary<int, FieldInfo> {
                { 1, new FieldInfo("reinforce_points", "vector_ReinforcePoint") }
            }},
            { "ReinforcePoint", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("cell_idx", "uint16") },
                { 1, new FieldInfo("faction_id", "uint8") },
                { 2, new FieldInfo("is_key_unit", "bool") },
                { 3, new FieldInfo("flag", "uint8") }
            }},
            { "FactionInfo", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("factions", "vector_Faction") },
                { 1, new FieldInfo("faction_cards", "vector_FactionCardsRelation") },
                { 2, new FieldInfo("version", "uint16") },
                { 3, new FieldInfo("faction_limits", "vector_FactionLimitsRelation") }
            }},
            { "Faction", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("info", "FactionMetadata") },
                { 1, new FieldInfo("tiles", "vector_uint8") },
                { 2, new FieldInfo("forces", "vector_FactionForceEntry") },
                { 3, new FieldInfo("assignments", "vector_FactionAssignEntry") },
                { 4, new FieldInfo("detail_config", "FactionDetailConfig") },
                { 5, new FieldInfo("targets", "vector_FactionTargetEntry") },
                { 6, new FieldInfo("stage_num", "FactionStageNum") },
                { 7, new FieldInfo("status", "uint8") },
                { 8, new FieldInfo("config_ref", "uint16") },
                { 9, new FieldInfo("general_flag", "uint8") }
            }},
            { "FactionMetadata", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("faction_id", "uint16") },
                { 1, new FieldInfo("country_id", "uint16") },
                { 2, new FieldInfo("camp", "uint8") },
                { 3, new FieldInfo("is_ai", "uint8") },
                { 4, new FieldInfo("general_limit", "uint8") },
                { 5, new FieldInfo("align_1", "uint8") },
                { 6, new FieldInfo("initial_gold", "uint32") },
                { 7, new FieldInfo("initial_tech", "uint32") },
                { 8, new FieldInfo("income_modifier", "float") },
                { 9, new FieldInfo("damage_modifier", "float") },
                { 10, new FieldInfo("hp_modifier", "float") },
                { 11, new FieldInfo("color", "uint32") },
                { 12, new FieldInfo("align_2", "uint16") },
                { 13, new FieldInfo("config_id", "uint16") }
            }},
            { "FactionCardsRelation", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("faction_id", "uint16") },
                { 1, new FieldInfo("cards", "vector_uint16") }
            }},
            { "FactionLimitsRelation", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("faction_id", "uint16") },
                { 1, new FieldInfo("limits", "vector_uint16") }
            }},
            { "FactionForceEntry", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("country_id", "uint16") },
                { 1, new FieldInfo("strength", "uint8") },
                { 2, new FieldInfo("flag", "uint8") }
            }},
            { "FactionAssignEntry", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("key_id", "uint16") },
                { 1, new FieldInfo("value", "uint16") }
            }},
            { "FactionTargetEntry", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("key_id", "uint16") },
                { 1, new FieldInfo("value", "int16") }
            }},
            { "FactionArmyLimitEntry", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("type_id", "uint8") },
                { 1, new FieldInfo("limit", "uint16") }
            }},
            { "FactionTechBindEntry", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("tech_id", "uint16") },
                { 1, new FieldInfo("param_1", "uint16") },
                { 2, new FieldInfo("param_2", "uint16") }
            }},
            { "FactionStageNum", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("stage_id", "uint16") },
                { 1, new FieldInfo("packed_data", "vector_uint8") }
            }},
            { "FactionDetailConfig", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("general_pool", "vector_uint8") },
                { 1, new FieldInfo("field_1", "uint16") },
                { 2, new FieldInfo("tech_ids", "vector_uint16") },
                { 3, new FieldInfo("field_3", "uint16") },
                { 4, new FieldInfo("field_4", "uint16") },
                { 5, new FieldInfo("army_limits", "vector_FactionArmyLimitEntry") },
                { 6, new FieldInfo("field_6", "uint16") },
                { 7, new FieldInfo("field_7", "uint16") },
                { 10, new FieldInfo("field_10", "uint16") },
                { 12, new FieldInfo("field_12", "bool") },
                { 13, new FieldInfo("field_13", "bool") },
                { 14, new FieldInfo("field_14", "bool") },
                { 15, new FieldInfo("field_15", "bool") },
                { 16, new FieldInfo("tech_binds", "vector_FactionTechBindEntry") },
                { 19, new FieldInfo("field_19", "uint16") },
                { 20, new FieldInfo("field_20", "uint16") },
                { 21, new FieldInfo("general_states", "vector_uint8") },
                { 22, new FieldInfo("field_22", "uint8") },
                { 23, new FieldInfo("field_23", "uint8") },
                { 24, new FieldInfo("field_24", "uint8") }
            }},
            { "FactionLimits", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("field_0", "uint16") },
                { 1, new FieldInfo("field_1", "uint16") },
                { 5, new FieldInfo("field_5", "uint16") }
            }},
            { "FactionCards", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("field_0", "uint16") },
                { 1, new FieldInfo("field_1", "bool") },
                { 2, new FieldInfo("field_2", "uint16") },
                { 3, new FieldInfo("field_3", "uint16") }
            }},
            { "Card", new Dictionary<int, FieldInfo>() },
            { "TriggerInfo", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("events", "vector_TriggerEvent") },
                { 1, new FieldInfo("conditions", "vector_int16") },
                { 2, new FieldInfo("var_values", "vector_int16") },
                { 3, new FieldInfo("var_ids", "vector_int16") },
                { 4, new FieldInfo("actions", "vector_TriggerAction") },
                { 5, new FieldInfo("enabled", "bool") },
                { 6, new FieldInfo("triggers", "vector_TriggerNode") },
                { 7, new FieldInfo("tag", "int16") }
            }},
            { "TriggerEvent", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("tile_index", "uint16", "格子一维索引") },
                { 1, new FieldInfo("field_1", "uint16") },
                { 2, new FieldInfo("field_2", "bool") },
                { 3, new FieldInfo("detail_bldg", "TriggerEventBldg", "建筑初始化子表 (Type=4时存在)") },
                { 4, new FieldInfo("detail_fort", "TriggerEventFort", "工事/特殊区域子表 (Type=0/16/20/24时存在)") }
            }},
            { "TriggerEventBldg", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("building_data", "BuildingData", "8字节建筑数据结构体") },
                { 1, new FieldInfo("field_1", "TriggerEventSub1", "子表1") },
                { 2, new FieldInfo("field_2", "TriggerEventSub2", "子表2") },
                { 3, new FieldInfo("field_3", "TriggerEventSub3", "子表3") },
                { 4, new FieldInfo("field_4", "TriggerEventSub4", "子表4") },
                { 5, new FieldInfo("field_5", "TriggerEventSub5", "子表5") },
                { 6, new FieldInfo("field_6", "uint8", "标志位/扩展参数") }
            }},
            { "TriggerEventFort", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("fort_id", "uint16", "工事ID (1=雷达,2=战壕,3=地堡,4=要塞炮,5=海岸炮)") },
                { 1, new FieldInfo("field_1", "TriggerEventSub1", "子表1") },
                { 2, new FieldInfo("field_2", "TriggerEventSub2", "子表2") },
                { 3, new FieldInfo("field_3", "uint8", "标志位/扩展参数") }
            }},
            { "TriggerEventSub1", new Dictionary<int, FieldInfo>() },
            { "TriggerEventSub2", new Dictionary<int, FieldInfo>() },
            { "TriggerEventSub3", new Dictionary<int, FieldInfo>() },
            { "TriggerEventSub4", new Dictionary<int, FieldInfo>() },
            { "TriggerEventSub5", new Dictionary<int, FieldInfo>() },
            { "BuildingData", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("flag", "uint16", "状态/激活标志位 (恒为1)") },
                { 1, new FieldInfo("building_id", "uint16", "建筑配置ID (BuildingSettings.json)") },
                { 2, new FieldInfo("extra_flag", "uint8", "附加标志 (恒为1)") },
                { 3, new FieldInfo("owner", "uint8", "初始归属势力ID") },
                { 4, new FieldInfo("dx", "int8", "渲染偏移X") },
                { 5, new FieldInfo("dy", "int8", "渲染偏移Y") }
            }},
            { "TriggerNode", new Dictionary<int, FieldInfo>() },
            { "TriggerAction", new Dictionary<int, FieldInfo>() },
            { "AIInfo", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("agents", "vector_AIAgent") },
                { 1, new FieldInfo("behaviors", "vector_AIBehavior") },
                { 2, new FieldInfo("faction_id", "uint16") },
                { 3, new FieldInfo("routes", "vector_AIRoute") }
            }},
            { "AIAgent", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("agent_info", "AgentInfo") },
                { 2, new FieldInfo("field_2", "uint8") },
                { 3, new FieldInfo("behavior", "AIAgentBehavior") },
                { 4, new FieldInfo("field_4", "uint16") },
                { 5, new FieldInfo("ai_target", "uint8") },
                { 6, new FieldInfo("field_6", "uint8") },
                { 7, new FieldInfo("faction_id_extra", "uint8") },
                { 8, new FieldInfo("extra_table_8", "AIAgentTable8") },
                { 10, new FieldInfo("extra_table_10", "AIAgentTable10") },
                { 11, new FieldInfo("extra_table_11", "AIAgentTable11") },
                { 12, new FieldInfo("extra_table_12", "AIAgentTable12") },
                { 14, new FieldInfo("field_14", "uint8") }
            }},
            { "AIAgentBehavior", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("field_0", "uint8") },
                { 1, new FieldInfo("field_1", "uint16") },
                { 2, new FieldInfo("field_2", "int16") },
                { 3, new FieldInfo("field_3", "uint8") },
                { 4, new FieldInfo("field_4", "uint8") },
                { 5, new FieldInfo("field_5", "uint16") },
                { 6, new FieldInfo("field_6", "int16") }
            }},
            { "AIAgentTable8", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("field_0", "uint8") },
                { 1, new FieldInfo("field_1", "uint8") },
                { 2, new FieldInfo("binds", "vector_Table8Entry") },
                { 3, new FieldInfo("field_3", "uint8") },
                { 4, new FieldInfo("field_4", "uint8") },
                { 5, new FieldInfo("field_5", "int32") },
                { 6, new FieldInfo("field_6", "uint8") },
                { 7, new FieldInfo("field_7", "int16") }
            }},
            { "Table8Entry", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("config_id", "uint16") },
                { 1, new FieldInfo("param_1", "uint16") },
                { 2, new FieldInfo("param_2", "uint16") }
            }},
            { "AIAgentTable10", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("field_0", "uint16") },
                { 1, new FieldInfo("field_1", "uint8") },
                { 2, new FieldInfo("field_2", "uint16") },
                { 3, new FieldInfo("field_3", "uint16") },
                { 4, new FieldInfo("field_4", "uint16") },
                { 5, new FieldInfo("field_5", "uint16") }
            }},
            { "AIAgentTable11", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("general_id", "uint16") },
                { 1, new FieldInfo("is_active", "bool") },
                { 2, new FieldInfo("param_2", "uint8") }
            }},
            { "AIAgentTable12", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("field_0", "uint16") },
                { 1, new FieldInfo("cells", "vector_uint8") }
            }},
            { "AIBehavior", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("flag", "uint16") },
                { 1, new FieldInfo("params", "vector_uint16") }
            }},
            { "AIRoute", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("flag", "uint16") },
                { 1, new FieldInfo("cells", "vector_uint16") }
            }},
            { "RegionInfo", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("name", "string") },
                { 1, new FieldInfo("sub_regions", "vector_SubRegion") },
                { 2, new FieldInfo("events", "vector_uint8") },
                { 3, new FieldInfo("properties", "vector_uint8") }
            }},
            { "SubRegion", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("id", "uint16") },
                { 1, new FieldInfo("name", "string") },
                { 2, new FieldInfo("flag", "bool") }
            }},
            { "StageConfig", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("faction_cards", "vector_FactionCards") },
                { 1, new FieldInfo("faction_limits", "vector_FactionLimits") },
                { 2, new FieldInfo("tech_upgrades", "vector_TechUpgrade") },
                { 3, new FieldInfo("generals_diplomacy", "vector_Diplomacy") },
                { 5, new FieldInfo("cities_status", "vector_CityStatus") },
                { 6, new FieldInfo("harbors_status", "vector_HarborStatus") },
                { 7, new FieldInfo("disabled_skills", "vector_uint16") }
            }},
            { "TechUpgrade", new Dictionary<int, FieldInfo>() },
            { "Diplomacy", new Dictionary<int, FieldInfo>() },
            { "CityStatus", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("x", "uint16") },
                { 1, new FieldInfo("owner", "uint16") },
                { 2, new FieldInfo("y", "uint16") },
                { 3, new FieldInfo("level", "uint16") }
            }},
            { "HarborStatus", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("x", "uint16") },
                { 1, new FieldInfo("owner", "uint16") },
                { 2, new FieldInfo("y", "uint16") },
                { 3, new FieldInfo("level", "uint16") }
            }},
            { "DecalInfo", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("decals", "vector_Decal") }
            }},
            { "Decal", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("decal_type", "uint8") },
                { 1, new FieldInfo("x", "uint16") },
                { 2, new FieldInfo("y", "uint16") }
            }},
            { "ViewBoundary", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("boundary_id", "uint16") }
            }},
            { "AgentInfo", new Dictionary<int, FieldInfo> {
                { 0, new FieldInfo("cell_idx", "uint16") },
                { 1, new FieldInfo("faction_id", "uint16") },
                { 2, new FieldInfo("agent_id", "uint16") },
                { 3, new FieldInfo("unit_id", "uint16") },
                { 4, new FieldInfo("stack_count", "uint16") },
                { 5, new FieldInfo("val5", "uint16") },
                { 6, new FieldInfo("hp", "uint16") },
                { 7, new FieldInfo("max_hp", "uint16") },
                { 8, new FieldInfo("val8", "uint16") },
                { 9, new FieldInfo("val9", "uint16") }
            }}
        };

        private static readonly HashSet<string> TableTypes = new HashSet<string>
        {
            "Root", "MapTerrain", "StageMetadata", "BattleInfo", "ReinforcePoint", "FactionInfo",
            "TriggerInfo", "TriggerEvent", "TriggerEventBldg", "TriggerEventFort", "TriggerNode", "TriggerAction", "AIInfo", "AIAgent", "AIBehavior",
            "AIRoute", "RegionInfo", "SubRegion", "StageConfig", "DecalInfo", "ViewBoundary",
            "StageTarget", "Faction", "Card", "FactionCards", "FactionLimits",
            "TechUpgrade", "Diplomacy", "CityStatus", "HarborStatus", "Decal",
            "TriggerEventSub1", "TriggerEventSub2", "TriggerEventSub3", "TriggerEventSub4", "TriggerEventSub5",
            "FactionCardsRelation", "FactionLimitsRelation",
            "FactionForceEntry", "FactionAssignEntry", "FactionTargetEntry",
            "FactionArmyLimitEntry", "FactionTechBindEntry", "FactionStageNum", "FactionDetailConfig",
            "AIAgentBehavior", "AIAgentTable8", "Table8Entry", "AIAgentTable10", "AIAgentTable11", "AIAgentTable12"
        };
        
        public static bool UseDynamic = true;
        public static readonly Dictionary<string, Dictionary<int, FieldInfo>> DynamicSchema = new Dictionary<string, Dictionary<int, FieldInfo>>();
        public static readonly HashSet<string> DynamicTableTypes = new HashSet<string>();
        public static readonly HashSet<string> DynamicStructTypes = new HashSet<string>();

        public static readonly string WorkspacePath;
        public static readonly string DefaultFbsPath;

        static string FindWorkspaceRoot()
        {
            foreach (var start in new[] { AppDomain.CurrentDomain.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start)) continue;
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, "schema", "battle.fbs"))
                        || System.IO.File.Exists(System.IO.Path.Combine(dir, "BtldMapEditor.sln"))
                        || System.IO.File.Exists(System.IO.Path.Combine(dir, "btl相关代码", "battle.fbs")))
                        return dir;
                    string parent = System.IO.Path.GetDirectoryName(dir);
                    if (parent == dir) break;
                    dir = parent;
                }
            }
            return System.IO.Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// 自动加载默认 FBS Schema 文件，若文件不存在则静默回退到硬编码模式。
        /// </summary>
        public static void AutoLoadDefaultFbs()
        {
            string fbsPath = DefaultFbsPath;

            if (System.IO.File.Exists(fbsPath))
            {
                try
                {
                    LoadFromFbs(fbsPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FBS Error] Failed to load dynamic schema: {ex}");
                    // FBS 解析失败，回退到硬编码
                    UseDynamic = false;
                }
            }
            else
            {
                UseDynamic = false;
            }
        }

        public static void LoadFromFbs(string fbsFilePath)
        {
            string text = System.IO.File.ReadAllText(fbsFilePath);
            
            // Inline includes dynamically (e.g. include "structs.sym";)
            var includeRegex = new System.Text.RegularExpressions.Regex(@"include\s+""([^""]+)"";");
            while (true)
            {
                var match = includeRegex.Match(text);
                if (!match.Success) break;
                string incFile = match.Groups[1].Value;
                string incPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fbsFilePath), incFile);
                string incText = System.IO.File.Exists(incPath) ? System.IO.File.ReadAllText(incPath) : "";
                // Replace include statement with actual file contents
                text = text.Replace(match.Value, incText);
            }

            // Remove block comments (but preserve single-line // comments for field-level extraction)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

            DynamicSchema.Clear();
            DynamicTableTypes.Clear();
            DynamicStructTypes.Clear();

            var blockRegex = new System.Text.RegularExpressions.Regex(@"(table|struct)\s+(\w+)\s*\{([^}]*)\}", System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = blockRegex.Matches(text);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string blockType = match.Groups[1].Value;
                string name = match.Groups[2].Value;
                string body = match.Groups[3].Value;

                if (blockType == "table")
                {
                    DynamicTableTypes.Add(name);
                }
                else if (blockType == "struct")
                {
                    // 以 _ 开头的伪结构体 (_Meta, _Enum_* 等) 仅用于文档，不作为真实 struct 注册
                    if (!name.StartsWith("_"))
                        DynamicStructTypes.Add(name);
                }

                var fields = new Dictionary<int, FieldInfo>();
                // 逐行解析字段：先分离注释，再匹配字段定义，避免注释中的分号干扰
                var fieldDefRegex = new System.Text.RegularExpressions.Regex(@"(\w+)\s*:\s*([^;]+);");
                string[] bodyLines = body.Split('\n');
                var fieldMatchList = new System.Collections.Generic.List<(System.Text.RegularExpressions.Match m, string comment)>();
                foreach (string rawLine in bodyLines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                    // 分离行内注释：找到不在引号/括号内的 // 
                    string codePart = line;
                    string commentPart = "";
                    int commentIdx = line.IndexOf("//");
                    if (commentIdx >= 0)
                    {
                        codePart = line.Substring(0, commentIdx);
                        commentPart = line.Substring(commentIdx + 2).Trim();
                    }
                    var fm = fieldDefRegex.Match(codePart);
                    if (fm.Success)
                        fieldMatchList.Add((fm, commentPart));
                }

                int nextId = 0;
                foreach (var (fMatch, rawComment) in fieldMatchList)
                {
                    string fName = fMatch.Groups[1].Value;
                    string fTypeRaw = fMatch.Groups[2].Value.Trim();
                    string fComment = rawComment;

                    // 从注释中提取 @enum(NAME) 标签
                    string enumName = "";
                    var enumMatch = System.Text.RegularExpressions.Regex.Match(fComment, @"@enum\((\w+)\)");
                    if (enumMatch.Success)
                    {
                        enumName = enumMatch.Groups[1].Value;
                        // 从注释中移除 @enum(...) 标签，保留其余注释文本
                        fComment = System.Text.RegularExpressions.Regex.Replace(fComment, @"\s*@enum\(\w+\)", "").Trim();
                    }

                    string fType = fTypeRaw;
                    int id = nextId;

                    var idMatch = System.Text.RegularExpressions.Regex.Match(fTypeRaw, @"\(id:\s*(\d+)\)");
                    if (idMatch.Success)
                    {
                        id = int.Parse(idMatch.Groups[1].Value);
                        fType = System.Text.RegularExpressions.Regex.Replace(fType, @"\s*\(.*?\)", "").Trim();
                    }

                    fType = MapFbsType(fType);
                    fields[id] = new FieldInfo(fName, fType, fComment, enumName);
                    nextId = id + 1;
                }

                DynamicSchema[name] = fields;
            }
            UseDynamic = true;
            BuildUniqueNameMappings();
        }

        private static string MapFbsType(string type)
        {
            if (type.StartsWith("[") && type.EndsWith("]"))
            {
                string elem = type.Substring(1, type.Length - 2).Trim();
                if (elem == "ushort" || elem == "uint16") return "vector_uint16";
                if (elem == "short" || elem == "int16") return "vector_int16";
                if (elem == "uint" || elem == "uint32") return "vector_uint32";
                if (elem == "int" || elem == "int32") return "vector_int32";
                if (elem == "ubyte" || elem == "uint8") return "vector_uint8";
                if (elem == "byte") return "vector_int8";
                if (elem == "float" || elem == "float32") return "vector_float";
                if (elem == "double" || elem == "float64") return "vector_double";
                return "vector_" + elem;
            }

            if (type == "ushort" || type == "uint16") return "uint16";
            if (type == "short" || type == "int16") return "int16";
            if (type == "uint" || type == "uint32") return "uint32";
            if (type == "int" || type == "int32") return "int32";
            if (type == "ubyte" || type == "uint8") return "uint8";
            if (type == "byte") return "int8";  // FlatBuffers 的 byte 是有符号 8 位
            if (type == "float" || type == "float32") return "float";
            if (type == "double" || type == "float64") return "double";
            return type;
        }

        public static Dictionary<int, FieldInfo> GetSchema(string tableType)
        {
            if (UseDynamic)
            {
                if (DynamicSchema.ContainsKey(tableType))
                    return DynamicSchema[tableType];
                return new Dictionary<int, FieldInfo>();
            }

            if (Schema.ContainsKey(tableType))
                return Schema[tableType];
            return new Dictionary<int, FieldInfo>();
        }

        public static bool IsTableType(string type)
        {
            if (UseDynamic)
            {
                return DynamicTableTypes.Contains(type) || type.StartsWith("Table_Field_");
            }
            return TableTypes.Contains(type) || type.StartsWith("Table_Field_");
        }

        /// <summary>
        /// 判断给定类型名是否为结构体 (struct)。
        /// 动态模式下基于 DynamicStructTypes 判定；
        /// 静态模式下保持对 Size/AgentInfo/FactionMetadata 等的兼容。
        /// </summary>
        public static bool IsStructType(string type)
        {
            if (UseDynamic)
            {
                return DynamicStructTypes.Contains(type);
            }
            // 静态回退：兼容旧硬编码
            return type == "Size" || type == "AgentInfo" || type == "FactionMetadata" || type == "TileAttr" || type == "TriggerAction" || type == "BuildingData";
        }

        /// <summary>
        /// 计算给定结构体类型的内联字节大小。
        /// 根据其动态 Schema 中定义的所有字段类型，按自然对齐规则顺序累加。
        /// </summary>
        public static int GetStructSize(string structType)
        {
            var schema = GetSchema(structType);
            if (schema == null || schema.Count == 0)
                return 0;

            int currentOffset = 0;
            var sortedIds = new List<int>(schema.Keys);
            sortedIds.Sort();

            foreach (int fid in sortedIds)
            {
                string fieldType = schema[fid].Type;
                int size = GetTypeSize(fieldType);
                // 自然对齐
                currentOffset = ((currentOffset + size - 1) / size) * size;
                currentOffset += size;
            }
            return currentOffset;
        }

        /// <summary>
        /// 获取标量类型的字节大小。
        /// </summary>
        public static int GetTypeSize(string type)
        {
            if (type == "uint8" || type == "int8" || type == "bool" || type == "ubyte" || type == "byte")
                return 1;
            if (type == "uint16" || type == "int16" || type == "ushort" || type == "short")
                return 2;
            if (type == "uint32" || type == "int32" || type == "uint" || type == "int" || type == "float")
                return 4;
            if (type == "uint64" || type == "int64" || type == "double")
                return 8;
            return 1;
        }

        /// <summary>
        /// 获取指定表/结构体中某个字段的注释。
        /// 如果 Schema 中存在该字段且注释不为空，返回 "; 注释内容"；否则返回 defaultComment。
        /// </summary>
        public static string GetFieldComment(string tableType, int fieldId, string defaultComment = "")
        {
            var schema = GetSchema(tableType);
            if (schema != null && schema.ContainsKey(fieldId))
            {
                string comment = schema[fieldId].Comment;
                if (!string.IsNullOrEmpty(comment))
                    return "; " + comment;
            }
            return defaultComment;
        }

        /// <summary>
        /// 从 _Enum_XXX 伪结构体中查找枚举值描述。
        /// 例如 GetEnumDescription("_Enum_AITargetType", 2, "未知") 会查找 type_2 字段的注释。
        /// </summary>
        public static string GetEnumDescription(string enumStructName, int value, string defaultDesc = "")
        {
            var schema = GetSchema(enumStructName);
            if (schema != null)
            {
                // 遍历所有字段，查找名为 type_N 的条目
                foreach (var kvp in schema)
                {
                    if (kvp.Value.Name == $"type_{value}")
                    {
                        if (!string.IsNullOrEmpty(kvp.Value.Comment))
                            return kvp.Value.Comment;
                    }
                }
            }
            return string.IsNullOrEmpty(defaultDesc) ? $"未知类型 {value}" : defaultDesc;
        }
    }
}
