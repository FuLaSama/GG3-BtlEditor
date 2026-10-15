local function text(value)
  local result=(value or ""):match("^%s*(.-)%s*$"); if result~="" then return result end
end
local function write_node(tree,input)
  if input.path==nil then return end
  local node=tree:node(input.path)
  node.id=input.node_id; node.class=text(input.node_class); node.method=text(input.method)
  node.params=input.params -- nil 为未写，{} 为明确的空参数数组。
  node.rounds=input.rounds
  node.result=input.result; node.count=input.count
end
editor.modules.bt_forms={write_node=write_node}
