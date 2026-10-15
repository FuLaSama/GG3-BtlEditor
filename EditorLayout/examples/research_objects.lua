-- 合成研究示例：新部队属性 + 新集合 + 工具。未登记到自带布局。
editor.format.schema {
  id="example.research_objects",
  definition=[[
    table AIAgent { research_rank:ushort (id:33); }
    table Root { research_records:[ResearchRecord] (id:35); }
    table ResearchRecord { owner:ushort (id:0); rank:ushort (id:1); }
  ]],
}
local objects=editor.modules.objects
---@class GameUnit
---@field research_rank integer?
---@field research_promote fun(self:GameUnit,delta:integer)
game.units.define_field("research_rank",objects.scalar("/33","u16"))
game.units.define_method("research_promote",function(unit,delta)
  unit.research_rank=(unit.research_rank or 0)+delta
end)

---@class ResearchRecord: GameObject
---@field owner integer
---@field rank integer
---@type GameCollection<ResearchRecord>
local records=objects.collection {
  path="/35",label="研究记录",
  fields={owner=objects.scalar("/0","u16"),rank=objects.scalar("/1","u16")},
}
game.research_records=records

editor.tool {
  id="example.research_promote",title="增加研究等级（示例）",target="stage",
  params={{id="delta",label="增加等级",type="integer",default=1,min=0,max=65535}},
  run=function(_,params)
    for unit in game.units.each() do
      unit:research_promote(params.delta)
      records.remove_where(function(record) return record.owner==unit.agent_id end)
      records.create {owner=unit.agent_id,rank=unit.research_rank}
    end
  end,
}
