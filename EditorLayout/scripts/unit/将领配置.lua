-- 将领对象保留未知属性，取消启用仍删除整个配置。
function write_unit_general(index,input)
  local unit=game.units.get(index)
  if not input.general then unit.general=nil; return end
  unit.general={id=input.general_id or 0}
  local general=unit.general
  if input.general_active or general.active~=nil then general.active=input.general_active or false end
  if input.general_param2~=nil or general.parameter_2~=nil then general.parameter_2=input.general_param2 or 0 end
end
