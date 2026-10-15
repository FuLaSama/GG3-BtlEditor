-- 公共对象构造与扩展入口；这里不保存部队、势力等业务字段。
local bridge, data = editor.internal, editor.data
local model = {}
local function fail(message) error(message, 3) end
model.fail = fail
function model.integer(value, maximum, label)
  if type(value) ~= "number" or value ~= value or value < 0 or value > maximum or value % 1 ~= 0 then
    fail(label .. "需要 0 到 " .. maximum .. " 的整数")
  end
  return value
end
function model.scalar(path, kind, readonly, layout)
  return {get=function(a) return bridge.get(a,path) end, set=function(a,value)
    if readonly then fail("这个属性只读") end
    bridge.set(a,path,kind,value,layout)
  end}
end
function model.named(id, readonly)
  return {get=function(a) return bridge.field_get(a,id) end, set=function(a,value)
    if readonly then fail("这个属性只读") end
    bridge.field_set(a,id,value)
  end}
end
function model.packed(path, shift, default, label, layout)
  local mask = editor.lshift(255,shift)
  return {get=function(a)
    local word=bridge.get(a,path)
    if word~=nil then return editor.band(editor.rshift(word,shift),255) end
  end, set=function(a,value)
    model.integer(value,255,label or "属性")
    local word=bridge.get(a,path) or default or 0
    word=editor.bor(editor.band(word,editor.bxor(4294967295,mask)),editor.lshift(value,shift))
    bridge.set(a,path,shift>8 and "u32" or (default~=nil and "u32" or "u16"),word,layout)
  end}
end
function model.array(path,kind)
  return {get=function(a) if bridge.exists(a,path) then return bridge.array_get(a,path) end end,
    set=function(a,values)
      if values==nil then bridge.set(a,path,kind,nil) else bridge.array_set(a,path,kind,values) end
    end}
end
function model.update(object,values)
  if type(values)~="table" then fail("update 需要属性表，请使用 object:update {hp=10}") end
  for key,value in pairs(values) do object[key]=value end
end
function model.proxy(anchor,label,fields,methods)
  local object={}
  local defaults={update=model.update}
  function defaults.ensure(self,name,values)
    if not fields[name] or not fields[name].optional then fail("ensure 只用于可选配置："..tostring(name)) end
    if self[name]==nil then self[name]={} end
    local config=self[name]
    if values~=nil then config:update(values) end
    return config
  end
  function defaults.clear(self,name)
    if not fields[name] or not fields[name].optional then fail("clear 只用于可选配置："..tostring(name)) end
    self[name]=nil
  end
  return setmetatable(object,{
    __index=function(_,key)
      bridge.check_context(anchor)
      if fields[key] then return fields[key].get(anchor) end
      local method=methods and methods[key] or defaults[key]
      if method then return function(receiver,...)
        if receiver~=object then fail("请使用对象:"..tostring(key).."(...) 调用方法") end
        bridge.check_context(anchor)
        return method(receiver,...)
      end end
      fail(label.."没有属性「"..tostring(key).."」")
    end,
    __newindex=function(_,key,value)
      bridge.check_context(anchor)
      if not fields[key] then fail(label.."没有属性「"..tostring(key).."」") end
      local ok,message=pcall(fields[key].set,anchor,value)
      if not ok then fail(label.."."..tostring(key).."："..tostring(message)) end
    end,__metatable="game object",
  })
end
function model.optional(id,label,fields,prepare)
  return {optional=true,
    get=function(a) if bridge.field_get(a,id) then return model.proxy(a,label,fields) end end,
    set=function(a,values)
      if values==nil then bridge.field_set(a,id,false); return end
      if type(values)~="table" then fail(label.."需要属性表或 nil") end
      bridge.field_set(a,id,true)
      if prepare then prepare(a) end
      model.update(model.proxy(a,label,fields),values)
    end}
end

