-- 已确认字段的公共适配层。普通工具脚本只操作 game 的具名属性。
local bridge = editor.internal
local data = editor.data
local function fail(message) error(message, 3) end
local function integer(value, maximum, label)
  if type(value) ~= "number" or value ~= value or value < 0 or value > maximum or value % 1 ~= 0 then
    fail(label .. "需要 0 到 " .. maximum .. " 的整数")
  end
  return value
end
local function scalar(path, kind, readonly, layout)
  return {
    get=function(anchor) return bridge.get(anchor, path) end,
    set=function(anchor, value)
      if readonly then fail("这个属性只读") end
      bridge.set(anchor, path, kind, value, layout)
    end,
  }
end
local function packed(path, shift, default, label, layout)
  local mask = editor.lshift(255, shift)
  return {
    get=function(anchor)
      local word = bridge.get(anchor, path)
      if word == nil then return nil end
      return editor.band(editor.rshift(word, shift), 255)
    end,
    set=function(anchor, value)
      integer(value, 255, label or "属性")
      local word = bridge.get(anchor, path) or default or 0
      word = editor.bor(editor.band(word, editor.bxor(4294967295, mask)), editor.lshift(value, shift))
      bridge.set(anchor, path, shift > 8 and "u32" or (default ~= nil and "u32" or "u16"), word, layout)
    end,
  }
end
local function proxy(anchor, label, fields, methods)
  local object = {}
  return setmetatable(object, {
    __index=function(_, key)
      bridge.check_context(anchor)
      if fields[key] then return fields[key].get(anchor) end
      if methods and methods[key] then return function(...) return methods[key](object, ...) end end
      fail(label .. "没有属性「" .. tostring(key) .. "」")
    end,
    __newindex=function(_, key, value)
      bridge.check_context(anchor)
      if not fields[key] then fail(label .. "没有属性「" .. tostring(key) .. "」") end
      local ok, message = pcall(fields[key].set, anchor, value)
      if not ok then fail(label .. "." .. tostring(key) .. "：" .. tostring(message)) end
    end,
    __metatable="game object",
  })
end

local unit_layout = {"u16","u16","u16","u16","u16","u16","u16","u16","u16","u16"}
local function unit_scalar(path, readonly) return scalar(path,"u16",readonly,unit_layout) end
local function unit_packed(path,shift,label) return packed(path,shift,nil,label,unit_layout) end
local unit_fields = {
  cell_index=unit_scalar("/0.0"), faction=unit_scalar("/0.1"),
  agent_id=unit_scalar("/0.2", true), type_id=unit_scalar("/0.3"),
  hp=unit_scalar("/0.6"), max_hp=unit_scalar("/0.7"),
  level=unit_packed("/0.4", 0, "经验等级"), stack=unit_packed("/0.4", 8, "堆叠编制"),
  direction=unit_packed("/0.5", 0, "朝向"), mobility=unit_packed("/0.5", 8, "移动力"),
  general_id=scalar("/11/0", "u16"),
}
local function update(object, _, values)
  if type(values) ~= "table" then fail("update 需要属性表，请使用 object:update {hp=10}") end
  for key, value in pairs(values) do object[key] = value end
end
local unit_methods = {
  update=update,
  heal=function(unit)
    if unit.max_hp == nil then fail("部队没有最大生命值") end
    unit.hp = unit.max_hp
  end,
  move=function(unit, _, x, y) unit.cell_index = game.map.cell(x, y).index end,
}
local faction_layout = {"u16","u16","u8","u8","u8","u8","u32","u32","f32","f32","f32","u32","u16","u16"}
local function faction_scalar(path,kind,readonly) return scalar(path,kind,readonly,faction_layout) end
local faction_fields = {
  id=faction_scalar("/0.0", "u16", true), country=faction_scalar("/0.1", "u16"),
  camp=faction_scalar("/0.2", "u8"), gold=faction_scalar("/0.6", "u32"), tech=faction_scalar("/0.7", "u32"),
  income_multiplier=faction_scalar("/0.8", "f32"), damage_multiplier=faction_scalar("/0.9", "f32"),
  hp_multiplier=faction_scalar("/0.10", "f32"),
  is_ai={
    get=function(anchor) local value=bridge.get(anchor, "/0.3"); return value ~= nil and value ~= 0 end,
    set=function(anchor, value)
      if type(value) ~= "boolean" then fail("is_ai 需要 true 或 false") end
      bridge.set(anchor, "/0.3", "u8", value and 1 or 0, faction_layout)
    end,
  },
}
local color_fields = {r=packed("/0.11",24,255,nil,faction_layout), g=packed("/0.11",16,255,nil,faction_layout),
  b=packed("/0.11",8,255,nil,faction_layout), a=packed("/0.11",0,255,nil,faction_layout)}
