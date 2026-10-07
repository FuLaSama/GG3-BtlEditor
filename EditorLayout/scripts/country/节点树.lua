-- 节点路径由行为树对象解释；业务脚本不操作 JSON 句柄。
editor.action("add_root",function(ctx)
  game.behavior_trees.get(ctx.index):add_child({class="seq"},true)
end)
editor.action("add_child",function(ctx)
  game.behavior_trees.get(ctx.index):node(ctx.input.path):add_child {class="act"}
end)
editor.action("delete_child",function(ctx)
  game.behavior_trees.get(ctx.index):node(ctx.input.path):remove_child(ctx.input.index)
end)
