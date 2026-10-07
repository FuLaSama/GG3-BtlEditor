local function text(value)
  local result=(value or ""):match("^%s*(.-)%s*$"); if result~="" then return result end
end
editor.action("save_bt",function(ctx)
  local tree=game.behavior_trees.get(ctx.index); local input=ctx.input
  tree.btid=input.btid; tree.id=input.id; tree.name=text(input.name)
  tree.agent=text(input.agent); tree.class=text(input.class)
  write_bt_node(tree,input)
end)
