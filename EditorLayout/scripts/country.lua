-- 国家行为树。键按写入顺序保留。空字符串和空整数列表删掉键。
-- 不把 rle_metadata 当成普通数组去 insert / remove。

local BT = "Root.rle_metadata"

local function trim(value)
  if value == nil then return "" end
  return (value:match("^%s*(.-)%s*$"))
end

local function set_str(obj, key, value)
  local text = trim(value)
  if text == "" then editor.json_set(obj, key, nil)
  else editor.json_set(obj, key, text) end
end

local function set_int_list(obj, key, list)
  if list == nil or #list == 0 then editor.json_set(obj, key, nil)
  else editor.json_set(obj, key, list) end
end

local function walk(tree, path)
  local cur = tree
  if path == nil then return cur end
  for i = 1, #path do
    cur = editor.json_at(cur, "node", path[i])
  end
  return cur
end

editor.action("add_tree", function(ctx)
  local n = editor.json_vec_count(BT)
  local next_id = 1
  for i = 0, n - 1 do
    local id = editor.json_get(editor.json_vec_at(BT, i), "btid")
    if id == nil then id = 0 end
    if id >= next_id then next_id = id + 1 end
  end
  local tree = editor.json_vec_append(BT)
  editor.json_set(tree, "btid", next_id)
  editor.json_set(tree, "name", "新行为树")
  editor.json_set(tree, "agent", "CBTCountryAgent")
  editor.json_set(tree, "class", "bt")
  editor.json_set(tree, "node", {})
end)

editor.action("delete_tree", function(ctx)
  if ctx.index == nil then return end
  editor.json_vec_remove(BT, ctx.index)
end)

editor.action("add_root", function(ctx)
  local tree = editor.json_vec_at(BT, ctx.index)
  local node = editor.json_insert(tree, "node", editor.json_count(tree, "node"))
  editor.json_set(node, "class", "seq")
  editor.json_set(node, "node", {})
end)

editor.action("add_child", function(ctx)
  local parent = walk(editor.json_vec_at(BT, ctx.index), ctx.input.path)
  local node = editor.json_insert(parent, "node", editor.json_count(parent, "node"))
  editor.json_set(node, "class", "act")
end)

editor.action("delete_child", function(ctx)
  local parent = walk(editor.json_vec_at(BT, ctx.index), ctx.input.path)
  editor.json_remove(parent, "node", ctx.input.index)
end)

editor.action("save_bt", function(ctx)
  local tree = editor.json_vec_at(BT, ctx.index)
  local input = ctx.input
  editor.json_set(tree, "btid", input.btid)
  editor.json_set(tree, "id", input.id)
  set_str(tree, "name", input.name)
  set_str(tree, "agent", input.agent)
  set_str(tree, "class", input.class)
  if input.path ~= nil then
    local node = walk(tree, input.path)
    editor.json_set(node, "id", input.node_id)
    set_str(node, "class", input.node_class)
    set_str(node, "method", input.method)
    set_int_list(node, "params", input.params)
    set_int_list(node, "rounds", input.rounds)
    editor.json_set(node, "result", input.result)
    editor.json_set(node, "count", input.count)
  end
end)
