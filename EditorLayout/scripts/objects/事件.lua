local model,bridge,data=editor.modules.objects,editor.internal,editor.data
local named=model.named
local events
events=model.collection {
  path="/5/0",label="事件",
  fields={
    cell_index=named("event.cell_index"),parameter_1=named("event.parameter_1"),
    building=model.optional("building","建筑",{
      flag=named("building.flag"),type_id=named("building.type_id"),extra=named("building.extra"),
      owner=named("building.owner"),dx=named("building.dx"),dy=named("building.dy"),parameter_6=named("building.parameter_6"),
    },function(a) bridge.field_set(a,"building.data",true) end),
    fort=model.optional("fort","工事",{id=named("fort.id"),parameter_3=named("fort.parameter_3")}),
  },
  methods={
    prepare_building=function(event)
      local anchor=bridge.anchor(data.row("/5/0",event.index))
      local path=bridge.field_path("building")
      bridge.ensure(anchor,path.."/2"); bridge.ensure(anchor,path.."/4")
    end,
    -- 保留原扩图识别条件，通过目录属性取得位置；空配置组仍按脚本事件处理。
    is_building=function(event) local b=event.building; return b~=nil and b.flag~=nil end,
    is_fort=function(event) local f=event.fort; return f~=nil and (f.id~=nil or f.parameter_3~=nil) end,
  },
  api={
    index_at_cell=function(cell,kind,last)
      if kind=="building" then return bridge.index_at_cell("/5/0",bridge.field_path("event.cell_index"),cell,last,bridge.field_path("building.flag")) end
      if kind=="fort" then return bridge.index_at_cell("/5/0",bridge.field_path("event.cell_index"),cell,last,bridge.field_path("fort")) end
      if kind==nil then return bridge.index_at_cell("/5/0",bridge.field_path("event.cell_index"),cell,last) end
    end,
    remove_landmarks=function(cell)
      return events.remove_where(function(event) return event.cell_index==cell and (event.building~=nil or event.fort~=nil) end)
    end,
  },
}
game.events=events
