/*
 * BtlModels.cs
 * 
 * 本文件定义了 BtlVerifier 使用的强类型关卡配置数据模型：
 * - 包含了与 C# 对象相互绑定的 JSON 节点序列化结构（如 StageModel、MapTerrainModel、BattleInfoModel、AIInfoModel、FactionInfoModel 等）。
 * - 声明了大量的 `[JsonIgnore]` 分析属性（如 Offset、Cells、CitiesStatus、TilesOffset 等），用于记录和跟踪字段在 BTL 原始二进制字节数组中的绝对内存物理地址，从而为编辑器和可视网格提供像素/字节对应的高亮定位支持。
 */
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BtlVerifier
{
    // 关卡整本配置强类型模型，记录关键的物理文件偏移位置
    public class StageModel
    {
        [JsonPropertyName("version")]
        public ushort Version { get; set; }

        [JsonPropertyName("map_terrain")]
        public MapTerrainModel MapTerrain { get; set; }

        [JsonPropertyName("stage_metadata")]
        public StageMetadataModel StageMetadata { get; set; }

        [JsonPropertyName("battle_info")]
        public BattleInfoModel BattleInfo { get; set; }

        [JsonPropertyName("faction_info")]
        public FactionInfoModel FactionInfo { get; set; }

        [JsonPropertyName("trigger_info")]
        public JsonElement? TriggerInfo { get; set; }

        [JsonPropertyName("ai_info")]
        public AIInfoModel AIInfo { get; set; }

        [JsonPropertyName("region_info")]
        public JsonElement? RegionInfo { get; set; }

        [JsonPropertyName("stage_config")]
        public JsonElement? StageConfig { get; set; }

        [JsonPropertyName("decal_info")]
        public JsonElement? DecalInfo { get; set; }

        [JsonPropertyName("rle_metadata")]
        public List<byte> RleMetadata { get; set; }

        [JsonPropertyName("view_boundary")]
        public JsonElement? ViewBoundary { get; set; }

        // --- 验证器附加的物理文件偏移量跟踪信息 ---
        [JsonIgnore]
        public string File { get; set; }

        [JsonIgnore]
        public int TilesOffset { get; set; }

        [JsonIgnore]
        public int AttributesOffset { get; set; }

        [JsonIgnore]
        public int ReinforcePointsOffset { get; set; }

        [JsonIgnore]
        public Dictionary<string, CityStatusModel> CitiesStatus { get; set; } = new Dictionary<string, CityStatusModel>();

        [JsonIgnore]
        public Dictionary<string, CityStatusModel> HarborsStatus { get; set; } = new Dictionary<string, CityStatusModel>();

        [JsonIgnore]
        public List<TriggerEventModel> TriggerEvents { get; set; } = new List<TriggerEventModel>();

        [JsonIgnore]
        public List<CellModel> Cells { get; set; }

        [JsonIgnore]
        public JsonElement? RawRoot { get; set; }
    }

    public class CityStatusModel
    {
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Owner { get; set; }
        public ushort Level { get; set; }
    }

    public class MapTerrainModel
    {
        [JsonPropertyName("size")]
        public SizeModel Size { get; set; }

        [JsonPropertyName("playable_flag")]
        public byte? PlayableFlag { get; set; }

        [JsonPropertyName("tiles")]
        public List<ushort> Tiles { get; set; }

        [JsonPropertyName("attributes")]
        public List<List<byte>> Attributes { get; set; }

        [JsonPropertyName("fog_or_visibility")]
        public bool? FogOrVisibility { get; set; }
    }

    public class SizeModel
    {
        [JsonPropertyName("width")]
        public ushort Width { get; set; }

        [JsonPropertyName("height")]
        public ushort Height { get; set; }

        [JsonPropertyName("left_margin")]
        public ushort LeftMargin { get; set; }

        [JsonPropertyName("top_margin")]
        public ushort TopMargin { get; set; }

        [JsonPropertyName("playable_width")]
        public ushort PlayableWidth { get; set; }

        [JsonPropertyName("playable_height")]
        public ushort PlayableHeight { get; set; }
    }

    public class StageMetadataModel
    {
        [JsonPropertyName("targets")]
        public List<StageTargetModel> Targets { get; set; }

        [JsonPropertyName("stage_num")]
        public ushort StageNum { get; set; }
    }

    public class StageTargetModel
    {
        [JsonPropertyName("target_type")]
        public ushort TargetType { get; set; }

        [JsonPropertyName("target_value")]
        public short TargetValue { get; set; }

        [JsonPropertyName("param1")]
        public ushort Param1 { get; set; }

        [JsonPropertyName("param2")]
        public ushort Param2 { get; set; }

        [JsonPropertyName("flag")]
        public byte Flag { get; set; }

        [JsonPropertyName("_pos")]
        public string PosStr { get; set; }

        [JsonIgnore]
        public int Offset { get; set; }
    }

    public class BattleInfoModel
    {
        [JsonPropertyName("reinforce_points")]
        public List<ReinforcePointModel> ReinforcePoints { get; set; }
    }

    public class ReinforcePointModel
    {
        [JsonPropertyName("cell_idx")]
        public ushort CellIdx { get; set; }

        [JsonPropertyName("faction_id")]
        public byte FactionId { get; set; }

        [JsonPropertyName("is_key_unit")]
        public bool IsKeyUnit { get; set; }

        [JsonPropertyName("flag")]
        public byte Flag { get; set; }

        [JsonIgnore]
        public byte State { get; set; } // Keep State as ignored property so other structures compile successfully

        [JsonPropertyName("_pos")]
        public string PosStr { get; set; }

        // --- 验证器附加的物理文件偏移量 ---
        [JsonIgnore]
        public int Offset { get; set; }
    }

    public class UnitModel
    {
        [JsonPropertyName("cell_idx")]
        public ushort CellIdx { get; set; }

        [JsonPropertyName("faction_id")]
        public byte FactionId { get; set; }

        [JsonPropertyName("is_key_unit")]
        public bool IsKeyUnit { get; set; }

        [JsonPropertyName("flag")]
        public byte Flag { get; set; }

        [JsonIgnore]
        public byte State { get; set; } // Keep State as ignored property so other structures compile successfully

        [JsonPropertyName("_pos")]
        public string PosStr { get; set; }

        // --- 验证器附加的物理文件偏移量 ---
        [JsonIgnore]
        public int Offset { get; set; }

        [JsonIgnore]
        public ushort UnitId { get; set; } // Keep UnitId for map tile rendering/lookup

        [JsonIgnore]
        public ushort GeneralId { get; set; }

        [JsonIgnore]
        public ushort StackCount { get; set; }

        [JsonIgnore]
        public ushort HP { get; set; }

        [JsonIgnore]
        public ushort MaxHP { get; set; }

        [JsonIgnore]
        public int AgentIndex { get; set; }

        [JsonIgnore]
        public int DisplayIndex { get; set; }
    }

    public class FactionInfoModel
    {
        [JsonPropertyName("factions")]
        public List<FactionModel> Factions { get; set; }

        [JsonPropertyName("faction_cards")]
        public List<FactionCardsRelationModel> FactionCards { get; set; }

        [JsonPropertyName("version")]
        public ushort Version { get; set; }

        [JsonPropertyName("faction_limits")]
        public List<FactionLimitsRelationModel> FactionLimits { get; set; }
    }

    public class FactionCardsRelationModel
    {
        [JsonPropertyName("faction_id")]
        public ushort FactionId { get; set; }

        [JsonPropertyName("cards")]
        public List<ushort> Cards { get; set; }
    }

    public class FactionLimitsRelationModel
    {
        [JsonPropertyName("faction_id")]
        public ushort FactionId { get; set; }

        [JsonPropertyName("limits")]
        public List<ushort> Limits { get; set; }
    }

    public class FactionModel
    {
        [JsonPropertyName("info")]
        public FactionMetadataModel Info { get; set; }

        [JsonPropertyName("tiles")]
        public List<byte> Tiles { get; set; }

        [JsonPropertyName("forces")]
        public JsonElement? Forces { get; set; }

        [JsonPropertyName("assignments")]
        public JsonElement? Assignments { get; set; }

        [JsonPropertyName("detail_config")]
        public JsonElement? DetailConfig { get; set; }

        [JsonPropertyName("targets")]
        public JsonElement? Targets { get; set; }

        [JsonPropertyName("stage_num")]
        public JsonElement? StageNum { get; set; }

        [JsonPropertyName("status")]
        public byte Status { get; set; }

        [JsonPropertyName("config_ref")]
        public ushort ConfigRef { get; set; }

        [JsonPropertyName("general_flag")]
        public byte GeneralFlag { get; set; }

        [JsonPropertyName("_pos")]
        public string PosStr { get; set; }

        [JsonIgnore]
        public List<byte> Alliances { get; set; }

        [JsonIgnore]
        public List<CardModel> Cards { get; set; }
    }

    public class CardModel
    {
    }

    public class FactionMetadataModel
    {
        [JsonPropertyName("faction_id")]
        public ushort FactionId { get; set; }

        [JsonPropertyName("country_id")]
        public ushort CountryId { get; set; }

        [JsonPropertyName("camp")]
        public byte Camp { get; set; }

        [JsonPropertyName("is_ai")]
        public byte IsAI { get; set; }

        [JsonPropertyName("general_limit")]
        public byte GeneralLimit { get; set; }

        [JsonPropertyName("align_1")]
        public byte Align1 { get; set; }

        [JsonPropertyName("initial_gold")]
        public uint InitialGold { get; set; }

        [JsonPropertyName("initial_tech")]
        public uint InitialTech { get; set; }

        [JsonPropertyName("income_modifier")]
        public float IncomeModifier { get; set; }

        [JsonPropertyName("damage_modifier")]
        public float DamageModifier { get; set; }

        [JsonPropertyName("hp_modifier")]
        public float HPModifier { get; set; }

        [JsonPropertyName("color")]
        public uint Color { get; set; }

        [JsonPropertyName("align_2")]
        public ushort Align2 { get; set; }

        [JsonPropertyName("config_id")]
        public ushort ConfigId { get; set; }
    }

    // FactionLimitsModel 和 FactionCardsModel 保留 (StageConfig 仍在使用)
    public class FactionLimitsModel
    {
        [JsonPropertyName("field_0")]
        public ushort Field0 { get; set; }

        [JsonPropertyName("field_1")]
        public ushort Field1 { get; set; }

        [JsonPropertyName("field_5")]
        public ushort Field5 { get; set; }
    }

    public class FactionCardsModel
    {
        [JsonPropertyName("field_0")]
        public ushort Field0 { get; set; }

        [JsonPropertyName("field_1")]
        public bool Field1 { get; set; }

        [JsonPropertyName("field_2")]
        public ushort Field2 { get; set; }

        [JsonPropertyName("field_3")]
        public ushort Field3 { get; set; }
    }

    public class CellModel
    {
        public int Index { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public ushort Terrain { get; set; }
        public List<byte> Attr { get; set; } = new List<byte>();    // A1 属性
        public List<byte> AttrA2 { get; set; }                       // A2 属性 (可选)
        public List<byte> AttrA3 { get; set; }                       // A3 属性 (可选)
        public UnitModel Unit { get; set; }                          // 若无部署，则为 null

        // --- 16进制与地址定位的核心偏移参数 ---
        public int TileFileOffset { get; set; }
        public int AttrA1FileOffset { get; set; }
        public int AttrA2FileOffset { get; set; }
        public int AttrA3FileOffset { get; set; }
        public int UnitTableFileOffset { get; set; }

        // 关联的触发器事件 (建筑/工事)，在 BtlBridge 加载时从 TriggerEvents 中填充
        public TriggerEventModel TriggerBuilding { get; set; }
        public TriggerEventModel TriggerFort { get; set; }
    }

    // 用于表示 IDA 反汇编伪代码的一行映射数据
    public class DisasmItem
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string HexBytes { get; set; }
        public string Description { get; set; }
        public string Section { get; set; }
        public int? TargetOffset { get; set; }
        public int SequenceNo { get; set; }
        public string XrefText { get; set; }
    }

    // 触发器事件解析结果模型
    public class TriggerEventModel
    {
        public int TileIndex { get; set; }     // 格子一维索引
        public int TileX { get; set; }         // 计算出的X坐标
        public int TileY { get; set; }         // 计算出的Y坐标
        public int EventType { get; set; }     // 事件类型 (0=工事, 4=建筑, 16/20/24=特殊)
        public int FileOffset { get; set; }    // 事件在文件中的偏移

        // Type=4 建筑数据
        public ushort BuildingId { get; set; }  // 建筑配置ID (对应BuildingSettings.json)
        public byte Owner { get; set; }         // 初始归属势力ID
        public sbyte Dx { get; set; }           // 渲染偏移X
        public sbyte Dy { get; set; }           // 渲染偏移Y

        // Type=0 工事数据
        public ushort FortId { get; set; }      // 工事ID (1=雷达,2=战壕,3=地堡,4=要塞炮,5=海岸炮)
    }

    public class AIInfoModel
    {
        [JsonPropertyName("agents")]
        public List<AIAgentModel> Agents { get; set; }
    }

    public class AIAgentModel
    {
        [JsonPropertyName("agent_info")]
        public AgentInfoModel AgentInfo { get; set; }

        [JsonPropertyName("ai_target")]
        public ushort AITarget { get; set; }

        [JsonPropertyName("faction_id_extra")]
        public byte FactionIdExtra { get; set; }

        [JsonPropertyName("_pos")]
        public string PosStr { get; set; }

        [JsonPropertyName("extra_table")]
        public ExtraTableModel ExtraTable { get; set; }

        [JsonIgnore]
        public int Offset { get; set; }
    }

    public class ExtraTableModel
    {
        [JsonPropertyName("general_id")]
        public ushort GeneralId { get; set; }
    }

    public class AgentInfoModel
    {
        [JsonPropertyName("agent_id")]
        public ushort AgentId { get; set; }

        [JsonPropertyName("faction_id")]
        public ushort FactionId { get; set; }

        [JsonPropertyName("cell_idx")]
        public ushort CellIdx { get; set; }

        [JsonPropertyName("unit_id")]
        public ushort UnitId { get; set; }

        [JsonPropertyName("stack_count")]
        public ushort StackCount { get; set; }

        [JsonPropertyName("val5")]
        public ushort Val5 { get; set; }

        [JsonPropertyName("hp")]
        public ushort HP { get; set; }

        [JsonPropertyName("max_hp")]
        public ushort MaxHP { get; set; }

        [JsonPropertyName("val8")]
        public ushort Val8 { get; set; }

        [JsonPropertyName("val9")]
        public ushort Val9 { get; set; }
    }
}
