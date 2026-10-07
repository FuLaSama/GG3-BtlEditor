-- 增援部署点：具名属性，未填写的非布尔字段删槽。
local function write(point,input)
  point.cell=input.cell; point.faction=input.faction; point.is_key=input.is_key or false; point.flag=input.flag
end
editor.action("add_reinforce",function(ctx) write(game.reinforcements.create(),ctx.input) end)
editor.action("update_reinforce",function(ctx)
  local point=game.reinforcements.get(ctx.index); if point then write(point,ctx.input) end
end)
editor.action("delete_reinforce",function(ctx) game.reinforcements.remove(ctx.index) end)