faction_fields.color = {
  get=function(anchor) return proxy(anchor, "颜色", color_fields) end,
  set=function(anchor, values)
    if type(values) ~= "table" then fail("color 需要 {r=..., g=..., b=..., a=...}") end
    update(proxy(anchor, "颜色", color_fields), nil, values)
  end,
}

game = {units={}, factions={}}
function game.units.get(index)
  integer(index, 2147483647, "部队下标")
  local anchor = bridge.anchor(data.row("/6/0", index))
  if anchor == nil then return nil end
  return proxy(anchor, "部队", unit_fields, unit_methods)
end
function game.units.each(faction)
  local token = editor.root()
  local index, count = -1, data.count("/6/0")
  return function()
    bridge.check_context(token)
    while index + 1 < count do
      index = index + 1
      local unit = game.units.get(index)
      if unit and (faction == nil or unit.faction == faction) then return unit end
    end
  end
end
function game.units.all()
  local result = {}
  for unit in game.units.each() do result[#result+1] = unit end
  return result
end
function game.units.at_cell(index)
  if index == nil then return nil end
  for unit in game.units.each() do if unit.cell_index == index then return unit end end
  return nil
end
function game.units.selected(ctx)
  if ctx.unit_index ~= nil then return game.units.get(ctx.unit_index) end
  return game.units.at_cell(ctx.cell_index)
end
function game.factions.get(index)
  integer(index, 2147483647, "势力下标")
  local anchor = bridge.anchor(data.row("/4/0", index))
  if anchor == nil then return nil end
  return proxy(anchor, "势力", faction_fields, {update=update})
end
function game.factions.each()
  local token = editor.root()
  local index, count = -1, data.count("/4/0")
  return function()
    bridge.check_context(token)
    index = index + 1
    if index < count then return game.factions.get(index) end
  end
end
function game.factions.all()
  local result = {}
  for faction in game.factions.each() do result[#result+1] = faction end
  return result
end
function game.factions.by_id(id)
  for faction in game.factions.each() do if faction.id == id then return faction end end
  return nil
end
function game.factions.selected(ctx)
  if ctx.faction_index ~= nil then return game.factions.get(ctx.faction_index) end
  local unit = game.units.selected(ctx)
  return unit and game.factions.by_id(unit.faction) or nil
end

local stage_fields = {round_limit=scalar("/2/1", "u16")}
stage_fields.fog = {
  get=function(anchor) local value=bridge.get(anchor, "/1/4"); return not (value == true or value == 1) end,
  set=function(anchor, value)
    if type(value) ~= "boolean" then fail("fog 需要 true 或 false") end
    bridge.set(anchor, "/1/4", "bool", not value)
  end,
}
local size_layout = {"u16","u16","u16","u16","u16","u16"}
local map_fields = {
  width=scalar("/1/0.0", "u16", true), height=scalar("/1/0.1", "u16", true),
  left_margin=scalar("/1/0.2", "u16",false,size_layout), top_margin=scalar("/1/0.3", "u16",false,size_layout),
  play_width=scalar("/1/0.4", "u16",false,size_layout), play_height=scalar("/1/0.5", "u16",false,size_layout),
}
local function cell(index)
  local width, height = game.map.width or 0, game.map.height or 0
  integer(index, math.max(0, width * height - 1), "格子下标")
  if width <= 0 or height <= 0 then fail("关卡没有有效地图") end
  local anchor = editor.root()
  local fields = {}
  for _, property in ipairs({"climate", "variant", "sea", "playable"}) do
    local key = property
    fields[key] = {
      get=function() return bridge.cell_get(anchor,index,key) end,
      set=function(_, value) bridge.cell_set(anchor,index,key,value) end,
    }
  end
  for key, value in pairs({index=index, x=index % width, y=math.floor(index / width)}) do
    local fixed = value
    fields[key] = {get=function() return fixed end, set=function() fail("格子坐标只读") end}
  end
  for _, name in ipairs({"main", "secondary", "decor"}) do
    local layer = name
    local layer_fields = {}
    for _, property in ipairs({"id", "variant", "dx", "dy", "present"}) do
      local key = property
      layer_fields[key] = {
        get=function() return bridge.cell_get(anchor,index,key,layer) end,
        set=function(_,value) bridge.cell_set(anchor,index,key,value,layer) end,
      }
    end
    fields[layer] = {
      get=function() return proxy(anchor,"图层",layer_fields,{update=update}) end,
      set=function(_,values)
        if values == nil then bridge.cell_set(anchor,index,"present",false,layer)
        elseif type(values) == "table" then update(proxy(anchor,"图层",layer_fields),nil,values)
        else fail("图层需要属性表或 nil") end
      end,
    }
  end
  return proxy(anchor,"格子",fields,{update=update})
end
local map_methods = {}
function map_methods.cell(x,y)
  integer(x, math.max(0,(game.map.width or 0)-1), "横坐标")
  integer(y, math.max(0,(game.map.height or 0)-1), "纵坐标")
  return cell(y * game.map.width + x)
end
map_methods.cell_at_index = cell
function map_methods.cells()
  local token = editor.root()
  local index, count = -1, (game.map.width or 0) * (game.map.height or 0)
  return function() bridge.check_context(token); index=index+1; if index<count then return cell(index) end end
end
local function singleton(label, fields, methods)
  return setmetatable({}, {
    __index=function(_,key)
      if methods and methods[key] then return methods[key] end
      return proxy(editor.root(),label,fields)[key]
    end,
    __newindex=function(_,key,value) proxy(editor.root(),label,fields)[key]=value end,
    __metatable="game collection",
  })
end
game.map = singleton("地图",map_fields,map_methods)
game.stage = singleton("关卡",stage_fields)
-- 其他公共适配模块复用实现；工具作者无需使用这个模块。
editor.modules.game_model = {scalar=scalar, proxy=proxy, update=update, singleton=singleton,
  unit_fields=unit_fields, unit_methods=unit_methods, unit_layout=unit_layout,
  faction_fields=faction_fields, faction_layout=faction_layout, map_methods=map_methods, cell=cell}

function game._run_tool(run,target,ctx,params)
  local count = 0
  local function visit(object)
    if object == nil then fail("没有选中对应对象，请先选择部队、势力或地图格子") end
    if run(object,params,ctx) ~= false then count=count+1 end
  end
  if target == "selected_unit" then visit(game.units.selected(ctx))
  elseif target == "all_units" then for unit in game.units.each() do visit(unit) end
  elseif target == "faction_units" then for unit in game.units.each(params.faction) do visit(unit) end
  elseif target == "selected_faction" then visit(game.factions.selected(ctx))
  elseif target == "all_factions" then for faction in game.factions.each() do visit(faction) end
  elseif target == "selected_cell" then visit(ctx.cell_index ~= nil and cell(ctx.cell_index) or nil)
  elseif target == "all_cells" then for item in game.map.cells() do visit(item) end
  elseif target == "stage" then visit(game.stage)
  elseif target == "map" then visit(game.map)
  else fail("未知作用范围") end
  if count == 0 then fail("没有符合条件的对象，或工具跳过了所有对象") end
  return count
end
