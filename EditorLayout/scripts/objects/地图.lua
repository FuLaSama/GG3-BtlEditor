local model,bridge=editor.modules.objects,editor.internal
local size_layout={"u16","u16","u16","u16","u16","u16"}
local layer_names={"main","secondary","decor"}
local fields={
  width=model.scalar("/1/0.0","u16",true),height=model.scalar("/1/0.1","u16",true),
  left_margin=model.scalar("/1/0.2","u16",false,size_layout),top_margin=model.scalar("/1/0.3","u16",false,size_layout),
  play_width=model.scalar("/1/0.4","u16",false,size_layout),play_height=model.scalar("/1/0.5","u16",false,size_layout),
}
local function cell(index)
  local width,height=game.map.width or 0,game.map.height or 0
  model.integer(index,math.max(0,width*height-1),"格子下标")
  if width<=0 or height<=0 then model.fail("关卡没有有效地图") end
  local anchor=editor.root()
  local properties={}
  for _,property in ipairs({"climate","variant","sea","playable"}) do
    local key=property
    properties[key]={get=function() return bridge.cell_get(anchor,index,key) end,
      set=function(_,value) bridge.cell_set(anchor,index,key,value) end}
  end
  for key,value in pairs({index=index,x=index%width,y=math.floor(index/width)}) do
    local fixed=value
    properties[key]={get=function() return fixed end,set=function() model.fail("格子坐标只读") end}
  end
  for _,name in ipairs(layer_names) do
    local layer=name
    local layer_fields={}
    for _,property in ipairs({"id","variant","dx","dy","present"}) do
      local key=property
      layer_fields[key]={get=function() return bridge.cell_get(anchor,index,key,layer) end,
        set=function(_,value) bridge.cell_set(anchor,index,key,value,layer) end}
    end
    properties[layer]={
      get=function() return model.proxy(anchor,"图层",layer_fields) end,
      set=function(_,values)
        if values==nil then bridge.cell_set(anchor,index,"present",false,layer)
        elseif type(values)=="table" then model.update(model.proxy(anchor,"图层",layer_fields),values)
        else model.fail("图层需要属性表或 nil") end
      end,
    }
  end
  return model.proxy(anchor,"格子",properties)
end
local methods={
  is_layer=function(name)
    for _,layer in ipairs(layer_names) do if name==layer then return true end end
    return false
  end,
  cell_at_index=cell,
  cell=function(x,y)
    model.integer(x,math.max(0,(game.map.width or 0)-1),"横坐标")
    model.integer(y,math.max(0,(game.map.height or 0)-1),"纵坐标")
    return cell(y*game.map.width+x)
  end,
  try_cell_at_index=function(index)
    if type(index)~="number" or index<0 or index%1~=0 or index>=(game.map.width or 0)*(game.map.height or 0) then return nil end
    return cell(index)
  end,
  cells=function()
    local token=editor.root()
    local index,count=-1,(game.map.width or 0)*(game.map.height or 0)
    return function() bridge.check_context(token); index=index+1; if index<count then return cell(index) end end
  end,
  paste_terrain=function(index,source)
    bridge.cell_paste(editor.root(),index,source.terrain,source.decor,source.main,source.secondary)
  end,
  resize=editor.modules.map_resize,
}
game.map=model.singleton("地图",fields,methods)
