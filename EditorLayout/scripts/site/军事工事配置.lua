-- 工事是事件的可选具名子对象。
editor.resolve("fort",function(ctx) return game.events.index_at_cell(ctx.cell_index,"fort",true) end)
local function write(event,input)
  event.parameter_1=input.field1 or 0
  event.fort={id=input.fort_id or 0,parameter_3=input.field3 or 0}
end
editor.action("apply_fort",function(ctx)
  local event=game.events.get(ctx.index); if event then write(event,ctx.input) end
end)
editor.action("place_fort",function(ctx)
  write(game.events.create {cell_index=ctx.cell_index or ctx.input.cell or 0},ctx.input)
end)
editor.action("delete_event",function(ctx) game.events.remove(ctx.index) end)
