# BtlCore

BTL（FlatBuffers）↔ BtlFront。从零实现；旧管线若恢复，会放在仓库根的 `BtlCore.old`。

正式 schema 在 [`../../schema/battle.fbs`](../../schema/battle.fbs)。

## 文档自己的格式解释与编辑会话

`BtlFrontDocument.Context` 固定读入时使用的 `SchemaContext`。`SoftSchema.SetSchema` 现在只更换后续默认文档的上下文，不影响已打开文档；旧的无上下文调用保留兼容。

```csharp
var context = new SchemaContext(FbsSchema.LoadFile("schema/battle.fbs"));
var result = BtlToFront.FromFile("stage.btl", context);
var document = result.Document;
document.Editing.Execute(() => FrontEdit.SetScalar(document.Root, 0, "u16", (ushort)1));
document.Editing.Undo();
document.Editing.Redo();
var built = FrontToBtl.Build(document); // 使用 document.Context
```

具名路径和 `FrontEdit.StructFromFbs(context, name, ...)` 也使用指定上下文。`StageJson.LoadJson` / `LoadFile` 可传命名参数 `context:`；`StageJson.ToNamed(document)` / `ToFieldIds(document)` 默认使用文档的 schema。

工作文档 JSON 增加可选 `interpretation`，保存 schema 内容指纹（含 include）、源文件指纹和源诊断。克隆及历史保留这些信息。带指纹的工作文档重开时要求匹配的上下文；旧 JSON 没有该信息时仍按提供的上下文或默认 schema 导入。普通字段名/字段 ID JSON 仍是数据交换格式，不携带这些工作文档元数据。

`ConvertIssue.Code` 与 `IsGuess` 描述诊断类别，`BlocksRebuild` 表示是否阻止重建。未决源数据会随文档保留，核心 `Build` 也会拒绝；写入过程中需要截断、替换为空结构或猜测缺失布局的原有恢复分支改为 Error，`ToFile` 不落盘。此检查尚不等于完整的游戏语义校验。

`document.Editing` 是无 UI 的事务与历史入口。嵌套 `Execute` 共用快照和提交；嵌套失败即使被调用者捕获，外层仍整体回滚。宿主可通过 `Enlist` 登记附加状态回滚。无变化不增加历史；历史保持最多 100 帧、约 128 MiB 克隆分配预算，至少保留两帧。`RecordExternalChange` 仅供旧编辑入口迁移时记录已经发生的变化。

连续鼠标交互使用 `BeginHistoryGroup()` 返回的 `IDisposable`：各次 `Execute` 立即提交文档，结束分组时合为一次撤销。分组不是跨事件的长事务；某次移动失败只回滚这次操作，之前成功的修改保留并可一起撤销。空分组或最终恢复原值不增加历史，也不截断已有重做分支。分组不能嵌套；撤销/重做会先结束分组，UI 仍需在松手、失去捕获、窗口失焦等中断路径释放它并停止交互。

## 用法

```csharp
using BtlCore.Fb;
using BtlCore.Front;

var result = BtlToFront.FromFile(@"path\to\stage.btl");
foreach (var issue in result.Issues)
    Console.WriteLine(issue);

// Front → .btl
var built = FrontToBtl.Build(result.Document);
File.WriteAllBytes("out.btl", built.Bytes);
```

按字段名 / 字段 ID 导出 JSON：

```csharp
var schema = FbsSchema.LoadFile("schema/battle.fbs");
string named = StageJson.ToNamed(result.Document, schema);
string ids = StageJson.ToFieldIds(result.Document, schema);
```

## 自检

### FlexBuffers 共用行为树库

Root 字段 10 的 `[ubyte]` 内嵌 FlexBuffers，保存国家、部队和建筑共用的树定义。`PackedJsonStream` 的类名和 `country_ai_bt` 标记为兼容旧调用而保留；实际读写由 `FlexBuffersCodec` 处理，不再按自定义 JSON-like 流解释。字符串采用严格 GB18030，Map Key 采用 UTF-8。

读写支持有符号/无符号 1、2、4、8 字节整数、有限浮点、null、bool、String、Map 和 Vector；读取也支持间接数值、普通标量 TypedVector 及固定长度数值向量。编码器按数值、长度和偏移选择槽宽并对齐、写字符串长度、排序 Map 键；node 向量次序保留。超出整数范围或会造成数值精度丢失的转换明确失败。它是面向 JSON 值的实现，不承诺覆盖 Blob、旧 String TypedVector 或所有标准扩展类型。

成功解包的原 payload 放在 `BtlVector.PackedSource`，随克隆、工作 JSON（`packed_source` Base64）和历史保留。保存时重新比较内容，未改则复用原字节；改过则重新编码，不要求与原布局逐字节相同，部分 TypedVector 会以普通 Vector 写出。无法解包时保留普通原字节向量，允许其他合法内容重建，但 Lua 树操作拒绝把它当树库编辑。带 enc 的编码失败会阻止导出，不再回退为空树库。

该改动不改变外层 schema 类型和解释指纹；旧版工作文档没有原字节时仍可使用，但其中此前已经误读的负数不能可靠自动纠正，应从原 BTL 重新读取。绑定对象的任务执行状态不是树库，不在此入口自动解包。格式结构正确也不等于已检查所有 btid 引用、方法参数或游戏运行语义。

单元回归使用 [官方生成夹具](../../tests/FrontEdit.Tests/Fixtures/generate_flexbuffers.py)，不要求测试环境安装 Python；另有 [独立互操作检查](../../tests/FlexBuffers.Interop/README.md)，使用官方 Python 读取 C# 写出的载荷，支持只读原版样本目录。Lua 的 `rounds` 是非负整数，`params` 是数组，空数组与缺失分别保留，详见 [Lua 开发手册](../../EditorLayout/脚本编写.md#14-行为树与-json)。

```text
dotnet run --project tests/Smoke/Smoke.csproj
dotnet run --project tests/Smoke/Smoke.csproj -- path\to\stage.btl
```

## 工程

- `Front/` — BtlFront 节点与 JSON
- `Fb/BtlToFront.cs` — 容错读入
- `Fb/SoftSchema.cs` — 读入类型提示
- `Fb/FbsSchema.cs` / `StageJson.cs` — 按 `.fbs` 导入导出
- `Fb/BtlBinaryInspection.cs` — 独立的只读原始字节查看，局部 schema 解释、物理槽状态、地址和诊断；不猜未知类型，也不替换编辑文档

`BtlToFront` 仍是编辑器的容错转换入口。`ConvertResult.Ok` 在存在 Error 时为 false；`HasUnresolvedData` 表示有异常或类型猜测，调用方不应静默重建导出。查看原文件可使用 `new BtlBinaryInspection(bytes, schema)`，通过 `Root.GetChild(index)` 或 `Find("/1/2/i0")` 按需取节点，`Reinterpret(otherSchema)` 生成独立解释，`CopyBytes()` 返回源字节副本。

工作文档的 `FieldCatalogUses` 保存具名编辑使用过的字段目录快照、指纹及字段 ID，核心仅负责存储和历史恢复，不依赖脚本宿主的目录类型。`BtlFrontJson` 克隆及 JSON 往返保留记录，游戏 BTL 和 Stage JSON 不携带它。`EditSession.BeforeCommit` 可登记附加来源记录回调，只在外层事务的数据确有变化时、历史快照生成前执行；回调失败整体回滚。回调应只补充元数据，不重入事务。
