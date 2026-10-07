# Lua 宿主接口

面向用户与 AI 的完整对象/工具/XML/底层接口参考见 [操作手册](../../EditorLayout/操作手册.md)。

`ScriptHost` 是无 UI 的关卡脚本宿主。Lua 编写说明及 API 2 的参数表见 [脚本编写.md](../../EditorLayout/脚本编写.md)。

普通工具作者使用 [具名对象与工具声明](../../EditorLayout/写个小工具.md)。`editor.tool` 提供标题、作用范围、参数元数据和运行函数；`host.Tools` 供界面生成控件，`host.RunTool(id, args)` 执行整个批次。`selected_unit` 等范围由公共库分发，`ScriptArgs.UnitIndex/FactionIndex` 为显式列表选择，`CellIndex` 为地图选择，`Input` 为参数值。

`scripts/common/游戏对象.lua` 集中基本字段、类型布局及打包操作；`业务对象.lua` 封装集合与可选配置，`地图操作.lua` 封装扩图及关联重映射，`行为树对象.lua` 封装树和节点。现有页面动作都使用这些对象，详见 [对象接口与迁移试验](../../EditorLayout/对象接口迁移与试验.md)。C# 内部桥接处理相对路径与稳定句柄，已删除对象不能继续写入。`GameTerrain.cs` 处理格子图层紧凑存储；普通动作及工具均在事务提交前写回。游戏规则约束仍未全面封装。

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
- getter/定位器只读，不拍快照；所有写 API 检查写入权限，JSON 读取不补元数据。
- 未捕获失败后重建 MoonSharp VM，并重放成功加载的源码，避免残留全局或闭包修改。Lua 变量恢复到加载初值；持久工具状态应放在宿主的 `editor.state`。
- 源码加载只能定义工具和登记动作，失败加载不会留下部分注册。用户覆盖仍按加载顺序生效。
- Lua 协程按指令片自动挂起，外层检查指令预算、运行时间和取消。预算错误由外层抛出，不能被 Lua `pcall` 捕获后无限继续。
- 限制覆盖 Lua 运行阶段；编译、C# 回调和文档快照无法被协程抢占。不提供严格内存配额或强实时保证。
- API 2 固定数字路径操作的参数，保留原有重载接口兼容现有脚本。64 位整数使用十进制字符串接口，JSON 支持复杂对象并拒绝模糊、丢失数据的数组形状。

## 验证

`tests/FrontEdit.Tests/ScriptApiTests.cs` 覆盖指令与时间预算、取消、失败加载、文档/剪贴板/状态回滚、只读边界、缓存寿命、精确整数及 JSON 类型。`GameToolsTests` 覆盖工具声明/参数、选择/批量、局部修改、旧结构体类型补齐、图层紧凑顺序、整体回滚和对象寿命。`XmlLayoutTests` 检查实际地形字段在一次刷新中只扫描一次；`tests/XmlUi.Smoke` 验证 XML 页面、工具菜单/参数窗口、编辑、撤销及热加载。
