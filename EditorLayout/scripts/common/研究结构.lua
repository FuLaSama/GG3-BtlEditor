-- 按人工命名版研究修订物理类型；AgentInfo 保留等宽位段表示，兼容既有对象成员下标。
editor.format.schema {
  id="builtin.research.20261015",
  definition=[[
struct Size {
  width:ushort;
  height:ushort;
  left_margin:ushort;
  top_margin:ushort;
  playable_width:ushort;
  playable_height:ushort;
}

struct TileAttr {
  byte0:ubyte;
  byte1:ubyte;
  byte2:byte;
  byte3:byte;
}

struct Inline3 {
  b0:ubyte;
  b1:ubyte;
  b2:ubyte;
}

struct TriggerAction {
  action_type:ushort;
  param1:ushort;
  x:ushort;
  y:ushort;
}

struct AgentInfo { cell_idx:short; faction_id:ushort; agent_id:ushort; unit_id:ushort; stack_count:ushort; val5:ushort; hp:ushort; max_hp:ushort; val8:ushort; val9:ushort; }

struct FactionMetadata {
  faction_id:ushort;
  country_id:ushort;
  camp:ubyte;
  is_ai:ubyte;
  general_limit:ubyte;
  align_1:ubyte;
  initial_gold:uint;
  initial_tech:uint;
  income_modifier:float;
  damage_modifier:float;
  hp_modifier:float;
  color:uint;
  align_2:ushort;
  config_id:short;
}

table MapTerrain {
  size:Size (id: 0);
  val1:ubyte (id: 1);
  tiles:[ushort] (id: 2);
  attributes:[TileAttr] (id: 3);
  val2:bool (id: 4);
}

table StageTarget {
  target_type:ushort (id: 0);
  target_value:short (id: 1);
  param1:ushort (id: 2);
  param2:ushort (id: 3);
  unk:ubyte (id: 4);
}

table StageMetadata {
  targets:[StageTarget] (id: 0);
  round_limit:ushort (id: 1);
}

table ReinforcePoint {
  cell_idx:short (id: 0);
  faction_id:ubyte (id: 1);
  val1:bool (id: 2);
  val2:ubyte (id: 3);
}

table BattleInfo {
  field_0:ubyte (id: 0);
  reinforce_points:[ReinforcePoint] (id: 1);
}

table FactionForceEntry {
  country_id:ushort (id: 0);
  strength:ubyte (id: 1);
  flag:ubyte (id: 2);
}

table FactionAssignEntry {
  key_id:ushort (id: 0);
  value:ushort (id: 1);
}

table FactionTargetEntry {
  key_id:ushort (id: 0);
  value:short (id: 1);
}

table FactionArmyLimitEntry {
  type_id:ubyte (id: 0);
  limit:ushort (id: 1);
}

table FactionTechBindEntry {
  tech_id:ushort (id: 0);
  param_1:ushort (id: 1);
  param_2:ushort (id: 2);
}

table FactionStageNum {
  stage_id:ushort (id: 0);
  packed_data:[ubyte] (id: 1);
}

table FactionDetailConfig {
  general_pool:[ushort] (id: 0);
  field_1:ushort (id: 1);
  tech_ids:[ushort] (id: 2);
  field_3:ushort (id: 3);
  field_4:ushort (id: 4);
  army_limits:[FactionArmyLimitEntry] (id: 5);
  field_6:ushort (id: 6);
  field_7:ushort (id: 7);
  field_8:ushort (id: 8);
  field_9:ushort (id: 9);
  field_10:ushort (id: 10);
  field_11:ushort (id: 11);
  field_12:bool (id: 12);
  field_13:bool (id: 13);
  field_14:bool (id: 14);
  field_15:bool (id: 15);
  tech_binds:[FactionTechBindEntry] (id: 16);
  field_17:ushort (id: 17);
  field_18:ubyte (id: 18);
  field_19:ushort (id: 19);
  field_20:ushort (id: 20);
  general_states:[byte] (id: 21);
  field_22:ubyte (id: 22);
  field_23:ubyte (id: 23);
  field_24:ubyte (id: 24);
}

table Faction {
  info:FactionMetadata (id: 0);
  tiles:[ubyte] (id: 1);
  forces:[FactionForceEntry] (id: 2);
  assignments:[FactionAssignEntry] (id: 3);
  detail_config:FactionDetailConfig (id: 4);
  targets:[FactionTargetEntry] (id: 5);
  stage_num:FactionStageNum (id: 6);
  val1:ubyte (id: 7);
  val2:ushort (id: 8);
  general_flag:ubyte (id: 9);
}

table FactionInfo {
  factions:[Faction] (id: 0);
}

struct BuildingExtraData {
  param0:ushort;
  param1:ushort;
}

struct BuildingData {
  val1:ushort;
  building_id:ushort;
  val2:ubyte;
  owner:ubyte;
  dx:byte;
  dy:byte;
}

table TriggerEventSub2 {
  field_0:ubyte (id: 0);
  field_1:short (id: 1);
  field_2:short = -1 (id: 2);
  field_3:ubyte (id: 3);
  field_4:ubyte (id: 4);
  field_5:ushort (id: 5);
  field_6:short (id: 6);
}

table TriggerEventSub3 {
  config_id:ushort (id: 0);
  param_1:ushort (id: 1);
  param_2:ushort (id: 2);
}

table TriggerEventSub4 {
  field_0:ubyte (id: 0);
  field_1:short (id: 1);
  field_2:ubyte (id: 2);
}

table TriggerEventSub5 {
  agent_type:ushort (id: 0);
  packed_data:[ubyte] (id: 1);
}

table TriggerEventBldg {
  building_data:BuildingData (id: 0);
  field_1:BuildingExtraData (id: 1);
  field_2:TriggerEventSub2 (id: 2);
  field_3:[TriggerEventSub3] (id: 3);
  field_4:TriggerEventSub4 (id: 4);
  field_5:TriggerEventSub5 (id: 5);
  field_6:ubyte = 1 (id: 6);
  field_7:ubyte (id: 7);
}

table TriggerEventFort {
  fort_id:ubyte (id: 0);
  field_1:ubyte (id: 1);
  field_2:ubyte (id: 2);
  field_3:ubyte (id: 3);
}

table TriggerEvent {
  tile_index:ushort (id: 0);
  field_1:ushort (id: 1);
  field_2:ubyte (id: 2);
  detail_bldg:TriggerEventBldg (id: 3);
  detail_fort:TriggerEventFort (id: 4);
  field_5:Inline3 (id: 5);
  field_6:ubyte (id: 6);
}

table TriggerNode {
  field_0:short (id: 0);
  field_1:ushort (id: 1);
  field_2:bool (id: 2);
  field_3:ubyte (id: 3);
  field_4:ubyte (id: 4);
}

table TriggerInfo {
  events:[TriggerEvent] (id: 0);
  conditions:[short] (id: 1);
  var_values:[short] (id: 2);
  var_ids:[short] (id: 3);
  actions:[TriggerAction] (id: 4);
  enabled:bool (id: 5);
  triggers:[TriggerNode] (id: 6);
  tag:short (id: 7);
}

table AIAgentBehavior {
  field_0:ubyte (id: 0);
  field_1:short (id: 1);
  field_2:short (id: 2);
  field_3:ubyte (id: 3);
  field_4:ubyte (id: 4);
  field_5:ushort (id: 5);
  field_6:short (id: 6);
}

table Table8Entry {
  config_id:ushort (id: 0);
  param_1:ushort (id: 1);
  param_2:ushort (id: 2);
}

table AIAgentTable8 {
  field_0:ubyte (id: 0);
  field_1:ubyte (id: 1);
  binds:[Table8Entry] (id: 2);
  field_3:ubyte (id: 3);
  field_4:ubyte (id: 4);
  field_5:int (id: 5);
  field_6:ubyte (id: 6);
  field_7:short = -1 (id: 7);
}

table AIAgentTable10 {
  field_0:ushort (id: 0);
  field_1:ubyte (id: 1);
  field_2:ushort (id: 2);
  field_3:ushort (id: 3);
  field_4:ushort (id: 4);
  field_5:ushort (id: 5);
}

table AIAgentTable11 {
  general_id:ushort (id: 0);
  param1:bool (id: 1);
  param2:ubyte (id: 2);
  field_3:ushort (id: 3);
}

table AIAgentTable12 {
  field_0:ushort (id: 0);
  cells:[ubyte] (id: 1);
}

table AIAgent {
  agent_info:AgentInfo (id: 0);
  field_1:ushort (id: 1);
  field_2:ubyte (id: 2);
  field_3:AIAgentBehavior (id: 3);
  field_4:ushort (id: 4);
  field_5:byte (id: 5);
  field_6:byte (id: 6);
  faction_id_extra:ubyte (id: 7);
  extra_table_8:AIAgentTable8 (id: 8);
  field_9:Inline3 (id: 9);
  extra_table_10:AIAgentTable10 (id: 10);
  extra_table_11:AIAgentTable11 (id: 11);
  extra_table_12:AIAgentTable12 (id: 12);
  field_13:ushort (id: 13);
  field_14:ubyte (id: 14);
}

table AIBehavior {
  flag:ushort (id: 0);
  params:[ushort] (id: 1);
}

table AIRoute {
  flag:short (id: 0);
  cells:[ushort] (id: 1);
}

table AIInfo {
  agents:[AIAgent] (id: 0);
  behaviors:[AIBehavior] (id: 1);
  faction_id:ushort (id: 2);
  routes:[AIRoute] (id: 3);
}

table SubRegion {
  id:ushort (id: 0);
  name:string (id: 1);
  flag:bool (id: 2);
}

table RegionInfo {
  name:string (id: 0);
  sub_regions:[SubRegion] (id: 1);
  events:[ubyte] (id: 2);
  properties:[ubyte] (id: 3);
}

table CardEntry {
  card_id:short (id: 0);
  count:ushort (id: 1);
  turn_cooldown:ushort (id: 2);
}

table FactionCardsConfig {
  faction_id:short (id: 0);
  cards:[CardEntry] (id: 1);
}

table LimitEntry {
  unit_id:short (id: 0);
  max_limit:ushort (id: 1);
  current_count:ushort (id: 2);
}

table FactionLimitsConfig {
  faction_id:ushort (id: 0);
  limits:[LimitEntry] (id: 1);
}

table TechUpgradeEntry {
  tech_id:ushort (id: 0);
  level:ushort (id: 1);
  turn:ushort (id: 2);
}

table TechUpgradeConfig {
  faction_id:ushort (id: 0);
  upgrades:[TechUpgradeEntry] (id: 1);
}

table GeneralDiplomacyEntry {
  general_id:ushort (id: 0);
  status:ubyte (id: 1);
  is_active:bool (id: 2);
}

table CityStatus {
  x:ushort (id: 0);
  owner:short (id: 1);
  y:ushort (id: 2);
  level:ushort (id: 3);
}

table FactionCityStatus {
  faction_id:short (id: 0);
  status_list:[CityStatus] (id: 1);
}

table HarborStatus {
  x:ushort (id: 0);
  owner:short (id: 1);
  y:ushort (id: 2);
  level:ushort (id: 3);
}

table FactionHarborStatus {
  faction_id:ushort (id: 0);
  status_list:[HarborStatus] (id: 1);
}

table StageConfig {
  faction_cards:[FactionCardsConfig] (id: 0);
  faction_limits:[FactionLimitsConfig] (id: 1);
  tech_upgrades:[TechUpgradeConfig] (id: 2);
  generals_diplomacy:[GeneralDiplomacyEntry] (id: 3);
  field_4:ushort (id: 4);
  cities_status:[FactionCityStatus] (id: 5);
  harbors_status:[FactionHarborStatus] (id: 6);
  disabled_skills:[ushort] (id: 7);
}

table Decal {
  decal_type:ubyte (id: 0);
  x:ushort (id: 1);
  y:ushort (id: 2);
}

table DecalInfo {
  decals:[Decal] (id: 0);
}

table ViewBoundary {
  entries:[AIAgentTable11] (id: 0);
}

table Root {
  version:ushort (id: 0);
  map_terrain:MapTerrain (id: 1);
  stage_metadata:StageMetadata (id: 2);
  battle_info:BattleInfo (id: 3);
  faction_info:FactionInfo (id: 4);
  trigger_info:TriggerInfo (id: 5);
  ai_info:AIInfo (id: 6);
  region_info:RegionInfo (id: 7);
  stage_config:StageConfig (id: 8);
  decal_info:DecalInfo (id: 9);
  rle_metadata:[ubyte] (id: 10);
  view_boundary:ViewBoundary (id: 11);
}
]],
}
