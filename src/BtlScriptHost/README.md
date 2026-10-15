# Lua 宿主接口

新增字段类型、载荷编解码器注册和二进制读写接口见 [Lua 格式扩展](../../EditorLayout/格式扩展.md)。宿主在加载脚本后调用 `CreateSchemaContext()`，将结果传给文件读取入口；编辑器已接入此流程。载荷按需解码，显式 `format.write` 参加当前事务，保存阶段不执行 Lua。

`ScriptHost` 是无 UI 的关卡脚本宿主。面向工具作者、格式研究者和维护者的 [Lua 开发手册](../../EditorLayout/脚本编写.md) 包含安装、对象/工具/XML、API 2、事务、排错和验证流程。

普通工具作者可从手册的“第一个可用工具”开始。`editor.tool` 提供标题、作用范围、参数元数据和运行函数；`host.Tools` 供界面生成控件，`host.RunTool(id, args)` 执行整个批次。`selected_unit` 等范围由公共库分发，`ScriptArgs.UnitIndex/FactionIndex` 为显式列表选择，`CellIndex` 为地图选择，`Input` 为参数值。

`scripts/common/对象工厂.lua` 提供属性、集合与可选配置工厂；`游戏入口.lua` 提供命名空间和工具分发。部队、势力、事件、关卡和地图分别在 `scripts/objects` 中一次定义完整，扩图实体规则位于 `common/扩图规则.lua`，紧凑地形读写复用 `TerrainEdits`。JSON 树与节点仍由 `common/行为树对象.lua` 管理。扩展入口、批量删除、可选配置辅助方法及类型提示见 [Lua 对象扩展](../../EditorLayout/对象扩展.md)。C# 桥接管理相对路径与稳定句柄，已删除对象不能继续写入；普通动作及工具共用事务。游戏规则约束仍未全面封装。

## C# 使用

```csharp
var host = new ScriptHost(new ScriptLimits
{
    InstructionBudget = 5_000_000,
    TimeBudget = TimeSpan.FromSeconds(2)
});
host.Load(document);
host.Execute(scriptText, "tool.lua");
host.Run("apply", new ScriptArgs { CellIndex = 4, Value = 7 });

using (host.BeginRead())
{
    var a = host.CallGet("read_a", new ScriptArgs { CellIndex = 4 });
    var b = host.CallGet("read_b", new ScriptArgs { CellIndex = 4 });
    // 两次读取共享 ctx.cache，没有文档快照。
}
```

宿主按单线程使用；读取范围内不能提交修改、加载源码或切换文档。动作及读取执行期间不能重入。`Cancellation` 可以接外部取消令牌；该属性不表示 UI 已有取消入口。

## 边界

- 写动作以整份文档快照为事务，剪贴板和 `editor.state` 也参加失败回滚。只有文档发生变化才增加 `UndoFrames`。
- 文档事务与历史现在由 `Document.Editing` 统一管理，Lua 和 C# 操作可以加入同一个外层 `Execute`。`UndoFrames` / `SnapshotCount` 是当前文档会话的提交/事务快照计数；用户撤销仅恢复文档，失败回滚仍恢复剪贴板、状态及 Lua 运行时。
- getter/定位器只读，不拍快照；所有写 API 检查写入权限，JSON 读取不补元数据。
- 未捕获失败后重建 MoonSharp VM，并重放成功加载的源码，避免残留全局或闭包修改。Lua 变量恢复到加载初值；持久工具状态应放在宿主的 `editor.state`。
- 源码加载只能定义工具和登记动作，失败加载不会留下部分注册。用户覆盖仍按加载顺序生效。
- Lua 协程按指令片自动挂起，外层检查指令预算、运行时间和取消。预算错误由外层抛出，不能被 Lua `pcall` 捕获后无限继续。
- 限制覆盖 Lua 运行阶段；编译、C# 回调和文档快照无法被协程抢占。不提供严格内存配额或强实时保证。
- API 2 固定数字路径操作的参数，保留原有重载接口兼容现有脚本。64 位整数使用十进制字符串接口，JSON 支持复杂对象并拒绝模糊、丢失数据的数组形状。

## 验证

`tests/FrontEdit.Tests/ScriptApiTests.cs` 覆盖指令与时间预算、取消、失败加载、文档/剪贴板/状态回滚、只读边界、缓存寿命、精确整数及 JSON 类型。`GameToolsTests` 覆盖工具声明/参数、选择/批量、局部修改、旧结构体类型补齐、图层紧凑顺序、整体回滚和对象寿命。`XmlLayoutTests` 检查实际地形字段在一次刷新中只扫描一次；`tests/XmlUi.Smoke` 验证 XML 页面、工具菜单/参数窗口、编辑、撤销及热加载。

## 公共字段定义

`EditorLayout/fields/unit.json` 覆盖部队基础字段、行为、将领和特种配置；`site.json` 覆盖建筑、工事及相关事件。物理类型和布局取自当前文档 schema，XML 的 `field` 绑定与 Lua 具名对象共用字段路径及位段。配置组通过 `parent` 关联子字段，组的位置修改会传递到整个界面与脚本；已有数字 `bind` 保持可用。

布局根通过 `<fields src="fields/unit.json"/>` 等声明加载目录，用户覆盖以 ID 替换整项定义。C# 调用者在加载源码前调用 `host.ConfigureFields(bundle.Fields)`，再加载脚本和文档；默认目录从相同 JSON 嵌入构建。热加载使用新宿主，全部准备成功后才替换。

不传上下文的 `LayoutLoader.Load` 只展开目录路径、检查声明与父级引用，不提前用默认 FBS 校验位段。加载全部 Lua 后调用 `host.CreateSchemaContext()`，再以该上下文调用 `bundle.Fields.Bind(page, context)` 校验页面；有已打开文档时使用它的固定上下文。显式传入上下文的 `LayoutLoader.Load(..., context)` 仍立即校验。这样新版 FBS 中拆分的字节成员可以先经过 Lua 的等宽兼容定义，避免在兼容定义加载前报 `unit.stack` 越界；并不会跳过最终的物理类型或位段校验。

`version` 表示目录格式，`revision`、`confidence`、`evidence` 记录研究版本、确定程度和依据。有效目录有规范 JSON 和指纹；`ChangedFields(previous)` 追踪字段及父级变化。参与成功编辑的目录和字段 ID 写入工作文档来源记录，随历史恢复；读取、无变化和失败不留新记录。这不是完整的操作审计，也不会自动迁移旧数据。

配置格式、覆盖示例、可选值规则及版本比对见 [公共字段目录说明](../../EditorLayout/fields/README.md)。`FieldCatalogTests` 和 `FieldEvolutionTests` 覆盖字段与整组覆盖、位段保留、来源保存和嵌套回滚；UI 自检的 `FieldCatalogUiChecks` 使用真实控件验证将领组移位、零/false/缺失及建筑成员映射。
