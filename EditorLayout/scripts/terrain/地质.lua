-- 地质、变种和海洋标记分别赋值，自动保留其他标记及图层。
editor.action("apply_climate",function(ctx)
  if not game.map.try_cell_at_index(ctx.cell_index) then return end
  local cell=game.map.cell_at_index(ctx.cell_index); local item=ctx.item or {}
  if item.sea then cell.sea=true else
    if item.t~=nil then cell.climate=item.t end
    cell.variant=item.variant or 0
  end
end)
editor.action("set_sea",function(ctx)
  if ctx.cell_index~=nil then game.map.cell_at_index(ctx.cell_index).sea=ctx.value or false end
end)
