-- 每个声明都会自动出现在「脚本工具」菜单，不需要改 XML。
editor.tool {
  id="heal_selected", title="恢复生命", target="selected_unit",
  description="恢复到该部队的最大生命，保留兵种、经验、将领等其他设置。",
  run=function(unit) unit:heal() end,
}

editor.tool {
  id="heal_all", title="恢复生命", target="all_units",
  run=function(unit) unit:heal() end,
}

editor.tool {
  id="faction_level", title="设置经验", target="faction_units",
  params={{id="level", label="经验等级", type="integer", default=10, min=0, max=255}},
  description="只修改指定势力部队的经验，保留堆叠编制。",
  run=function(unit, params) unit.level=params.level end,
}

editor.tool {
  id="add_gold", title="调整金币", target="selected_faction",
  params={{id="amount", label="增加金币（负数表示扣除）", type="integer", default=100, min=-1000000, max=1000000}},
  run=function(faction, params) faction.gold=(faction.gold or 0)+params.amount end,
}

editor.tool {
  id="cell_sea", title="设为海洋", target="selected_cell",
  description="只改变海洋标记，保留地质、变种和所有贴图层。",
  run=function(cell) cell.sea=true end,
}

editor.tool {
  id="cell_climate", title="设置地质", target="selected_cell",
  params={{id="climate", label="地质", type="choice", default=0, choices={
    {label="土地", value=0}, {label="草地", value=1}, {label="沙漠", value=2},
    {label="雪地", value=3}, {label="第五套", value=4},
  }}},
  run=function(cell, params) cell.climate=params.climate end,
}

editor.tool {
  id="offset_main", title="设置主地形偏移", target="selected_cell",
  params={
    {id="dx", label="横向偏移", type="integer", default=0, min=-128, max=127},
    {id="dy", label="纵向偏移", type="integer", default=0, min=-128, max=127},
  },
  run=function(cell, params)
    if not cell.main.present then error("请先在这个格子添加主地形") end
    cell.main:update {dx=params.dx, dy=params.dy}
  end,
}
