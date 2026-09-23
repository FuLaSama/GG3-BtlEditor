-- 扩图、粘贴、AI 行为的格子列表。
-- 新格是地形 0。脚本事件在新地图外时保留原索引。增援点落在外面就删掉。

local TILES = "Root.map_terrain.tiles"
local ATTRS = "Root.map_terrain.attributes"
local AGENTS = "Root.ai_info.agents"
local EVENTS = "Root.trigger_info.events"
local POINTS = "Root.battle_info.reinforce_points"
local ROUTES = "Root.ai_info.behaviors"

local function band(a, b) return editor.band(a, b) end
local function rshift(a, n) return editor.rshift(a, n) end

local function n0(v)
  if v == nil then return 0 end
  return v
end

local function clear_vec(path)
  local i = editor.count(path) - 1
  while i >= 0 do
    editor.remove(path, i)
    i = i - 1
  end
end

local function push_tile(terrain)
  local i = editor.count(TILES)
  editor.append(TILES)
  if terrain ~= 0 then editor.set_at(TILES, i, terrain) end
end

local function push_attr(attr)
  if attr == nil then attr = editor.struct("TileAttr") end
  editor.append(ATTRS, attr)
end

local function is_building(ev)
  return editor.member(ev, "detail_bldg.building_data.val1") ~= nil
end

local function is_fort(ev)
  return editor.get(ev, "detail_fort.fort_id") ~= nil or editor.get(ev, "detail_fort.field_3") ~= nil
end

editor.action("resize_map", function(ctx)
  local left = n0(ctx.input.left)
  local right = n0(ctx.input.right)
  local up = n0(ctx.input.up)
  local down = n0(ctx.input.down)
  if left == 0 and right == 0 and up == 0 and down == 0 then return end
  local old_w = n0(editor.member("Root.map_terrain.size.width"))
  local old_h = n0(editor.member("Root.map_terrain.size.height"))
  if old_w <= 0 or old_h <= 0 then return end
  local new_w = old_w + left + right
  local new_h = old_h + up + down
  if new_w <= 0 or new_h <= 0 or new_w > 500 or new_h > 500 then return end
  local old_total = old_w * old_h
  local new_total = new_w * new_h

  local tile_n = editor.count(TILES)
  local attr_n = editor.count(ATTRS)
  local attr_i = 0
  local old_cells = {}
  for i = 0, old_total - 1 do
    local terrain = 9001
    if i < tile_n then terrain = editor.at(TILES, i) end
    local flags = rshift(terrain, 8)
    local decor, main, secondary
    if band(flags, 4) ~= 0 and attr_i < attr_n then
      decor = editor.clone(editor.at(ATTRS, attr_i))
      attr_i = attr_i + 1
    end
    if band(flags, 8) ~= 0 and attr_i < attr_n then
      main = editor.clone(editor.at(ATTRS, attr_i))
      attr_i = attr_i + 1
    end
    if band(flags, 16) ~= 0 and attr_i < attr_n then
      secondary = editor.clone(editor.at(ATTRS, attr_i))
      attr_i = attr_i + 1
    end
    old_cells[i + 1] = { terrain = terrain, flags = flags, decor = decor, main = main, secondary = secondary }
  end

  clear_vec(TILES)
  clear_vec(ATTRS)
  for idx = 0, new_total - 1 do
    local x = idx % new_w
    local y = (idx - x) / new_w
    local old_x = x - left
    local old_y = y - up
    if old_x >= 0 and old_x < old_w and old_y >= 0 and old_y < old_h then
      local cell = old_cells[old_y * old_w + old_x + 1]
      push_tile(cell.terrain)
      if band(cell.flags, 4) ~= 0 then push_attr(cell.decor) end
      if band(cell.flags, 8) ~= 0 then push_attr(cell.main) end
      if band(cell.flags, 16) ~= 0 then push_attr(cell.secondary) end
    else
      push_tile(0)
    end
  end

  local lm = n0(editor.member("Root.map_terrain.size.left_margin"))
  local tm = n0(editor.member("Root.map_terrain.size.top_margin"))
  local pw = n0(editor.member("Root.map_terrain.size.playable_width"))
  local ph = n0(editor.member("Root.map_terrain.size.playable_height"))
  editor.set_member("Root.map_terrain.size.width", new_w)
  editor.set_member("Root.map_terrain.size.height", new_h)
  local room_w = new_w - lm
  if room_w < 0 then room_w = 0 end
  local room_h = new_h - tm
  if room_h < 0 then room_h = 0 end
  if pw > room_w then pw = room_w end
  if ph > room_h then ph = room_h end
  editor.set_member("Root.map_terrain.size.playable_width", pw)
  editor.set_member("Root.map_terrain.size.playable_height", ph)

  editor.ensure(AGENTS)
  local agent_n = editor.count(AGENTS)
  local last_unit = {}
  for i = 0, agent_n - 1 do
    local c = n0(editor.member(editor.at(AGENTS, i), "agent_info.cell_idx"))
    if c >= 0 and c < old_total then last_unit[c + 1] = i end
  end
  local i = editor.count(AGENTS) - 1
  while i >= 0 do
    local agent = editor.at(AGENTS, i)
    local c = n0(editor.member(agent, "agent_info.cell_idx"))
    if c >= 0 and c < old_total then
      if last_unit[c + 1] == i then
        local mapped = editor.remap_cell(c, old_w, left, up, new_w, new_h)
        if mapped == nil then editor.remove(AGENTS, i)
        else editor.set_member(agent, "agent_info.cell_idx", mapped) end
      else
        editor.remove(AGENTS, i)
      end
    elseif c < new_total then
      editor.remove(AGENTS, i)
    end
    i = i - 1
  end

  editor.ensure(EVENTS)
  local event_n = editor.count(EVENTS)
  local last_b = {}
  local last_f = {}
  for k = 0, event_n - 1 do
    local ev = editor.at(EVENTS, k)
    local c = n0(editor.get(ev, "tile_index"))
    if c >= 0 and c < old_total then
      if is_building(ev) then last_b[c + 1] = k end
      if is_fort(ev) then last_f[c + 1] = k end
    end
  end
  i = editor.count(EVENTS) - 1
  while i >= 0 do
    local ev = editor.at(EVENTS, i)
    local c = n0(editor.get(ev, "tile_index"))
    if is_building(ev) or is_fort(ev) then
      local keep = c >= 0 and c < old_total and (last_b[c + 1] == i or last_f[c + 1] == i)
      if keep then
        local mapped = editor.remap_cell(c, old_w, left, up, new_w, new_h)
        if mapped == nil then editor.remove(EVENTS, i)
        else editor.set(ev, "tile_index", mapped) end
      else
        editor.remove(EVENTS, i)
      end
    else
      local mapped = editor.remap_cell(c, old_w, left, up, new_w, new_h)
      if mapped ~= nil then editor.set(ev, "tile_index", mapped) end
    end
    i = i - 1
  end

  i = editor.count(POINTS) - 1
  while i >= 0 do
    local rp = editor.at(POINTS, i)
    local mapped = editor.remap_cell(n0(editor.get(rp, "cell_idx")), old_w, left, up, new_w, new_h)
    if mapped == nil then editor.remove(POINTS, i)
    else editor.set(rp, "cell_idx", mapped) end
    i = i - 1
  end
end)

