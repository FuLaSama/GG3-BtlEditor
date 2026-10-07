-- 页面的空值规则与局部修改工具不同：基础 struct 的空输入写为 0。
editor.resolve("unit",function(ctx) return game.units.index_at_cell(ctx.cell_index) end)
local function write(unit,input,cell)
  if cell~=nil then unit.cell_index=cell end
  unit:set_id(input.agent or 0)
  unit:update {faction=input.faction or 0,type_id=input.unit or 0,
    level=input.level or 0,stack=input.stack or 0,direction=input.direction or 0,mobility=input.mobility or 0,
    hp=input.hp or 0,max_hp=input.max_hp or 0,parameter_8=input.val8 or 0,parameter_9=input.val9 or 0}
  unit.play_mode=input.play_mode; unit.ai_target=input.ai_target; unit.faction_extra=input.faction_extra
end
editor.action("apply_unit",function(ctx)
  local unit=game.units.get(ctx.index); if not unit then return end
  write(unit,ctx.input)
  write_unit_behavior(unit.index,ctx.input); write_unit_general(unit.index,ctx.input); write_unit_ex(unit.index,ctx.input)
end)
editor.action("place_unit",function(ctx)
  local input=ctx.input or {}; local cell=ctx.cell_index or input.cell
  if cell==nil then return end
  input.unit=input.unit or 101
  write(game.units.create(),input,cell)
end)
editor.action("delete_unit",function(ctx)
  local index=ctx.index
  if index==nil and ctx.cell_index~=nil then index=game.units.index_at_cell(ctx.cell_index,true) end
  game.units.remove(index)
end)
