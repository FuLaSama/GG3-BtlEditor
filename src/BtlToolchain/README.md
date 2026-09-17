# BtlToolchain

桌面端 **BTL 格式转换工具**（WinForms / .NET 8）。  
本项目本身几乎无算法：只做拖拽 GUI，真正管线全部在 [`../../BtlCore.old`](../../BtlCore.old/) 的 `BtlToolchain.Toolchain` 门面里（旧管线；正式 `BtlCore` 是 BTL↔BtlFront）。

注意命名易混：

| 名称 | 是什么 |
|------|--------|
| **本目录 `BtlToolchain` 工程** | 独立可执行转换 GUI |
| **`BtlCore.old` 里的 `BtlToolchain` 命名空间** | 汇编/编译/JSON 翻译等类库实现 |

---

## 定位

- **做什么**：在 BTL / BTLA / BTLD JSON / Logical JSON 之间一键互转。
- **不做什么**：不编辑地图、不做崩溃校验、不维护第二套反编译器。
- **依赖**：仅 `ProjectReference` → `BtlCore.old`。

与仓库内其它工具的关系：

```
BtlToolchain（本 GUI） ──调用──► BtlCore.old.Toolchain
BtldMapEditor.old     ──调用──► BtlCore.old.EditorBridge → 同一套 Toolchain
BtldMapEditor（正式）──调用──► BtlCore（Front）+ BtlCore.old（Stage 模型）
BtlVerifier（分析校验）──独立──► 自有 Core
```

---

## 文件结构

源码极少，扁平两文件：

| 文件 | 约行数 | 职责 |
|------|--------|------|
| `Program.cs` | ~20 | 启动 WinForms，打开 `GuiForm` |
| `GuiForm.cs` | ~380 | 模式选择、日志、拖拽、异步调用 `Toolchain.*` |

无 CLI；无参数即开 GUI。

---

## 支持的转换模式

界面下拉框对应 `Toolchain` API：

| 模式 | 调用 |
|------|------|
| BTL → 逻辑 JSON | `BtlToLogicalJson` |
| 逻辑 JSON → BTL | `LogicalJsonToBtl` |
| BTL → BTLA | `Disassemble` |
| BTLA → BTL | `Assemble` |
| BTLA → BTLD JSON | `Decompile` |
| BTLD JSON → BTLA | `Compile` |
| BTLD JSON → 逻辑 JSON | `BtldToLogicalJsonFile` |
| 逻辑 JSON → BTLD JSON | `LogicalToBtldJsonFile` |

管线含义见 [BtlCore README](../BtlCore/README.md) 的「核心管线」一节。

---

## 使用方式

1. 启动程序，选择转换模式。
2. 将源文件**拖进窗口**（会按扩展名推断是否匹配当前模式，并生成同目录默认输出名）。
3. 或选好路径后点「开始转换」。
4. 日志区输出成功/失败信息；转换在后台 `Task` 中执行，避免卡 UI。

---

## 结构备忘

- 薄壳：适合当「管线调试器 / 批处理前的手工转换台」。
- 若要加新转换步骤，应改 `BtlCore`，本工程只加一行模式映射即可。
- 与 `BtldMapEditor` 的 CLI（`--export-json` 等）功能有重叠，但本工具面向**任意中间格式**互转，不只编辑器用的 Logical JSON ↔ BTL。

---

*文档用途：说明本工程仅为 BtlCore 工具链的 GUI 壳，避免与类库命名空间混淆。*
