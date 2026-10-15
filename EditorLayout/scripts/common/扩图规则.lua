-- 扩图中的实体保留、删除和坐标重映射规则；紧凑地形存储交给公共实现。
local function n0(value) return value or 0 end
editor.modules.map_resize=function(options)
  options=options or {}
  local left,right,up,down=n0(options.left),n0(options.right),n0(options.up),n0(options.down)
  for _,value in ipairs({left,right,up,down}) do
    assert(type(value)=="number" and value==value and value%1==0,"扩图边界需要整数")
  end
  if left==0 and right==0 and up==0 and down==0 then return end
  local old_w,old_h=n0(game.map.width),n0(game.map.height)
  if old_w<=0 or old_h<=0 then return end
  local new_w,new_h=old_w+left+right,old_h+up+down
  if new_w<=0 or new_h<=0 or new_w>500 or new_h>500 then return end
  editor.internal.resize_terrain(left,up,new_w,new_h)
  local old_total,new_total=old_w*old_h,new_w*new_h
  local function remap(index) return editor.remap_cell(index,old_w,left,up,new_w,new_h) end

  game.units.ensure()
  local last_unit={}
  for unit in game.units.each() do
    local c=n0(unit.cell_index)
    if c>=0 and c<old_total then last_unit[c+1]=unit.index end
  end
  for index=game.units.count()-1,0,-1 do
    local unit=game.units.get(index)
    local c=n0(unit.cell_index)
    if c>=0 and c<old_total then
      local mapped=last_unit[c+1]==index and remap(c) or nil
      if mapped==nil then game.units.remove(unit) else unit.cell_index=mapped end
    elseif c>=0 and c<new_total then game.units.remove(unit) end
  end

  game.events.ensure()
  local last_building,last_fort={},{}
  for event in game.events.each() do
    local c=n0(event.cell_index)
    if c>=0 and c<old_total then
      if event:is_building() then last_building[c+1]=event.index end
      if event:is_fort() then last_fort[c+1]=event.index end
    end
  end
  for index=game.events.count()-1,0,-1 do
    local event=game.events.get(index)
    local c=n0(event.cell_index)
    if event:is_building() or event:is_fort() then
      local keep=c>=0 and c<old_total and (last_building[c+1]==index or last_fort[c+1]==index)
      local mapped=keep and remap(c) or nil
      if mapped==nil then game.events.remove(event) else event.cell_index=mapped end
    else
      local mapped=remap(c)
      if mapped~=nil then event.cell_index=mapped end
    end
  end
  for index=game.reinforcements.count()-1,0,-1 do
    local point=game.reinforcements.get(index)
    local mapped=remap(n0(point.cell))
    if mapped==nil then game.reinforcements.remove(point) else point.cell=mapped end
  end
end
