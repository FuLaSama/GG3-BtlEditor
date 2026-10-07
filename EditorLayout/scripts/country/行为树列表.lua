editor.action("add_tree",function() game.behavior_trees.create() end)
editor.action("delete_tree",function(ctx) game.behavior_trees.remove(ctx.index) end)