---构造表向量集合。methods 使用 function(self, ...)；api 中是集合的点调用函数。
function model.collection(options)
  bridge.check_registration()
  local path,label=assert(options.path),assert(options.label)
  assert(type(path)=="string" and path:sub(1,1)=="/","集合需要完整数字路径")
  assert(type(label)=="string" and label~="","集合需要名称")
  local allowed={path=true,label=true,fields=true,methods=true,api=true,prepare=true,initialize=true,
    after_create=true,before_remove=true,after_move=true,matches=true}
  for key in pairs(options) do if not allowed[key] then fail("未知集合选项："..tostring(key)) end end
  local fields,methods={},{}
  local reserved={index=true,update=true,ensure=true,clear=true}
  for key,value in pairs(options.fields or {}) do
    assert(type(key)=="string" and key~="" and not reserved[key],"属性名为空或保留名："..tostring(key))
    assert(type(value)=="table" and type(value.get)=="function" and type(value.set)=="function","属性描述需要 get 和 set")
    fields[key]=value
  end
  for key,value in pairs(options.methods or {}) do
    assert(type(key)=="string" and key~="" and not reserved[key] and not fields[key],"方法名为空、保留名或与属性冲突："..tostring(key))
    assert(type(value)=="function","对象方法需要函数")
    methods[key]=value
  end
  local result={}
  fields.index={get=function(a) return bridge.index(path,a) end,set=function() fail("行下标只读") end}
  function result.ensure() data.ensure_vector(path,"table") end
  function result.count() return data.count(path) end
  function result.get(index)
    if index==nil then return nil end
    local anchor=bridge.anchor(data.row(path,index))
    if anchor~=nil then return model.proxy(anchor,label,fields,methods) end
  end
  function result.create(values)
    result.ensure()
    local index=data.append(path)
    if options.prepare then options.prepare(bridge.anchor(data.row(path,index))) end
    local object=result.get(index)
    if options.initialize then options.initialize(object) end
    if values~=nil then object:update(values) end
    if options.after_create then options.after_create(object) end
    return object
  end
  function result.remove(index)
    if type(index)=="table" then index=index.index end
    local object=result.get(index)
    if object==nil then return end
    if options.before_remove then options.before_remove(object) end
    data.remove(path,object.index)
  end
  function result.move(from,to)
    if from==nil or to==nil or from==to then return end
    local object=result.get(from)
    data.move(path,from,to)
    if options.after_move then options.after_move(object,from,to) end
  end
  function result.each(filter)
    local token=editor.root()
    local index,count=-1,result.count()
    return function()
      bridge.check_context(token)
      while index+1<count do
        index=index+1
        local object=result.get(index)
        local matches=object~=nil
        if matches and filter~=nil then
          if type(filter)=="function" then matches=filter(object)
          elseif options.matches then matches=options.matches(object,filter)
          else fail("each 的筛选条件需要函数") end
        end
        if matches then return object end
      end
    end
  end
  function result.all(filter)
    local objects={}
    for object in result.each(filter) do objects[#objects+1]=object end
    return objects
  end
  function result.remove_where(predicate)
    assert(type(predicate)=="function","remove_where 需要筛选函数")
    local selected=result.all(predicate) -- 先筛选，再倒序删除，使用本集合的关联清理规则。
    for index=#selected,1,-1 do result.remove(selected[index]) end
    return #selected
  end
  function result.copy(index,key)
    if type(index)=="table" then index=index.index end
    if index==nil then editor.clipboard.clear(key) else editor.clipboard.copy(key,data.row(path,index)) end
  end
  function result.paste(key) return result.get(editor.clipboard.paste(path,key)) end
  local function available(name)
    if type(name)~="string" or name=="" or fields[name] or methods[name] or name=="update" or name=="ensure" or name=="clear" then
      fail("属性或方法名为空或已使用："..tostring(name))
    end
  end
  function result.define_field(name,descriptor)
    bridge.check_registration(); available(name)
    if type(descriptor)~="table" or type(descriptor.get)~="function" or type(descriptor.set)~="function" then
      fail("属性描述需要 get 和 set 函数")
    end
    fields[name]=descriptor
  end
  function result.define_method(name,method)
    bridge.check_registration(); available(name)
    assert(type(method)=="function","方法需要函数")
    methods[name]=method
  end
  for key,method in pairs(options.api or {}) do
    if result[key]~=nil then fail("集合方法名已使用："..tostring(key)) end
    result[key]=method
  end
  return result
end
function model.singleton(label,fields,methods)
  bridge.check_registration()
  return setmetatable({}, {
    __index=function(_,key)
      if methods and methods[key] then return methods[key] end
      if not fields[key] then fail(label.."没有属性「"..tostring(key).."」") end
      return fields[key].get(editor.root())
    end,
    __newindex=function(_,key,value) model.proxy(editor.root(),label,fields)[key]=value end,
    __metatable="game collection",
  })
end
editor.modules.objects=model
