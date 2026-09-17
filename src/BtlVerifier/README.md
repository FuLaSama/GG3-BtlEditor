# BtlVerifier

桌面端 **BTL 校验与结构分析器**（WinForms / .NET 8）。  
面向逆向 / 调试：只读加载、物理地址追踪、IDA 风格反汇编视图、六角图对照，以及模拟引擎加载时的崩溃检测。

**不引用 `BtlCore`**：自带一套独立 `Core`（模型、Schema、反编译、桥接）。与地图编辑器 / 转换工具链是平行实现，不是同一套往返编译路径。

相关文档：[`../BtlCore`](../BtlCore/README.md)、[`../BtldMapEditor`](../BtldMapEditor/README.md)、[`../BtlToolchain`](../BtlToolchain/README.md)。

---

## 定位对比

| 项目 | 目的 | 数据栈 |
|------|------|--------|
| **BtlVerifier**（本目录） | 分析、校验、物理寻址、模拟闪退 | 自有 `Core/*`（只读为主） |
| **BtldMapEditor** | 可视化编辑并写回 BTL | `BtlCore`（可往返） |
| **BtlToolchain** GUI | 格式互转 | `BtlCore.Toolchain` |

```
BTL 字节
  → FlatBufferDecompiler（本项目）
  → 字典树 + 物理偏移
  → BtlBridge → StageModel（本项目模型）+ DisasmItem[]
  → MainForm（IDA 视图 / 六角图 / 战役面板）
  → BtlSimLoader（崩溃规则扫描）
```

---

## 目录结构

```
BtlVerifier/
├── Program.cs              # GUI 入口 + CLI
├── Core/                   # 独立分析核心（不依赖 BtlCore）
│   ├── FlatBufferDecompiler.cs
│   ├── BtlBridge.cs
│   ├── BtlSchema.cs
│   ├── BtlModels.cs
│   ├── BtlSimLoader.cs
│   └── GameSettings.cs
└── UI/
    ├── MainForm.cs         # 主分析窗体
    └── HexMapCanvas.cs     # 只读六角格画布
```

| 路径 | 约行数 | 职责 |
|------|--------|------|
| `Program.cs` | ~260 | GUI；`--verify` / `--verify-all` / `--dump` / `--test` |
| `Core/FlatBufferDecompiler.cs` | ~650 | 只读 FlatBuffers 反编译；记录向量物理偏移 |
| `Core/BtlBridge.cs` | ~2.0k | 加载 Stage、格子↔地址映射、生成 IDA 风格 `DisasmItem` |
| `Core/BtlSchema.cs` | ~750 | 内置 / 动态 `.fbs`；字段名映射 |
| `Core/BtlModels.cs` | ~500 | 本工具用的 `StageModel`、`CellModel`、`DisasmItem` 等 |
| `Core/BtlSimLoader.cs` | ~300 | 模拟加载规则：Tiles/Attr/Agent/Trigger 等致命问题 |
| `Core/GameSettings.cs` | ~230 | 建筑/工事/兵种/国家等名称表 |
| `UI/MainForm.cs` | ~3.7k | 三 Tab 分析 UI + 调试辅助 |
| `UI/HexMapCanvas.cs` | ~840 | 只读六角图（图层过滤、缩放、选中） |

`BtlVerifier.csproj` **无** `ProjectReference`；纯 WinExe。

---

## UI 功能（`MainForm`）

默认无参数启动 GUI，并先 `GameSettings.LoadDatabases()`。

| Tab | 内容 |
|-----|------|
| **IDA 视图** | 等宽反汇编伪代码、段条、跳转引线；右键改值/追加/断点；F7/F8/F9/F2 等调试手感 |
| **六角图** | `HexMapCanvas` + 目录/详情；选中格子高亮对应 **Tiles / Attributes 物理地址** |
| **战役面板** | 势力、限制、目标、增援等分类树 + 详情 |

另有：空白区开辟、hex 编辑、一键跑 `BtlSimLoader` 等辅助。

---

## CLI

| 参数 | 作用 |
|------|------|
| （无） | 打开 `MainForm` |
| `--verify <file>` | 单文件：`LoadBtl` + `BtlSimLoader.Simulate`，打印日志 |
| `--verify-all` | 批量扫包体内 `stage*` / `conquest*` / `corps*`，写校验报告 |
| `--dump <file> [out]` | 导出 IDA 风格反汇编文本到文件 |
| `--test` | 批量解析势力元数据并汇总导出 |

批量命令里的游戏包体路径目前写死在 `Program.cs`（本机 RE 工作区路径），换机器需改代码或后续参数化。

---

## Core 要点

### `FlatBufferDecompiler`

- 校验 Table / VTable 边界与对齐，降低坏文件读崩风险。
- 产出字典树，并暴露如 Tiles、Attributes 等在文件中的**绝对起始偏移**（供地图 Tab 地址高亮）。

### `BtlBridge`

- `LoadBtl`：反编译 →（可选动态 Schema 归一化）→ `StageModel`；保留 `RawRoot`。
- 消费 Attributes 与格子的对应关系，把建筑/工事等挂到 hex。
- `GenerateDisasm`：带 XREF 的 `DisasmItem` 列表，驱动 IDA Tab 双向跳转。

### `BtlSimLoader`

模拟引擎加载时容易闪退的检查，例如：

- 版本、Tiles 数量与地图尺寸
- Attributes 槽位与标志位是否匹配（越界类）
- AgentId、堆叠、HP
- Trigger 的格子索引范围

输出分级日志：`INFO` / `WARN` / `ERROR` / `FATAL`；`ERROR`/`FATAL` 计为潜在崩溃。

### `BtlSchema` / `GameSettings`

- Schema：硬编码表或加载外部 `.fbs`（菜单可切换）。
- Settings：读 GameData 类 JSON，把 ID 译成中文名，辅助展示与校验。

---

## 与 BtlCore 的关系（重要）

| | BtlVerifier Core | BtlCore |
|--|------------------|--------|
| 目标 | 分析 + 校验 + 物理地址 | 编辑往返 + bit-perfect 编译 |
| 模型 | 自有 `BtlVerifier.StageModel`（含 `CellModel`、`DisasmItem`、`RawRoot`） | `BtldMapEditor.StageModel`（含 `CellItem`、AST 绑定等） |
| 写回 | 以分析/局部 hex 编辑为主，**不是**完整 Logical JSON 编译链 | `EditorBridge` / Assembler 完整写回 |
| 维护 | 与 BtlCore **平行演进**，字段/行为可能不一致 |

改 Schema 或加载逻辑时，两边可能都要改；不要假设 Verifier 的 `StageModel` 与编辑器可互换。

---

## 结构备忘

1. **独立栈**：便于放心做破坏性二进制实验，不拖累编辑器管线。
2. **代价**：模型 / Schema / GameSettings 与 BtlCore 有重复，长期易漂移。
3. **`MainForm` 偏大**（~3.7k）：IDA 视图与调试逻辑集中在此，和编辑器的 `MainEditorForm` 类似是上帝窗体。
4. 若未来统一工具链，较稳妥的方向是：Verifier **继续只读分析**，加载层逐步改为调用 BtlCore 反汇编 AST，但保留本项目的地址映射与 `BtlSimLoader`。

---

*文档用途：记录 Verifier 作为独立分析/校验前端的层次，并明确与 BtlCore / 编辑器的边界。*
