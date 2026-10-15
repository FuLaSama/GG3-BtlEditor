editor.tool {
  id="assign_unit_uids", title="按地块顺序分配单位 UID", target="map",
  description="地图内单位按地块 ID 升序从 1 编号，同格按原列表顺序；冲突的地图外单位顺延，已知 UID 引用同步更新。可一步撤销。",
  confirm="即将重新分配地图内所有单位的 UID，并调整冲突的地图外单位编号。\n\nBTL 中有很多地方与 UID 有关。工具会同步目前已知的引用，但关卡脚本、行为树参数及尚未确认的字段中仍可能存在其他引用，改号后可能影响关卡逻辑。\n\n是否确认继续？（执行后可一步撤销）",
  run=function() game.units.assign_uids_by_cell() end,
}
