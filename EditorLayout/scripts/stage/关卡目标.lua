-- 关卡目标：使用具名集合和对象，空值仍删除对应字段。
local function write(target,input)
  target.type=input.type; target.value=input.value; target.param1=input.param1
  target.param2=input.param2; target.flag=input.flag
end
editor.action("add_target",function(ctx) write(game.targets.create(),ctx.input) end)
editor.action("update_target",function(ctx)
  local target=game.targets.get(ctx.index); if target then write(target,ctx.input) end
end)
editor.action("delete_target",function(ctx) game.targets.remove(ctx.index) end)
