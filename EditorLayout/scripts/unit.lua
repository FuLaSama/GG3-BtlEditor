-- 部队：按 cell_idx 找到 agents 里的那一张，改 agent_info 和可选子表。
-- 生命空值写成 0，agent_info 仍在。行为、将领、特种取消时删掉整张子表。

local AGENTS = "Root.ai_info.agents"

local function band(a, b) return editor.band(a, b) end
local function bor(a, b) return editor.bor(a, b) end
local function lshift(a, n) return editor.lshift(a, n) end

local function n0(v)
  if v == nil then return 0 end
  return v
end

local function pack_level(level, stack)
  return bor(band(n0(level), 255), lshift(band(n0(stack), 255), 8))
end

local function pack_facing(mobility, direction)
  return bor(lshift(band(n0(mobility), 255), 8), band(n0(direction), 255))
end

editor.resolve("unit", function(ctx)
  if ctx.cell_index == nil then return nil end
  local n = editor.count(AGENTS)
  for i = 0, n - 1 do
    local agent = editor.at(AGENTS, i)
    local cell = editor.member(agent, "agent_info.cell_idx")
    if cell == ctx.cell_index then return agent end
  end
  return nil
end)

local function write_info(agent, input)
  editor.ensure(agent, "agent_info")
  editor.set_member(agent, "agent_info.faction_id", n0(input.faction))
  editor.set_member(agent, "agent_info.agent_id", n0(input.agent))
  editor.set_member(agent, "agent_info.unit_id", n0(input.unit))
  editor.set_member(agent, "agent_info.stack_count", pack_level(input.level, input.stack))
  editor.set_member(agent, "agent_info.val5", pack_facing(input.mobility, input.direction))
  editor.set_member(agent, "agent_info.hp", n0(input.hp))
  editor.set_member(agent, "agent_info.max_hp", n0(input.max_hp))
  editor.set_member(agent, "agent_info.val8", n0(input.val8))
  editor.set_member(agent, "agent_info.val9", n0(input.val9))
  editor.set(agent, "field_2", input.play_mode)
  editor.set(agent, "field_5", input.ai_target)
  editor.set(agent, "field_6", input.faction_extra)
end

editor.action("apply_unit", function(ctx)
  local agent = ctx.object
  if agent == nil then return end
  local input = ctx.input or {}
  write_info(agent, input)
  if input.behavior then
    editor.ensure(agent, "field_3")
    editor.set(agent, "field_3.field_0", input.behavior_0)
    editor.set(agent, "field_3.field_1", input.behavior_id)
    editor.set(agent, "field_3.field_2", input.behavior_2)
    editor.set(agent, "field_3.field_3", input.behavior_radius)
    editor.set(agent, "field_3.field_4", input.behavior_4)
    editor.set(agent, "field_3.field_5", input.behavior_center)
    editor.set(agent, "field_3.field_6", input.behavior_6)
  else
    editor.set(agent, "field_3", nil)
  end
  if input.general then
    editor.ensure(agent, "extra_table_11")
    editor.set(agent, "extra_table_11.general_id", n0(input.general_id))
    local active = editor.get(agent, "extra_table_11.param1")
    if input.general_active or active ~= nil then
      local flag = false
      if input.general_active then flag = true end
      editor.set(agent, "extra_table_11.param1", flag)
    end
    local param2 = editor.get(agent, "extra_table_11.param2")
    if input.general_param2 ~= nil or param2 ~= nil then
      editor.set(agent, "extra_table_11.param2", n0(input.general_param2))
    end
  else
    editor.set(agent, "extra_table_11", nil)
  end
  if input.ex then
    editor.ensure(agent, "extra_table_10")
    editor.set(agent, "extra_table_10.field_0", n0(input.ex_id))
    editor.set(agent, "extra_table_10.field_1", input.ex_1)
    editor.set(agent, "extra_table_10.field_2", input.ex_hp)
    editor.set(agent, "extra_table_10.field_3", input.ex_max_hp)
    editor.set(agent, "extra_table_10.field_4", input.ex_4)
    editor.set(agent, "extra_table_10.field_5", input.ex_5)
  else
    editor.set(agent, "extra_table_10", nil)
  end
end)

editor.action("place_unit", function(ctx)
  if ctx.cell_index == nil then return end
  local input = ctx.input or {}
  local unit = input.unit
  if unit == nil then unit = 101 end
  local row = editor.append(AGENTS)
  editor.ensure(row, "agent_info")
  editor.set_member(row, "agent_info.cell_idx", ctx.cell_index)
  editor.set_member(row, "agent_info.faction_id", n0(input.faction))
  editor.set_member(row, "agent_info.agent_id", n0(input.agent))
  editor.set_member(row, "agent_info.unit_id", unit)
  editor.set_member(row, "agent_info.stack_count", pack_level(input.level, input.stack))
  editor.set_member(row, "agent_info.val5", pack_facing(input.mobility, input.direction))
  editor.set_member(row, "agent_info.hp", n0(input.hp))
  editor.set_member(row, "agent_info.max_hp", n0(input.max_hp))
end)

editor.action("delete_unit", function(ctx)
  if ctx.cell_index == nil then return end
  local n = editor.count(AGENTS)
  local hit = nil
  for i = 0, n - 1 do
    local agent = editor.at(AGENTS, i)
    if editor.member(agent, "agent_info.cell_idx") == ctx.cell_index then hit = i end
  end
  if hit ~= nil then editor.remove(AGENTS, hit) end
end)
