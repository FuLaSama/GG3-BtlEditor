-- 总回合：具名关卡对象。空值删槽。
editor.action("set_round_limit", {
  get=function() return game.stage.round_limit end,
  set=function(ctx) game.stage.round_limit=ctx.value end,
})
