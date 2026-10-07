-- 完整复制保留未知字段，粘贴时分配新身份并替换目标格子的部队。
editor.action("copy_unit",function(ctx)
  game.units.copy(game.units.index_at_cell(ctx.cell_index or 0),"unit")
end)
editor.action("paste_unit",function(ctx)
  local cell=ctx.cell_index or 0; local id=game.units.next_id()
  game.units.remove_at_cell(cell)
  if editor.clipboard.has("unit") then
    local unit=game.units.paste("unit"); unit.cell_index=cell; unit:set_id(id)
  end
end)
