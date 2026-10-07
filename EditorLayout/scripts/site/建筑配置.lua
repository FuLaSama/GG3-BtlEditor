-- 建筑的字段布局与类型由事件对象负责。
editor.resolve("building",function(ctx) return game.events.index_at_cell(ctx.cell_index,"building",true) end)
local function write(event,input,place)
  event.building={flag=input.flag or 0,type_id=input.type or 0,extra=input.extra or 0,
    owner=input.owner or 0,dx=input.dx or 0,dy=input.dy or 0}
  if place then event:prepare_building() end
  if not place or input.field6~=nil then event.building.parameter_6=input.field6 end
end
editor.action("apply_building",function(ctx)
  local event=game.events.get(ctx.index); if event then write(event,ctx.input,false) end
end)
editor.action("place_building",function(ctx)
  local event=game.events.create {cell_index=ctx.cell_index or ctx.input.cell or 0}
  write(event,ctx.input,true)
end)
editor.action("delete_event",function(ctx) game.events.remove(ctx.index) end)
