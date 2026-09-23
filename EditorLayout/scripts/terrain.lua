-- 刷地质、海、装饰、主地形、次地形、偏移、地形粘贴。
-- attributes 只收有标志的层，顺序是装饰、主地形、次地形。与 TerrainEdits.Store 一致。

local TILES = "Root.map_terrain.tiles"
local ATTRS = "Root.map_terrain.attributes"
local DECOR, MAIN, SEC, SEA = 4, 8, 16, 2

local function band(a, b) return editor.band(a, b) end
local function bor(a, b) return editor.bor(a, b) end
local function bxor(a, b) return editor.bxor(a, b) end
local function lshift(a, n) return editor.lshift(a, n) end
local function rshift(a, n) return editor.rshift(a, n) end

local function layer_bit(layer)
  if layer == "decor" then return DECOR end
  if layer == "main" then return MAIN end
  if layer == "secondary" then return SEC end
  error("未知地形层")
end

local function split(t)
  local climate = band(t, 7)
  local variation = band(rshift(t, 3), 31)
  local flags = band(rshift(t, 8), 255)
  return climate, variation, flags
end

local function pack(climate, variation, flags)
  return bor(bor(band(climate, 7), lshift(band(variation, 31), 3)), lshift(band(flags, 255), 8))
end

local function bit_on(t, bit)
  return band(band(rshift(t, 8), 255), bit) ~= 0
end

local function map_cells()
  local w = editor.member("Root.map_terrain.size.width")
  local h = editor.member("Root.map_terrain.size.height")
  if w == nil or h == nil or w <= 0 or h <= 0 then return 0 end
  return w * h
end

local function read_attr(index)
  local h = editor.at(ATTRS, index)
  return {
    editor.member(h, "byte0"),
    editor.member(h, "byte1"),
    editor.member(h, "byte2"),
    editor.member(h, "byte3"),
  }
end

local function read_cells()
  local total = map_cells()
  local tile_count = editor.count(TILES)
  local attr_count = editor.count(ATTRS)
  local ai = 0
  local cells = {}
  for i = 0, total - 1 do
    local t = 9001
    if i < tile_count then t = editor.at(TILES, i) end
    local cell = { terrain = t }
    if bit_on(t, DECOR) then
      if ai < attr_count then cell.decor = read_attr(ai) end
      ai = ai + 1
    end
    if bit_on(t, MAIN) then
      if ai < attr_count then cell.main = read_attr(ai) end
      ai = ai + 1
    end
    if bit_on(t, SEC) then
      if ai < attr_count then cell.sec = read_attr(ai) end
      ai = ai + 1
    end
    cells[i] = cell
  end
  return cells, total
end

local function make_attr(parts)
  local h = editor.struct("TileAttr")
  local p = parts or { 0, 0, 0, 0 }
  editor.set_member(h, "byte0", p[1] or 0)
  editor.set_member(h, "byte1", p[2] or 0)
  editor.set_member(h, "byte2", p[3] or 0)
  editor.set_member(h, "byte3", p[4] or 0)
  return h
end

local function write_cells(cells, total)
  editor.ensure(TILES)
  editor.ensure(ATTRS)
  while editor.count(TILES) > total do
    editor.remove(TILES, editor.count(TILES) - 1)
  end
  while editor.count(TILES) < total do
    editor.append(TILES)
  end
  for i = 0, total - 1 do
    editor.set_at(TILES, i, cells[i].terrain)
  end
  while editor.count(ATTRS) > 0 do
    editor.remove(ATTRS, editor.count(ATTRS) - 1)
  end
  for i = 0, total - 1 do
    local c = cells[i]
    if bit_on(c.terrain, DECOR) then editor.append(ATTRS, make_attr(c.decor)) end
    if bit_on(c.terrain, MAIN) then editor.append(ATTRS, make_attr(c.main)) end
    if bit_on(c.terrain, SEC) then editor.append(ATTRS, make_attr(c.sec)) end
  end
end

local function with_cell(ctx, fn)
  if ctx.cell_index == nil then return end
  local cells, total = read_cells()
  local i = ctx.cell_index
  if i < 0 or i >= total then return end
  if fn(cells[i]) == false then return end
  write_cells(cells, total)
end

local function copy_parts(v)
  if v == nil then return nil end
  return { v[1], v[2], v[3], v[4] }
end

editor.action("apply_climate", function(ctx)
  local item = ctx.item or {}
  with_cell(ctx, function(cell)
    local climate, variation, flags = split(cell.terrain)
    if item.sea then
      cell.terrain = pack(climate, variation, bor(flags, SEA))
    else
      if item.t ~= nil then climate = item.t end
      cell.terrain = pack(climate, item.variant or 0, flags)
    end
  end)
end)

editor.action("set_sea", function(ctx)
  with_cell(ctx, function(cell)
    local climate, variation, flags = split(cell.terrain)
    if ctx.value then flags = bor(flags, SEA) else flags = band(flags, bxor(255, SEA)) end
    cell.terrain = pack(climate, variation, flags)
  end)
end)

editor.action("apply_layer", function(ctx)
  local item = ctx.item or {}
  local layer = item.layer
  if layer == nil and ctx.input ~= nil then layer = ctx.input.layer end
  with_cell(ctx, function(cell)
    local bit = layer_bit(layer)
    local climate, variation, flags = split(cell.terrain)
    cell.terrain = pack(climate, variation, bor(flags, bit))
    local attr = { item.terrain_id or 0, item.variant or 0, item.dx or 0, item.dy or 0 }
    if layer == "decor" then cell.decor = attr
    elseif layer == "main" then cell.main = attr
    else cell.sec = attr end
  end)
end)

editor.action("clear_layer", function(ctx)
  local layer = ctx.input.layer
  with_cell(ctx, function(cell)
    local climate, variation, flags = split(cell.terrain)
    if layer == "sea" then
      cell.terrain = pack(climate, variation, band(flags, bxor(255, SEA)))
      return
    end
    local bit = layer_bit(layer)
    cell.terrain = pack(climate, variation, band(flags, bxor(255, bit)))
    if layer == "decor" then cell.decor = nil
    elseif layer == "main" then cell.main = nil
    else cell.sec = nil end
  end)
end)

editor.action("set_offset", function(ctx)
  local layer = ctx.input.layer
  with_cell(ctx, function(cell)
    local bit = layer_bit(layer)
    local attr = cell.sec
    if layer == "decor" then attr = cell.decor
    elseif layer == "main" then attr = cell.main end
    if not bit_on(cell.terrain, bit) or attr == nil or attr[1] == 0 then return false end
    attr[3] = ctx.input.dx
    attr[4] = ctx.input.dy
  end)
end)

editor.action("paste_terrain", function(ctx)
  local src = ctx.input or {}
  with_cell(ctx, function(cell)
    cell.terrain = src.terrain
    cell.decor = copy_parts(src.decor)
    cell.main = copy_parts(src.main)
    cell.sec = copy_parts(src.secondary)
  end)
end)
