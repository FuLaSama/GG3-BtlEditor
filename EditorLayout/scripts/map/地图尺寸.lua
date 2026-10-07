-- 地图尺寸变化与关联索引重映射由公共地图对象负责。
editor.action("resize_map",function(ctx) game.map.resize(ctx.input) end)
