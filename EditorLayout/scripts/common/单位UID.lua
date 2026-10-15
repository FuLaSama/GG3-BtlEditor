-- UID 及其已知引用集中在 Lua 中维护；研究出新引用时，在 references 中补充即可。
local data=editor.data
local function rows(path,visit)
  for index=0,data.count(path)-1 do visit(data.row(path,index)) end
end
local function references(visit)
  rows("/6/0",function(row) visit(row.."/3/5") end) -- 部队 AI 目标
  rows("/5/0",function(row) visit(row.."/3/2/5") end) -- 建筑招募 AI 目标（研究推定）
  for _,path in ipairs({"/6/1","/6/3"}) do -- 部队组、战略驻军成员
    rows(path,function(row) rows(row.."/1",visit) end)
  end
  for _,path in ipairs({"/8/0","/8/1"}) do -- 按格/按势力分组的已有部队增援队列
    rows(path,function(row) rows(row.."/1",function(entry) visit(entry.."/1") end) end)
  end
end

function game.units.assign_uids_by_cell()
  local total=(game.map.width or 0)*(game.map.height or 0)
  if total<=0 then error("关卡没有有效地图") end
  local onmap,offmap,all,owners={},{},{},{}
  for unit in game.units.each() do
    local cell,old=unit.cell_index,unit.agent_id
    if cell==nil or old==nil then error("部队列表第 "..(unit.index+1).." 行缺少部队信息，无法分配 UID") end
    local entry={unit=unit,cell=cell,old=old,index=unit.index}
    all[#all+1]=entry
    if cell>=0 and cell<total then onmap[#onmap+1]=entry else offmap[#offmap+1]=entry end
    owners[old]=(owners[old] or 0)+1
  end
  if #onmap==0 then error("地图内没有可分配 UID 的单位") end
  -- UID 为 u16，还需为「最大 UID + 1」的分配游标保留空间。
  if #all>65534 then error("单位数量超过 65534，无法同时保存唯一 UID 和下一个 UID") end
  table.sort(onmap,function(a,b)
    if a.cell~=b.cell then return a.cell<b.cell end
    return a.index<b.index
  end)
  local used,remap={},{}
  for id,entry in ipairs(onmap) do entry.new=id; used[id]=true end
  -- 先保留地图外合法且不冲突的编号，再为冲突项寻找空位，避免挤占其他增援的编号。
  for _,entry in ipairs(offmap) do
    if entry.old>0 and entry.old<65535 and not used[entry.old] then
      entry.new=entry.old; used[entry.new]=true
    end
  end
  local next_id=#onmap+1
  for _,entry in ipairs(offmap) do
    if not entry.new then
      while used[next_id] do next_id=next_id+1 end
      entry.new=next_id; used[next_id]=true
    end
  end
  local maximum=0
  for _,entry in ipairs(all) do
    maximum=math.max(maximum,entry.new)
    if entry.old>0 and owners[entry.old]==1 then remap[entry.old]=entry.new end
  end
  local updates={}
  references(function(path)
    local old=data.get(path)
    -- 0 是未指定目标；缺省字段也不创建。重号但被引用时，无法判断指向哪一行。
    if old and old>0 then
      if owners[old] and owners[old]>1 then
        error("UID "..old.." 被多个单位共用，且在 "..path.." 中被引用；请先消除引用歧义")
      end
      if remap[old] and remap[old]~=old then updates[#updates+1]={path=path,value=remap[old]} end
    end
  end)
  -- 所有校验及映射完成后再写入，交换/循环编号不会使引用被重复映射。
  for _,entry in ipairs(all) do if entry.old~=entry.new then entry.unit:set_id(entry.new) end end
  for _,update in ipairs(updates) do data.set(update.path,"u16",update.value) end
  if data.get("/6/2")~=maximum+1 then data.set("/6/2","u16",maximum+1) end
  return #onmap
end
