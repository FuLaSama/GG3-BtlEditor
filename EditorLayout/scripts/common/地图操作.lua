-- 扩图。新格是地形 0。脚本事件在新地图外时保留原索引。增援点落在外面就删掉。

local TILES = "/1/2"
local ATTRS = "/1/3"
local AGENTS = "/6/0"
local EVENTS = "/5/0"
local POINTS = "/3/1"
local TILE = { "u8", "u8", "i8", "i8" }
local SIZE = { "u16", "u16", "u16", "u16", "u16", "u16" }

local function band(a, b) return editor.band(a, b) end
local function rshift(a, n) return editor.rshift(a, n) end

local function n0(v)
  if v == nil then return 0 end
  return v
end

local function size_n(index)
  return n0(editor.get("/1/0." .. index))
end

local function agent_cell(i)
  return game.units.get(i).cell_index
end

local function clear_vec(path)
  local i = editor.count(path) - 1
  while i >= 0 do
    editor.remove(path, i)
    i = i - 1
  end
end

local function push_tile(terrain)
  editor.ensure(TILES, "u16")
  local i = editor.append(TILES)
  if terrain ~= 0 then editor.set("/1/2[" .. i .. "]", "u16", terrain) end
end

local function read_attr(index)
  -- 复制完整 struct，而非只取四个已知值，保留额外成员与类型布局。
  return editor.clone(editor.at(ATTRS,index))
end

local function push_attr(parts)
  editor.insert(ATTRS,editor.count(ATTRS),parts or editor.struct(TILE))
end

local function is_building(i)
  return editor.get("/5/0[" .. i .. "]/3/0.0") ~= nil
end

local function is_fort(i)
  return editor.get("/5/0[" .. i .. "]/4/0") ~= nil or editor.get("/5/0[" .. i .. "]/4/3") ~= nil
end

editor.modules.game_model.map_methods.resize = function(options)
  local ctx = {input=options or {}}
  local left = n0(ctx.input.left)
  local right = n0(ctx.input.right)
  local up = n0(ctx.input.up)
  local down = n0(ctx.input.down)
  if left == 0 and right == 0 and up == 0 and down == 0 then return end
  local old_w = size_n(0)
  local old_h = size_n(1)
  if old_w <= 0 or old_h <= 0 then return end
  local new_w = old_w + left + right
  local new_h = old_h + up + down
  if new_w <= 0 or new_h <= 0 or new_w > 500 or new_h > 500 then return end
  editor.internal.flush_cells()
  local old_total = old_w * old_h
  local new_total = new_w * new_h

  local tile_n = editor.count(TILES)
  local attr_n = editor.count(ATTRS)
  local attr_i = 0
  local old_cells = {}
  for i = 0, old_total - 1 do
    local terrain = 9001
    if i < tile_n then terrain = editor.get("/1/2[" .. i .. "]") end
    local flags = rshift(terrain, 8)
    local decor, main, secondary
    if band(flags, 4) ~= 0 and attr_i < attr_n then
      decor = read_attr(attr_i)
      attr_i = attr_i + 1
    end
    if band(flags, 8) ~= 0 and attr_i < attr_n then
      main = read_attr(attr_i)
      attr_i = attr_i + 1
    end
    if band(flags, 16) ~= 0 and attr_i < attr_n then
      secondary = read_attr(attr_i)
      attr_i = attr_i + 1
    end
    old_cells[i + 1] = { terrain = terrain, flags = flags, decor = decor, main = main, secondary = secondary }
  end

  clear_vec(TILES)
  local extra_attributes={}
  for at=attr_i,attr_n-1 do extra_attributes[#extra_attributes+1]=read_attr(at) end
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

  for _,attribute in ipairs(extra_attributes) do push_attr(attribute) end

  local lm = size_n(2)
  local tm = size_n(3)
  local pw = size_n(4)
  local ph = size_n(5)
  editor.internal.ensure(editor.root(), "/1/0", SIZE)
  editor.set("/1/0.0", "u16", new_w)
  editor.set("/1/0.1", "u16", new_h)
  local room_w = new_w - lm
  if room_w < 0 then room_w = 0 end
  local room_h = new_h - tm
  if room_h < 0 then room_h = 0 end
  if pw > room_w then pw = room_w end
  if ph > room_h then ph = room_h end
  editor.set("/1/0.4", "u16", pw)
  editor.set("/1/0.5", "u16", ph)

  editor.ensure(AGENTS, "table")
  local agent_n = editor.count(AGENTS)
  local last_unit = {}
  for i = 0, agent_n - 1 do
    local c = n0(agent_cell(i))
    if c >= 0 and c < old_total then last_unit[c + 1] = i end
  end
  local i = editor.count(AGENTS) - 1
  while i >= 0 do
    local c = n0(agent_cell(i))
    if c >= 0 and c < old_total then
      if last_unit[c + 1] == i then
        local mapped = editor.remap_cell(c, old_w, left, up, new_w, new_h)
        if mapped == nil then editor.remove(AGENTS, i)
        else game.units.get(i).cell_index = mapped end
      else
        editor.remove(AGENTS, i)
      end
    elseif c < new_total then
      editor.remove(AGENTS, i)
    end
    i = i - 1
  end

  editor.ensure(EVENTS, "table")
  local event_n = editor.count(EVENTS)
  local last_b = {}
  local last_f = {}
  for k = 0, event_n - 1 do
    local c = n0(game.events.get(k).cell_index)
    if c >= 0 and c < old_total then
      if is_building(k) then last_b[c + 1] = k end
      if is_fort(k) then last_f[c + 1] = k end
    end
  end
  i = editor.count(EVENTS) - 1
  while i >= 0 do
    local c = n0(game.events.get(i).cell_index)
    if is_building(i) or is_fort(i) then
      local keep = c >= 0 and c < old_total and (last_b[c + 1] == i or last_f[c + 1] == i)
      if keep then
        local mapped = editor.remap_cell(c, old_w, left, up, new_w, new_h)
        if mapped == nil then editor.remove(EVENTS, i)
        else game.events.get(i).cell_index = mapped end
      else
        editor.remove(EVENTS, i)
      end
    else
      local mapped = editor.remap_cell(c, old_w, left, up, new_w, new_h)
      if mapped ~= nil then game.events.get(i).cell_index = mapped end
    end
    i = i - 1
  end

  i = editor.count(POINTS) - 1
  while i >= 0 do
    local mapped = editor.remap_cell(n0(game.reinforcements.get(i).cell), old_w, left, up, new_w, new_h)
    if mapped == nil then editor.remove(POINTS, i)
    else game.reinforcements.get(i).cell = mapped end
    i = i - 1
  end
end
