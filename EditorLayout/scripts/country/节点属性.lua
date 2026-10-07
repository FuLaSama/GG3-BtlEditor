local function text(value)
  local result=(value or ""):match("^%s*(.-)%s*$"); if result~="" then return result end
end
function write_bt_node(tree,input)
  if input.path==nil then return end
  local node=tree:node(input.path)
  node.id=input.node_id; node.class=text(input.node_class); node.method=text(input.method)
  if input.params and #input.params>0 then node.params=input.params else node.params=nil end
  if input.rounds and #input.rounds>0 then node.rounds=input.rounds else node.rounds=nil end
  node.result=input.result; node.count=input.count
end
