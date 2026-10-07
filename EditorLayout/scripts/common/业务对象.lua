-- 所有字段映射集中在公共适配层。界面动作只使用具名对象。
local model, bridge, data = editor.modules.game_model, editor.internal, editor.data
local scalar, proxy, update = model.scalar, model.proxy, model.update
local function array(path, kind)
  return {get=function(a) return bridge.array_get(a,path) end,
    set=function(a,v) bridge.array_set(a,path,kind,v or {}) end}
end
local function optional(path, label, fields, prepare)
  return {get=function(a)
    if not bridge.exists(a,path) then return nil end
    return proxy(a,label,fields,{update=update})
  end, set=function(a,v)
    if v==nil then bridge.set(a,path,"u8",nil); return end
    assert(type(v)=="table", label.."需要属性表或 nil")
    bridge.ensure(a,path)
    if prepare then prepare(a) end
    update(proxy(a,label,fields),nil,v)
  end}
end
local function collection(path,label,fields,methods,prepare)
  local result = {}
  fields.index={get=function(a) return bridge.index(path,a) end,set=function() error("行下标只读") end}
  function result.count() return data.count(path) end
  function result.get(index)
    if index==nil then return nil end
    local anchor=bridge.anchor(data.row(path,index))
    if anchor==nil then return nil end
    return proxy(anchor,label,fields,methods or {update=update})
  end
  function result.create(values)
    data.ensure_vector(path,"table")
    local index=data.append(path)
    if prepare then prepare(bridge.anchor(data.row(path,index))) end
    local object=result.get(index)
    if values then update(object,nil,values) end
    return object
  end
  function result.remove(index)
    if type(index)=="table" then index=index.index end
    if index~=nil then data.remove(path,index) end
  end
  function result.move(from,to) data.move(path,from,to) end
  function result.each()
    local token=editor.root()
    local index,count=-1,result.count()
    return function() bridge.check_context(token); index=index+1; if index<count then return result.get(index) end end
  end
  function result.copy(index,key)
    if type(index)=="table" then index=index.index end
    if index==nil then editor.clipboard.clear(key) else editor.clipboard.copy(key,data.row(path,index)) end
  end
  function result.paste(key) return result.get(editor.clipboard.paste(path,key)) end
  return result
end

local function unit_scalar(path,kind) return scalar(path,kind or "u16",false,model.unit_layout) end
model.unit_fields.parameter_8=unit_scalar("/0.8")
model.unit_fields.parameter_9=unit_scalar("/0.9")
model.unit_fields.play_mode=scalar("/2","u8")
model.unit_fields.ai_target=scalar("/5","u8")
model.unit_fields.faction_extra=scalar("/6","u8")
model.unit_fields.behavior=optional("/3","部队行为",{
  parameter_0=scalar("/3/0","u8"), id=scalar("/3/1","u16"), parameter_2=scalar("/3/2","i16"),
  radius=scalar("/3/3","u8"), parameter_4=scalar("/3/4","u8"), center_cell=scalar("/3/5","u16"), parameter_6=scalar("/3/6","i16"),
})
model.unit_fields.general=optional("/11","将领",{
  id=scalar("/11/0","u16"), active=scalar("/11/1","bool"), parameter_2=scalar("/11/2","u8"),
})
model.unit_fields.special=optional("/10","特种配置",{
  id=scalar("/10/0","u16"), parameter_1=scalar("/10/1","u8"), hp=scalar("/10/2","u16"),
  max_hp=scalar("/10/3","u16"), parameter_4=scalar("/10/4","u16"), parameter_5=scalar("/10/5","u16"),
})
-- 身份字段只读；显式方法用于原编辑页的身份修改和创建。
model.unit_methods.set_id=function(_,self,id)
  local anchor=bridge.anchor(data.row("/6/0",self.index))
  bridge.set(anchor,"/0.2","u16",id,model.unit_layout)
end
local old_units=game.units
local units=collection("/6/0","部队",model.unit_fields,model.unit_methods,function(a)
  bridge.ensure(a,"/0",model.unit_layout)
  bridge.set(a,"/0.2","u16",game.units.next_id(),model.unit_layout)
end)
for _,name in ipairs({"get","count","create","remove","move","copy","paste"}) do old_units[name]=units[name] end
function game.units.index_at_cell(cell,last)
  return bridge.index_at_cell("/6/0","/0.0",cell,last)
end

function game.units.next_id()
  local max=0; for unit in game.units.each() do max=math.max(max,unit.agent_id or 0) end
  return max+1
end
function game.units.remove_at_cell(cell)
  for i=game.units.count()-1,0,-1 do if game.units.get(i).cell_index==cell then game.units.remove(i) end end
end

