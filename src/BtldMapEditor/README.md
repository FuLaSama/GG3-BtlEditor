# BtldMapEditor

使用编辑器或让 AI 协助编写 Lua，先看 [完整操作手册](../../EditorLayout/操作手册.md)。

战役地图编辑器。打开关卡按后缀识别，经 [`BtlCore`](../BtlCore/README.md) 转成内存 BtlFront 再编辑。

## 运行

仓库根打开 `BtldMapEditor.sln`，启动项目选 **BtldMapEditor**。输出：仓库根 `dist\`。

| 菜单 | 作用 |
|------|------|
| **新建关卡地图…** | 空白图 |
| **打开…** | `.btl` / `.json`（字段名或字段 ID） |
| **导出为…** | `.btl` / JSON 字段名 / JSON 原始字段 ID |
| **脚本工具** | Lua 声明自动生成的工具菜单；带参数的工具自动显示输入窗口 |

## 结构

| 路径 | 作用 |
|------|------|
| `Front/` | 会话、注册表、编辑器侧 Front JSON |
| 其余 `.cs` | 窗体 / 画布 / 贴图 |

依赖：同目录的 `BtlCore`（读写转换）。显示用名表和贴图来自仓库根 `GameData` / `地形贴图相关`（构建时拷到 `dist`）。

右侧面板的输入框和修改动作来自仓库根 `EditorLayout`。自己写 Lua 或加输入框，见 [`EditorLayout/脚本编写.md`](../../EditorLayout/脚本编写.md)。

普通工具从 [`写个小工具.md`](../../EditorLayout/写个小工具.md) 开始。把 Lua 放在 `EditorLayout.user/tools`，重新加载布局即可使用；不需要 XML。自带七个工具覆盖生命、经验、金币和地形；部队、势力、地图及图层用具名属性访问，打包字段由公共库处理。整批工具共用一个事务和一步撤销。

现有 25 个页面动作脚本也已改为对象调用，覆盖创建/删除、可选部队配置、建筑/工事、天气/目标/路线、行为树以及扩图。迁移规则、样例关卡试验和导出文件见 [`对象接口迁移与试验.md`](../../EditorLayout/对象接口迁移与试验.md)。

编辑页和新建地图对话框全部由 XML 动态生成，不再创建旧的硬编码属性面板。地形贴图选择和国家行为树使用 XML 声明的专用控件；势力、地图尺寸、战争迷雾和地图外部队也已经迁入 XML。新增页面只需在 `layout.xml` 注册，热加载失败保留上一份有效布局。

Lua API 2 保留原有底层接口，增加固定参数的数据、JSON、剪贴板及会话状态接口。宿主执行预算和失败回滚、只读刷新缓存、64 位整数精确读写的边界见 [`BtlScriptHost/README.md`](../BtlScriptHost/README.md)。地形字段在一次刷新中共用属性扫描，读取不再生成整份关卡快照。

Windows UI 回归自检：先 `dotnet build tests/XmlUi.Smoke/XmlUi.Smoke.csproj -c Release -m:1`，再 `dotnet run --project tests/XmlUi.Smoke/XmlUi.Smoke.csproj -c Release --no-build`。可追加 `-- --render`，把页面预览写入 `artifacts/xml-ui/`。该自检覆盖页面挂载、样例加载、刷新不写入、热加载、复选框与工具撤销重做、自动参数窗口、部队顺序/重叠部队/额外地形属性保留、嵌套行为树编辑、地图外部队下标、路线字段和新建窗口默认值。
