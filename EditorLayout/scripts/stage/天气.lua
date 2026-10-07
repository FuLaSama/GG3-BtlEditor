-- 天气：旧页面的空输入仍按 0 保存。
local function write(weather,input)
  weather:update {type=input.type or 0,start=input.start or 0,duration=input.duration or 0}
end
editor.action("add_weather",function(ctx) write(game.weather.create(),ctx.input) end)
editor.action("update_weather",function(ctx)
  local weather=game.weather.get(ctx.index); if weather then write(weather,ctx.input) end
end)
editor.action("delete_weather",function(ctx) game.weather.remove(ctx.index) end)
