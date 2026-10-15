-- game 入口与工具分发。每种对象在 objects/ 中定义一次。
local fail=editor.modules.objects.fail
game={}
function game._run_tool(run,target,ctx,params)
  local count=0
  local function visit(object)
    if object==nil then fail("没有选中对应对象，请先选择部队、势力或地图格子") end
    if run(object,params,ctx)~=false then count=count+1 end
  end
  if target=="selected_unit" then visit(game.units.selected(ctx))
  elseif target=="all_units" then for unit in game.units.each() do visit(unit) end
  elseif target=="faction_units" then for unit in game.units.each(params.faction) do visit(unit) end
  elseif target=="selected_faction" then visit(game.factions.selected(ctx))
  elseif target=="all_factions" then for faction in game.factions.each() do visit(faction) end
  elseif target=="selected_cell" then visit(game.map.try_cell_at_index(ctx.cell_index))
  elseif target=="all_cells" then for cell in game.map.cells() do visit(cell) end
  elseif target=="stage" then visit(game.stage)
  elseif target=="map" then visit(game.map)
  else fail("未知作用范围") end
  if count==0 then fail("没有符合条件的对象，或工具跳过了所有对象") end
  return count
end
