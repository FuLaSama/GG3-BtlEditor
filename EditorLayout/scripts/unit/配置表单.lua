-- XML 整份表单保存的空值规则，独立于工具的局部更新。
local configs={
  behavior={enabled="behavior",fields={parameter_0="behavior_0",id="behavior_id",parameter_2="behavior_2",
    radius="behavior_radius",parameter_4="behavior_4",center_cell="behavior_center",parameter_6="behavior_6"}},
  general={enabled="general",fields={id="general_id",active="general_active",parameter_2="general_param2",medal="general_medal"}},
  special={enabled="ex",fields={id="ex_id",parameter_1="ex_1",hp="ex_hp",max_hp="ex_max_hp",parameter_4="ex_4",parameter_5="ex_5"}},
}
local forms={}
function forms.write_all(unit,input)
  for _,name in ipairs({"behavior","general","special"}) do
    local spec=configs[name]
    if not input[spec.enabled] then unit:clear(name)
    else
      local config=unit:ensure(name)
      for property,key in pairs(spec.fields) do
        local present=input.__fields and input.__fields[key]
        if present or input[key]~=nil or (input.__fields==nil and key~="general_medal") then
          config[property]=input[key]
        end
      end
    end
  end
end
editor.modules.unit_forms=forms
