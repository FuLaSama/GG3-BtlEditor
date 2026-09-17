# BtldMapEditor

战役地图编辑器。打开关卡按后缀识别，经 [`BtlCore`](../BtlCore/README.md) 转成内存 BtlFront 再编辑。

## 运行

仓库根打开 `BtldMapEditor.sln`，启动项目选 **BtldMapEditor**。输出：仓库根 `dist\`。

| 菜单 | 作用 |
|------|------|
| **新建关卡地图…** | 空白图 |
| **打开…** | `.btl` / `.json`（字段名或字段 ID） |
| **导出为…** | `.btl` / JSON 字段名 / JSON 原始字段 ID |

## 结构

| 路径 | 作用 |
|------|------|
| `Front/` | 会话、注册表、编辑器侧 Front JSON |
| 其余 `.cs` | 窗体 / 画布 / 贴图 |

依赖：同目录的 `BtlCore`（读写转换）。显示用名表和贴图来自仓库根 `GameData` / `地形贴图相关`（构建时拷到 `dist`）。
