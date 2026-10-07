# 将荣3 战役地图编辑器

本工具用于编辑《将军的荣耀3》的 `.btl` 格式文件。

## 能做什么

- 打开游戏关卡 `.btl`，或按字段名 / 字段 ID 写的 JSON
- 修改地形、部队、关卡目标等数据
- 导出三种结果：
  - `.btl`（给游戏用）
  - JSON（`battle.fbs` 里的字段名）
  - JSON（原始字段 ID）
- 附带只读分析工具 **BtlVerifier**

需要本机已安装 [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)。

```text
dotnet build BtldMapEditor.sln -c Release
```

## 仓库结构

| 路径 | 内容 |
|------|------|
| `src/BtldMapEditor` | 地图编辑器（WinForms） |
| `src/BtlCore` | `.btl` 读写与 JSON 导入导出 |
| `src/BtlVerifier` | 独立校验 / 反编译 GUI |
| `schema/battle.fbs` | 关卡字段定义 |
| `EditorLayout/操作手册.md` | 统一操作手册、完整接口参考、工具示例与 AI 编写规范 |
| `EditorLayout/脚本编写.md` | 用 Lua 和布局 XML 自己写修改 |
| `EditorLayout/写个小工具.md` | Lua 工具入门、具名对象和参数声明 |
| `EditorLayout/对象接口迁移与试验.md` | 现有脚本迁移、对象集合和样例关卡验证 |
| `tests/Smoke` | 往返自检 |
| `tools/Dump` | 命令行：`.btl` → 带字段名的 JSON |

更详细的说明见 [`src/BtlCore/README.md`](src/BtlCore/README.md)、[`src/BtldMapEditor/README.md`](src/BtldMapEditor/README.md)。
