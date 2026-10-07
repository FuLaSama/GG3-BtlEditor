-- 特种配置：具名属性，未启用时删除整份配置。
function write_unit_ex(index,input)
  local unit=game.units.get(index)
  if not input.ex then unit.special=nil; return end
  unit.special={id=input.ex_id or 0}
  local special=unit.special
  special.parameter_1=input.ex_1; special.hp=input.ex_hp; special.max_hp=input.ex_max_hp
  special.parameter_4=input.ex_4; special.parameter_5=input.ex_5
end
