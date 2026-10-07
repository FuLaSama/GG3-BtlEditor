terrain_layers=terrain_layers or {}
terrain_layers.main={name="main"}
editor.action("apply_layer",function(ctx)
  if not game.map.try_cell_at_index(ctx.cell_index) then return end
  local item=ctx.item or {}; local layer=item.layer or (ctx.input or {}).layer
  assert(terrain_layers[layer],"未知地形层")
  game.map.cell_at_index(ctx.cell_index)[layer]={id=item.terrain_id or 0,variant=item.variant or 0,dx=item.dx or 0,dy=item.dy or 0}
end)
editor.action("clear_layer",function(ctx)
  if not game.map.try_cell_at_index(ctx.cell_index) then return end
  local cell=game.map.cell_at_index(ctx.cell_index); local layer=ctx.input.layer
  if layer=="sea" then cell.sea=false else assert(terrain_layers[layer],"未知地形层"); cell[layer]=nil end
end)
editor.action("set_offset",function(ctx)
  if not game.map.try_cell_at_index(ctx.cell_index) then return end
  local layer=ctx.input.layer; assert(terrain_layers[layer],"未知地形层")
  local attr=game.map.cell_at_index(ctx.cell_index)[layer]
  if attr.present and attr.id~=0 then attr:update {dx=ctx.input.dx or 0,dy=ctx.input.dy or 0} end
end)
