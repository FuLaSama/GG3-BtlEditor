-- 旧画布剪贴板转换集中到公共库；保留未涉及的格子和属性尾部。
editor.action("paste_terrain",function(ctx)
  if ctx.cell_index~=nil then game.map.paste_terrain(ctx.cell_index,ctx.input) end
end)
