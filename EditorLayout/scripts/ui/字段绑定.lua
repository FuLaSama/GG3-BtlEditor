-- XML 字段绑定只访问具名对象；读取缓存只保存普通值。
local function selected(ctx)
  local width,height=game.map.width or 0,game.map.height or 0
  if ctx.cell_index==nil or ctx.cell_index<0 or ctx.cell_index>=width*height then return nil end
  return game.map.cell_at_index(ctx.cell_index)
end
editor.resolve("cell",function(ctx) return selected(ctx) and ctx.cell_index or nil end)
local function object_action(name,object,property)
  editor.action(name,{
    get=function() return object[property] end,
    set=function(ctx) object[property]=ctx.value end,
  })
end
object_action("ui_fog",game.stage,"fog")
for _,property in ipairs({"left_margin","top_margin","play_width","play_height"}) do
  object_action("ui_"..property,game.map,property)
end
for _,property in ipairs({"playable","sea"}) do
  local key=property
  editor.action("ui_"..key,{
    get=function(ctx) local cell=selected(ctx); if cell then return cell[key] end end,
    set=function(ctx) local cell=selected(ctx); if cell then cell[key]=ctx.value end end,
  })
end
local function attributes(ctx)
  local cell=selected(ctx); if not cell then return {} end
  local cache_key="game.ui.attributes."..ctx.cell_index
  if ctx.cache[cache_key] then return ctx.cache[cache_key] end
  local values={}
  for _,name in ipairs({"main","secondary","decor"}) do
    local layer=cell[name]; local entry={}
    if layer.present then
      for _,property in ipairs({"id","variant","dx","dy"}) do entry[property]=layer[property] end
    end
    values[name]=entry
  end
  ctx.cache[cache_key]=values
  return values
end
for _,name in ipairs({"main","secondary","decor"}) do
  for _,property in ipairs({"id","variant","dx","dy"}) do
    local layer,key=name,property
    editor.action("ui_"..layer.."_"..key,{
      get=function(ctx) return (attributes(ctx)[layer] or {})[key] end,
      set=function(ctx)
        local cell=selected(ctx); if cell and cell[layer].present then cell[layer][key]=ctx.value or 0 end
      end,
    })
  end
end
editor.action("move_faction_up",function(ctx)
  if ctx.index~=nil and ctx.index>0 then game.factions.move(ctx.index,ctx.index-1) end
end)
editor.action("move_faction_down",function(ctx)
  if ctx.index~=nil and ctx.index<game.factions.count()-1 then game.factions.move(ctx.index,ctx.index+1) end
end)
