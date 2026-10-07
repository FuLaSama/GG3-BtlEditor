-- 颜色、结构体、关系行的操作由公共对象层统一处理。
local function write(faction,input)
  faction:set_id(input.id or 0)
  faction:update {country=input.country or 0,camp=input.camp or 0,is_ai=input.is_ai~=nil and input.is_ai~=0 and input.is_ai~=false,
    parameter_5=input.val5 or 0,alignment_1=input.align1 or 0,gold=input.gold or 0,tech=input.tech or 0,
    income_multiplier=input.income or 1,damage_multiplier=input.damage or 1,hp_multiplier=input.hp or 1,
    alignment_2=input.align2 or 0,config_id=input.config_id or 0}
  if input.r~=nil or input.g~=nil or input.b~=nil or input.a~=nil then
    faction.color={r=input.r or 0,g=input.g or 0,b=input.b or 0,a=input.a or 255}
  end
  faction.general_flag=input.general_flag; faction.config_ref=input.config_ref
end
editor.action("apply_faction",function(ctx)
  local faction=game.factions.get(ctx.index); if faction then write(faction,ctx.input) end
end)
editor.action("add_faction",function() game.factions.create() end)
editor.action("delete_faction",function(ctx) game.factions.remove(ctx.index) end)
function move_faction_rows(from,to) game.factions.move(from,to) end
editor.action("move_faction",function(ctx) game.factions.move(ctx.input.from,ctx.input.to) end)
