local model,bridge,data=editor.modules.objects,editor.internal,editor.data
local named=model.named
local fields={
  cell_index=named("unit.cell_index"),faction=named("unit.faction"),agent_id=named("unit.agent_id",true),
  type_id=named("unit.type_id"),hp=named("unit.hp"),max_hp=named("unit.max_hp"),
  level=named("unit.level"),stack=named("unit.stack"),direction=named("unit.direction"),mobility=named("unit.mobility"),
  general_id=named("unit.general.id"),parameter_8=named("unit.parameter_8"),parameter_9=named("unit.parameter_9"),
  play_mode=named("unit.play_mode"),ai_target=named("unit.ai_target"),faction_extra=named("unit.faction_extra"),
  behavior=model.optional("unit.behavior","部队行为",{
    parameter_0=named("unit.behavior.parameter_0"),id=named("unit.behavior.id"),
    parameter_2=named("unit.behavior.parameter_2"),radius=named("unit.behavior.radius"),
    parameter_4=named("unit.behavior.parameter_4"),center_cell=named("unit.behavior.center_cell"),parameter_6=named("unit.behavior.parameter_6"),
  }),
  general=model.optional("unit.general","将领",{
    id=named("unit.general.id"),active=named("unit.general.active"),parameter_2=named("unit.general.parameter_2"),medal=named("unit.general.medal"),
  }),
  special=model.optional("unit.special","特种配置",{
    id=named("unit.special.id"),parameter_1=named("unit.special.parameter_1"),hp=named("unit.special.hp"),
    max_hp=named("unit.special.max_hp"),parameter_4=named("unit.special.parameter_4"),parameter_5=named("unit.special.parameter_5"),
  }),
}
local units
units=model.collection {
  path="/6/0",label="部队",fields=fields,
  methods={
    heal=function(unit)
      if unit.max_hp==nil then model.fail("部队没有最大生命值") end
      unit.hp=unit.max_hp
    end,
    move=function(unit,x,y) unit.cell_index=game.map.cell(x,y).index end,
    set_id=function(unit,id) bridge.field_set(bridge.anchor(data.row("/6/0",unit.index)),"unit.agent_id",id) end,
  },
  prepare=function(anchor)
    bridge.field_set(anchor,"unit.info",true)
    bridge.field_set(anchor,"unit.agent_id",units.next_id())
  end,
  matches=function(unit,faction) return unit.faction==faction end,
  api={
    index_at_cell=function(cell,last) return bridge.index_at_cell("/6/0",bridge.field_path("unit.cell_index"),cell,last) end,
    at_cell=function(cell) return units.get(units.index_at_cell(cell)) end,
    selected=function(ctx)
      if ctx.unit_index~=nil then return units.get(ctx.unit_index) end
      return units.at_cell(ctx.cell_index)
    end,
    next_id=function()
      local maximum=0
      for unit in units.each() do maximum=math.max(maximum,unit.agent_id or 0) end
      return maximum+1
    end,
    remove_at_cell=function(cell) return units.remove_where(function(unit) return unit.cell_index==cell end) end,
  },
}
game.units=units