local function unit_at(cell)
  local found = nil
  local n = editor.count(AGENTS)
  for i = 0, n - 1 do
    local agent = editor.at(AGENTS, i)
    if n0(editor.member(agent, "agent_info.cell_idx")) == cell then found = agent end
  end
  return found
end

local function next_agent_id()
  local max_id = 0
  local n = editor.count(AGENTS)
  for i = 0, n - 1 do
    local id = n0(editor.member(editor.at(AGENTS, i), "agent_info.agent_id"))
    if id > max_id then max_id = id end
  end
  return max_id + 1
end

local function remove_units(cell)
  local i = editor.count(AGENTS) - 1
  while i >= 0 do
    if n0(editor.member(editor.at(AGENTS, i), "agent_info.cell_idx")) == cell then
      editor.remove(AGENTS, i)
    end
    i = i - 1
  end
end

editor.action("copy_unit", function(ctx)
  local unit = unit_at(n0(ctx.cell_index))
  if unit == nil then editor.keep("unit", nil)
  else editor.keep("unit", unit) end
end)

editor.action("paste_unit", function(ctx)
  local cell = n0(ctx.cell_index)
  local id = next_agent_id()
  remove_units(cell)
  local src = editor.kept("unit")
  if src == nil then return end
  editor.set_member(src, "agent_info.cell_idx", cell)
  editor.set_member(src, "agent_info.agent_id", id)
  editor.append(AGENTS, src)
end)

local function landmark_at(cell, want_fort)
  local found = nil
  local n = editor.count(EVENTS)
  for i = 0, n - 1 do
    local ev = editor.at(EVENTS, i)
    if n0(editor.get(ev, "tile_index")) == cell then
      if want_fort and is_fort(ev) then found = ev end
      if not want_fort and is_building(ev) then found = ev end
    end
  end
  return found
end

local function remove_landmarks(cell)
  local i = editor.count(EVENTS) - 1
  while i >= 0 do
    local ev = editor.at(EVENTS, i)
    if n0(editor.get(ev, "tile_index")) == cell and (is_building(ev) or is_fort(ev)) then
      editor.remove(EVENTS, i)
    end
    i = i - 1
  end
end

editor.action("copy_landmarks", function(ctx)
  local cell = n0(ctx.cell_index)
  local bldg = landmark_at(cell, false)
  local fort = landmark_at(cell, true)
  if bldg == nil then editor.keep("bldg", nil) else editor.keep("bldg", bldg) end
  if fort == nil then editor.keep("fort", nil) else editor.keep("fort", fort) end
end)

editor.action("paste_landmarks", function(ctx)
  local cell = n0(ctx.cell_index)
  local bldg = editor.kept("bldg")
  local fort = editor.kept("fort")
  remove_landmarks(cell)
  if bldg ~= nil then
    editor.set(bldg, "tile_index", cell)
    editor.append(EVENTS, bldg)
  end
  if fort ~= nil then
    editor.set(fort, "tile_index", cell)
    editor.append(EVENTS, fort)
  end
end)

editor.action("add_route", function(ctx)
  local flag = ctx.input.flag
  if flag == nil then flag = 1 end
  local row = editor.append(ROUTES)
  editor.set(row, "flag", flag)
  editor.fill(row, "params", ctx.input.cells)
end)

editor.action("set_route", function(ctx)
  if ctx.object == nil then return end
  local flag = ctx.input.flag
  if flag == nil then flag = 1 end
  editor.set(ctx.object, "flag", flag)
  editor.fill(ctx.object, "params", ctx.input.cells)
end)

editor.action("delete_route", function(ctx)
  if ctx.index == nil then return end
  editor.remove(ROUTES, ctx.index)
end)
