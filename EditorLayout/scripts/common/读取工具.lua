-- 保留通用路径工具，供自定义底层脚本使用；正式页面使用 game 对象。
local paths={row=editor.data.row}
function paths.field(parent,id)
  assert(type(id)=="number" and id>=0 and id%1==0,"字段号必须是非负整数")
  return parent.."/"..id
end
function paths.member(parent,index)
  assert(type(index)=="number" and index>=0 and index%1==0,"成员下标必须是非负整数")
  return parent.."."..index
end
editor.modules.paths=paths
