-- 合成格式示例，不代表已经确认的游戏字段。确认真实位置与格式后再安装。
-- 放入 EditorLayout.user/tools 后重新加载布局，并重新打开关卡。
-- 示范载荷：0x7B + u16 小端数值 + 不修改的其余字节，至少 8 字节。
editor.format.schema {
  id = "example.research_schema",
  definition = [[
    table Root { research_payload:[ubyte] (id:31); }
  ]],
}

editor.format.codec {
  id = "example.research_packet",
  revision = "1",
  decode = function(bytes)
    assert(#bytes >= 8 and bytes[1] == 123, "载荷不是这个示例支持的格式")
    return {value = editor.binary.read(bytes, 1, "u16"), bytes = bytes}
  end,
  encode = function(packet)
    editor.binary.write(packet.bytes, 1, "u16", packet.value)
    return packet.bytes -- 保留尚未理解的其他字节
  end,
}

editor.tool {
  id = "example.research_packet_value",
  title = "修改研究载荷数值（示例）",
  target = "stage",
  params = {{id = "value", label = "数值", type = "integer", default = 7, min = 0, max = 65535}},
  run = function(_, params)
    local packet = editor.format.read("example.research_packet", "/31")
    packet.value = params.value
    editor.format.write("example.research_packet", "/31", packet)
  end,
}
