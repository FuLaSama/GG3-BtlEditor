-- 路线对象自动处理格子数组的类型。
local function write(route,input) route:update {flag=input.flag or 1,cells=input.cells or {}} end
editor.action("add_route",function(ctx) write(game.routes.create(),ctx.input) end)
editor.action("set_route",function(ctx)
  local route=game.routes.get(ctx.index); if route then write(route,ctx.input) end
end)
editor.action("delete_route",function(ctx) game.routes.remove(ctx.index) end)
