-- 国家行为树与节点共用具名 JSON 对象；未知键保持原样。
local json, bridge = editor.json, editor.internal
local scalar_keys={btid=true,id=true,name=true,agent=true,class=true,method=true,result=true,count=true}
local lists={params=true,rounds=true}
local wrap
wrap=function(handle)
  local object, methods = {}, {}
  function methods.update(_,values) for key,value in pairs(values) do object[key]=value end end
  function methods.node(_,path)
    local current=handle
    for _,index in ipairs(path or {}) do current=json.at(current,"node",index) end
    return wrap(current)
  end
  function methods.add_child(_,values,with_children)
    local child=json.insert(handle,"node",json.count(handle,"node"))
    if with_children then json.set(child,"node",json.array()) end
    local result=wrap(child); if values then result:update(values) end; return result
  end
  function methods.remove_child(_,index) json.remove(handle,"node",index) end
  return setmetatable(object,{
    __index=function(_,key)
      bridge.check_context(handle)
      if methods[key] then return methods[key] end
      if key=="child_count" then return json.count(handle,"node") end
      if lists[key] then
        local values=json.get(handle,key); if values==nil then return nil end
        local result={}; for i=0,json.count(values)-1 do result[#result+1]=json.at(values,i) end
        return result
      end
      assert(scalar_keys[key],"行为树没有属性「"..tostring(key).."」")
      return json.get(handle,key)
    end,
    __newindex=function(_,key,value)
      bridge.check_context(handle)
      assert(scalar_keys[key] or lists[key],"行为树没有属性「"..tostring(key).."」")
      json.set(handle,key,value)
    end,__metatable="game behavior tree",
  })
end
game.behavior_trees={}
function game.behavior_trees.count() return json.vec_count("/10") end
function game.behavior_trees.get(index) return wrap(json.vec_at("/10",index)) end
function game.behavior_trees.create()
  local next_id=1
  for i=0,game.behavior_trees.count()-1 do next_id=math.max(next_id,(game.behavior_trees.get(i).btid or 0)+1) end
  local handle=json.vec_append("/10")
  json.set(handle,"node",json.array())
  local tree=wrap(handle)
  tree:update {btid=next_id,name="新行为树",agent="CBTCountryAgent",class="bt"}
  return tree
end
function game.behavior_trees.remove(index) if index~=nil then json.vec_remove("/10",index) end end