local function fs(path,kind) return scalar(path,kind,false,model.faction_layout) end
model.faction_fields.parameter_5=fs("/0.4","u8")
model.faction_fields.alignment_1=fs("/0.5","u8")
model.faction_fields.alignment_2=fs("/0.12","u16")
model.faction_fields.config_id=fs("/0.13","u16")
model.faction_fields.general_flag=scalar("/7","u8")
model.faction_fields.config_ref=scalar("/8","u16")
local relation_fields={faction_id=scalar("/0","u16"),items=array("/1","u16")}
local countries=collection("/4/1","势力国家关系",relation_fields)
local links=collection("/4/3","势力关联",{faction_id=scalar("/0","u16"),items=array("/1","u16")})
local factions=collection("/4/0","势力",model.faction_fields,{update=update,
  set_id=function(_,self,id) bridge.set(bridge.anchor(data.row("/4/0",self.index)),"/0.0","u16",id,model.faction_layout) end,
},function(a) bridge.ensure(a,"/0",model.faction_layout) end)
local old_factions=game.factions
old_factions.get=factions.get; old_factions.count=factions.count
function old_factions.create(values)
  local next_id=0
  for faction in old_factions.each() do next_id=math.max(next_id,(faction.id or 0)+1) end
  local object=factions.create()
  object:set_id(next_id)
  object:update {country=1,camp=1,is_ai=false,parameter_5=0,alignment_1=0,gold=100,tech=0,
    income_multiplier=1,damage_multiplier=1,hp_multiplier=1,color={r=255,g=255,b=255,a=255},
    alignment_2=0,config_id=0,general_flag=1,config_ref=1}
  if values then object:update(values) end
  countries.create {faction_id=next_id,items={}}
  links.create {faction_id=next_id,items={}}
  return object
end
function old_factions.remove(index)
  local object=old_factions.get(index); if not object then return end
  local id=object.id; factions.remove(index)
  for _,related in ipairs({countries,links}) do
    for i=related.count()-1,0,-1 do if related.get(i).faction_id==id then related.remove(i) end end
  end
end
function old_factions.move(from,to)
  if from==nil or to==nil or from==to then return end
  local id=old_factions.get(from).id; factions.move(from,to)
  for _,related in ipairs({countries,links}) do
    for i=0,related.count()-1 do
      if related.get(i).faction_id==id then related.move(i,math.min(to,related.count()-1)); break end
    end
  end
end

game.targets=collection("/2/0","关卡目标",{type=scalar("/0","u16"),value=scalar("/1","i16"),
  param1=scalar("/2","u16"),param2=scalar("/3","u16"),flag=scalar("/4","u8")})
game.reinforcements=collection("/3/1","增援点",{cell=scalar("/0","u16"),faction=scalar("/1","u8"),
  is_key=scalar("/2","bool"),flag=scalar("/3","u8")})
game.weather=collection("/9/0","天气",{type=scalar("/0","u8"),start=scalar("/1","u16"),duration=scalar("/2","u16")})
game.routes=collection("/6/1","路线",{flag=scalar("/0","u16"),cells=array("/1","u16")})

local building_layout={"u16","u16","u8","u8","i8","i8"}
local function bs(path,kind) return scalar(path,kind,false,building_layout) end
local building_fields={flag=bs("/3/0.0","u16"),type_id=bs("/3/0.1","u16"),extra=bs("/3/0.2","u8"),
  owner=bs("/3/0.3","u8"),dx=bs("/3/0.4","i8"),dy=bs("/3/0.5","i8"),parameter_6=scalar("/3/6","u8")}
game.events=collection("/5/0","事件",{cell_index=scalar("/0","u16"),parameter_1=scalar("/1","u16"),
  building=optional("/3","建筑",building_fields,function(a) bridge.ensure(a,"/3/0",building_layout) end),
  fort=optional("/4","工事",{id=scalar("/4/0","u8"),parameter_3=scalar("/4/3","u8")}),
},{update=update,prepare_building=function(_,self)
  local a=bridge.anchor(data.row("/5/0",self.index)); bridge.ensure(a,"/3/2"); bridge.ensure(a,"/3/4")
end})
function game.events.index_at_cell(cell,kind,last)
  if kind=="building" then return bridge.index_at_cell("/5/0","/0",cell,last,"/3/0.0") end
  if kind=="fort" then return bridge.index_at_cell("/5/0","/0",cell,last,"/4/0","/4/3") end
  if kind~=nil then return nil end
  return bridge.index_at_cell("/5/0","/0",cell,last)
end

function game.events.remove_landmarks(cell)
  for i=game.events.count()-1,0,-1 do
    local e=game.events.get(i)
    if e.cell_index==cell and (e.building or e.fort) then game.events.remove(i) end
  end
end
-- 原始画布剪贴板协议只在适配层解码；新工具使用具名 cell 属性。
model.map_methods.paste_terrain=function(index,source)
  bridge.cell_paste(editor.root(),index,source.terrain,source.decor,source.main,source.secondary)
end
function model.map_methods.try_cell_at_index(index)
  if type(index)~="number" or index<0 or index%1~=0 or index>=(game.map.width or 0)*(game.map.height or 0) then return nil end
  return model.cell(index)
end

function game.units.at_cell(index)
  return game.units.get(game.units.index_at_cell(index))
end
