local model,bridge=editor.modules.objects,editor.internal
local scalar=model.scalar
game.stage=model.singleton("关卡",{
  round_limit=scalar("/2/1","u16"),
  fog={get=function(a) local value=bridge.get(a,"/1/4"); return not (value==true or value==1) end,
    set=function(a,value)
      if type(value)~="boolean" then model.fail("fog 需要 true 或 false") end
      bridge.set(a,"/1/4","bool",not value)
    end},
})
game.targets=model.collection {path="/2/0",label="关卡目标",fields={
  type=scalar("/0","u16"),value=scalar("/1","i16"),param1=scalar("/2","u16"),param2=scalar("/3","u16"),flag=scalar("/4","u8"),
}}
game.reinforcements=model.collection {path="/3/1",label="增援点",fields={
  cell=scalar("/0","i16"),faction=scalar("/1","u8"),is_key=scalar("/2","bool"),flag=scalar("/3","u8"),
}}
game.weather=model.collection {path="/9/0",label="天气",fields={
  type=scalar("/0","u8"),start=scalar("/1","u16"),duration=scalar("/2","u16"),
}}
game.routes=model.collection {path="/6/1",label="路线",fields={flag=scalar("/0","u16"),cells=model.array("/1","u16")}}
