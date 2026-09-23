-- 全局配置：总回合、目标、天气、增援。
-- 目标、增援的空值删槽。天气的空值写成 0，与现在的按钮一致。

local function zero(v)
  if v == nil then return 0 end
  return v
end

local function key_flag(v)
  if v == nil then return false end
  return v
end

editor.action("set_round_limit", {
  get = function(ctx)
    return editor.get("Root.stage_metadata.round_limit")
  end,
  set = function(ctx)
    editor.ensure("Root.stage_metadata")
    editor.set("Root.stage_metadata.round_limit", ctx.value)
  end
})

local function write_target(row, input)
  editor.set(row, "target_type", input.type)
  editor.set(row, "target_value", input.value)
  editor.set(row, "param1", input.param1)
  editor.set(row, "param2", input.param2)
  editor.set(row, "unk", input.flag)
end

editor.action("add_target", function(ctx)
  editor.ensure("Root.stage_metadata.targets")
  local row = editor.append("Root.stage_metadata.targets")
  write_target(row, ctx.input)
end)

editor.action("update_target", function(ctx)
  write_target(ctx.object, ctx.input)
end)

editor.action("delete_target", function(ctx)
  editor.remove("Root.stage_metadata.targets", ctx.index)
end)

local function write_weather(row, input)
  editor.set(row, "decal_type", zero(input.type))
  editor.set(row, "x", zero(input.start))
  editor.set(row, "y", zero(input.duration))
end

editor.action("add_weather", function(ctx)
  editor.ensure("Root.decal_info.decals")
  local row = editor.append("Root.decal_info.decals")
  write_weather(row, ctx.input)
end)

editor.action("update_weather", function(ctx)
  write_weather(ctx.object, ctx.input)
end)

editor.action("delete_weather", function(ctx)
  editor.remove("Root.decal_info.decals", ctx.index)
end)

local function write_reinforce(row, input)
  editor.set(row, "cell_idx", input.cell)
  editor.set(row, "faction_id", input.faction)
  editor.set(row, "val1", key_flag(input.is_key))
  editor.set(row, "val2", input.flag)
end

editor.action("add_reinforce", function(ctx)
  editor.ensure("Root.battle_info.reinforce_points")
  local row = editor.append("Root.battle_info.reinforce_points")
  write_reinforce(row, ctx.input)
end)

editor.action("update_reinforce", function(ctx)
  write_reinforce(ctx.object, ctx.input)
end)

editor.action("delete_reinforce", function(ctx)
  editor.remove("Root.battle_info.reinforce_points", ctx.index)
end)
