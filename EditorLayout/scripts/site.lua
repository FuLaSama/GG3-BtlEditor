-- 建筑、工事和势力。颜色打成一个 u32，R 在高位。
-- 势力排序先摘下，再按原来的目标下标插入。

local EVENTS = "Root.trigger_info.events"
local FACTIONS = "Root.faction_info.factions"
local CARDS = "Root.faction_info.faction_cards"
local LIMITS = "Root.faction_info.faction_limits"

local function n0(v)
  if v == nil then return 0 end
  return v
end

local function n1(v)
  if v == nil then return 1 end
  return v
end

local function id_of(faction)
  local id = editor.member(faction, "info.faction_id")
  if id == nil then return 0 end
  return id
end

local function pack_color(input)
  if input.r == nil and input.g == nil and input.b == nil and input.a == nil then
    return nil
  end
  local a = input.a
  if a == nil then a = 255 end
  return editor.bor(
    editor.bor(editor.bor(editor.lshift(n0(input.r), 24), editor.lshift(n0(input.g), 16)), editor.lshift(n0(input.b), 8)),
    a)
end

editor.action("apply_building", function(ctx)
  local ev = ctx.object
  if ev == nil then return end
  local input = ctx.input
  editor.ensure(ev, "detail_bldg")
  editor.set_member(ev, "detail_bldg.building_data.val1", n0(input.flag))
  editor.set_member(ev, "detail_bldg.building_data.building_id", n0(input.type))
  editor.set_member(ev, "detail_bldg.building_data.val2", n0(input.extra))
  editor.set_member(ev, "detail_bldg.building_data.owner", n0(input.owner))
  editor.set_member(ev, "detail_bldg.building_data.dx", n0(input.dx))
  editor.set_member(ev, "detail_bldg.building_data.dy", n0(input.dy))
  editor.set(ev, "detail_bldg.field_6", input.field6)
end)

editor.action("place_building", function(ctx)
  local input = ctx.input
  local ev = editor.append(EVENTS)
  editor.set(ev, "tile_index", n0(ctx.cell_index))
  editor.ensure(ev, "detail_bldg.building_data")
  editor.set_member(ev, "detail_bldg.building_data.val1", n0(input.flag))
  editor.set_member(ev, "detail_bldg.building_data.building_id", n0(input.type))
  editor.set_member(ev, "detail_bldg.building_data.val2", n0(input.extra))
  editor.set_member(ev, "detail_bldg.building_data.owner", n0(input.owner))
  editor.set_member(ev, "detail_bldg.building_data.dx", n0(input.dx))
  editor.set_member(ev, "detail_bldg.building_data.dy", n0(input.dy))
  editor.ensure(ev, "detail_bldg.field_2")
  editor.ensure(ev, "detail_bldg.field_4")
  if input.field6 ~= nil then
    editor.set(ev, "detail_bldg.field_6", input.field6)
  end
end)

editor.action("apply_fort", function(ctx)
  local ev = ctx.object
  if ev == nil then return end
  local input = ctx.input
  editor.ensure(ev, "detail_fort")
  editor.set(ev, "detail_fort.fort_id", n0(input.fort_id))
  editor.set(ev, "field_1", n0(input.field1))
  editor.set(ev, "detail_fort.field_3", n0(input.field3))
end)

editor.action("place_fort", function(ctx)
  local input = ctx.input
  local ev = editor.append(EVENTS)
  editor.set(ev, "tile_index", n0(ctx.cell_index))
  editor.set(ev, "field_1", n0(input.field1))
  editor.ensure(ev, "detail_fort")
  editor.set(ev, "detail_fort.fort_id", n0(input.fort_id))
  editor.set(ev, "detail_fort.field_3", n0(input.field3))
end)

editor.action("delete_event", function(ctx)
  if ctx.index == nil then return end
  editor.remove(EVENTS, ctx.index)
end)

local function write_faction(faction, input)
  editor.ensure(faction, "info")
  editor.set_member(faction, "info.faction_id", n0(input.id))
  editor.set_member(faction, "info.country_id", n0(input.country))
  editor.set_member(faction, "info.camp", n0(input.camp))
  editor.set_member(faction, "info.is_ai", n0(input.is_ai))
  editor.set_member(faction, "info.general_limit", n0(input.val5))
  editor.set_member(faction, "info.align_1", n0(input.align1))
  editor.set_member(faction, "info.initial_gold", n0(input.gold))
  editor.set_member(faction, "info.initial_tech", n0(input.tech))
  editor.set_member(faction, "info.income_modifier", n1(input.income))
  editor.set_member(faction, "info.damage_modifier", n1(input.damage))
  editor.set_member(faction, "info.hp_modifier", n1(input.hp))
  local color = pack_color(input)
  if color ~= nil then
    editor.set_member(faction, "info.color", color)
  end
  editor.set_member(faction, "info.align_2", n0(input.align2))
  editor.set_member(faction, "info.config_id", n0(input.config_id))
  editor.set(faction, "val1", input.general_flag)
  editor.set(faction, "val2", input.config_ref)
end

editor.action("apply_faction", function(ctx)
  if ctx.object == nil then return end
  write_faction(ctx.object, ctx.input)
end)

editor.action("add_faction", function(ctx)
  local n = editor.count(FACTIONS)
  local new_id = 0
  for i = 0, n - 1 do
    local id = id_of(editor.at(FACTIONS, i))
    if id >= new_id then new_id = id + 1 end
  end
  local faction = editor.append(FACTIONS)
  local input = {
    id = new_id, country = 1, camp = 1, is_ai = 0, val5 = 0, align1 = 0,
    gold = 100, tech = 0, income = 1, damage = 1, hp = 1,
    r = 255, g = 255, b = 255, a = 255,
    align2 = 0, config_id = 0, general_flag = 1, config_ref = 1
  }
  write_faction(faction, input)
  local card = editor.append(CARDS)
  editor.set(card, "faction_id", new_id)
  editor.ensure(card, "cards")
  local lim = editor.append(LIMITS)
  editor.set(lim, "faction_id", new_id)
  editor.ensure(lim, "limits")
end)

local function drop_relation(path, faction_id)
  local i = editor.count(path) - 1
  while i >= 0 do
    if editor.get(editor.at(path, i), "faction_id") == faction_id then
      editor.remove(path, i)
    end
    i = i - 1
  end
end

editor.action("delete_faction", function(ctx)
  if ctx.index == nil then return end
  local faction = editor.at(FACTIONS, ctx.index)
  local id = id_of(faction)
  editor.remove(FACTIONS, ctx.index)
  drop_relation(CARDS, id)
  drop_relation(LIMITS, id)
end)

local function move_relation(path, faction_id, index)
  local found = nil
  local n = editor.count(path)
  for i = 0, n - 1 do
    if editor.get(editor.at(path, i), "faction_id") == faction_id then
      found = i
      break
    end
  end
  if found == nil then return end
  local row = editor.at(path, found)
  editor.remove(path, found)
  local at = index
  local left = editor.count(path)
  if at > left then at = left end
  editor.insert(path, at, row)
end

editor.action("move_faction", function(ctx)
  local from = ctx.input.from
  local to = ctx.input.to
  if from == nil or to == nil or from == to then return end
  local faction = editor.at(FACTIONS, from)
  local id = id_of(faction)
  editor.remove(FACTIONS, from)
  editor.insert(FACTIONS, to, faction)
  move_relation(CARDS, id, to)
  move_relation(LIMITS, id, to)
end)
