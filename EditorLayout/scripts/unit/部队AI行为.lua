-- 已启用时局部写入行为，未启用时删除整份行为配置。
function write_unit_behavior(index,input)
  local unit=game.units.get(index)
  if not input.behavior then unit.behavior=nil; return end
  unit.behavior={}
  local behavior=unit.behavior
  behavior.parameter_0=input.behavior_0; behavior.id=input.behavior_id
  behavior.parameter_2=input.behavior_2; behavior.radius=input.behavior_radius
  behavior.parameter_4=input.behavior_4; behavior.center_cell=input.behavior_center; behavior.parameter_6=input.behavior_6
end
