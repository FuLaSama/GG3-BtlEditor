-- 复制完整事件，以便保留建筑/工事的未知参数。
editor.action("copy_landmarks",function(ctx)
  local cell=ctx.cell_index or 0
  game.events.copy(game.events.index_at_cell(cell,"building"),"bldg")
  game.events.copy(game.events.index_at_cell(cell,"fort"),"fort")
end)
editor.action("paste_landmarks",function(ctx)
  local cell=ctx.cell_index or 0; game.events.remove_landmarks(cell)
  for _,key in ipairs({"bldg","fort"}) do
    if editor.clipboard.has(key) then game.events.paste(key).cell_index=cell end
  end
end)
