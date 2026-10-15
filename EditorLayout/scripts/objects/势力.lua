local model,bridge,data=editor.modules.objects,editor.internal,editor.data
local layout={"u16","u16","u8","u8","u8","u8","u32","u32","f32","f32","f32","u32","u16","i16"}
local function scalar(path,kind,readonly) return model.scalar(path,kind,readonly,layout) end
local colors={r=model.packed("/0.11",24,255,nil,layout),g=model.packed("/0.11",16,255,nil,layout),
  b=model.packed("/0.11",8,255,nil,layout),a=model.packed("/0.11",0,255,nil,layout)}
local fields={
  id=scalar("/0.0","u16",true),country=scalar("/0.1","u16"),camp=scalar("/0.2","u8"),
  gold=scalar("/0.6","u32"),tech=scalar("/0.7","u32"),income_multiplier=scalar("/0.8","f32"),
  damage_multiplier=scalar("/0.9","f32"),hp_multiplier=scalar("/0.10","f32"),parameter_5=scalar("/0.4","u8"),
  alignment_1=scalar("/0.5","u8"),alignment_2=scalar("/0.12","u16"),config_id=scalar("/0.13","i16"),
  general_flag=model.scalar("/7","u8"),config_ref=model.scalar("/8","u16"),
  is_ai={get=function(a) local value=bridge.get(a,"/0.3"); return value~=nil and value~=0 end,
    set=function(a,value)
      if type(value)~="boolean" then model.fail("is_ai 需要 true 或 false") end
      bridge.set(a,"/0.3","u8",value and 1 or 0,layout)
    end},
  color={get=function(a) return model.proxy(a,"颜色",colors) end,set=function(a,values)
    if type(values)~="table" then model.fail("color 需要 {r=..., g=..., b=..., a=...}") end
    model.update(model.proxy(a,"颜色",colors),values)
  end},
}
local relations={faction_id=model.scalar("/0","u16"),items=model.array("/1","u16")}
local countries=model.collection {path="/4/1",label="势力国家关系",fields=relations}
local links=model.collection {path="/4/3",label="势力关联",fields=relations}
local factions
factions=model.collection {
  path="/4/0",label="势力",fields=fields,
  methods={set_id=function(faction,id) bridge.set(bridge.anchor(data.row("/4/0",faction.index)),"/0.0","u16",id,layout) end},
  prepare=function(a) bridge.ensure(a,"/0",layout) end,
  initialize=function(object)
    local next_id=0
    for faction in factions.each() do
      if faction.index~=object.index then next_id=math.max(next_id,(faction.id or 0)+1) end
    end
    object:set_id(next_id)
    object:update {country=1,camp=1,is_ai=false,parameter_5=0,alignment_1=0,gold=100,tech=0,
      income_multiplier=1,damage_multiplier=1,hp_multiplier=1,color={r=255,g=255,b=255,a=255},
      alignment_2=0,config_id=0,general_flag=1,config_ref=1}
  end,
  after_create=function(faction)
    countries.create {faction_id=faction.id,items={}}
    links.create {faction_id=faction.id,items={}}
  end,
  before_remove=function(faction)
    local id=faction.id
    for _,related in ipairs({countries,links}) do related.remove_where(function(row) return row.faction_id==id end) end
  end,
  after_move=function(faction,_,to)
    for _,related in ipairs({countries,links}) do
      for row in related.each() do
        if row.faction_id==faction.id then related.move(row.index,math.min(to,related.count()-1)); break end
      end
    end
  end,
  api={
    by_id=function(id) for faction in factions.each() do if faction.id==id then return faction end end end,
    selected=function(ctx)
      if ctx.faction_index~=nil then return factions.get(ctx.faction_index) end
      local unit=game.units.selected(ctx)
      return unit and factions.by_id(unit.faction) or nil
    end,
  },
}
game.factions=factions
