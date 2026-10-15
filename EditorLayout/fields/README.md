# 公共字段目录

`unit.json` 覆盖部队基础字段、行为、将领及特种配置；`site.json` 覆盖建筑、工事及相关事件字段。XML 表单、列表列及 Lua 具名对象使用同一份定义。物理类型和结构体布局仍从当前文档的 schema 取得。

## 研究结论如何落地

目录的 `version: 1` 是配置文件格式版本；`revision` 是维护者填写的研究版本。每个定义有稳定的 `id`、所属表 `owner`、相对路径 `path`，可以补充：

- `kind`：`value`（默认）、`table` 或 `struct`。后两种描述可选配置组或基础结构体。
- `parent`：另一项定义的 ID。表的子项用 `/0`，结构体成员用 `.0`；有效路径沿父级拼接。修改父级路径即可移动整组绑定，父子必须有相同 owner。
- `shift` / `width`：无符号 8/16/32 位存储中的位段；其他位保持不变。
- `revision`、`evidence`：可在目录顶层声明，由未单独声明的字段继承；建议记录样例、实验报告及结论来源。
- `confidence`：`confirmed`（已确认）、`tentative`（暂定）、`unknown`（用途未知，默认）。这是语义研究记录，不参与物理类型推断或强制导出判断。
- `note`：解释当前映射及不确定性。

本轮条目沿用旧编辑器映射，以暂定或未知标注，没有把自动测试通过当作游戏内研究确认。`building.flag` 的原界面名称与 schema 注释存在不确定性，因此保留行为并记录依据。

用户覆盖写在 `EditorLayout.user`，其 `layout.xml` 添加 `<fields src="fields.json"/>`。覆盖以 ID 为单位替换整项定义，其他定义保留。例如，修订将领 ID 的说明：

```json
{
  "version": 1,
  "revision": "research.general.2",
  "fields": [
    {
      "id": "unit.general.id",
      "owner": "AIAgent",
      "parent": "unit.general",
      "path": "/0",
      "confidence": "tentative",
      "evidence": "填入实际样例和实验记录",
      "note": "保留将领 ID 映射，补充研究依据"
    }
  ]
}
```

若研究发现配置组移位，覆盖 `unit.general` 的路径；子项会随之移动。相应位置必须在文档的 schema 中有匹配的物理定义。目录不能将一个已知整数槽强行解释成表；已有文档更换物理布局仍需明确的重新解释或迁移。

## XML 和 Lua 的使用方式

XML 字段写 `field="unit.general.id"`，存在开关写 `field="unit.general"`，列表列也支持 `field`。字段不能同时声明原始 `bind`、`type`、位段或 `action`；列表列不能同时声明 `bind`。可选配置的显示依赖仍用 `when`，标签和控件类型仍留在 XML。

Lua 工具继续使用现有对象 API：

```lua
unit.general = {}          -- 启用空配置，保留已有的未知成员
unit.general.id = 0        -- 显式写入零
unit.general.active = false -- 显式写入 false
unit.general.active = nil -- 删除这个标量槽，保留配置和其他成员
unit.general = nil         -- 明确删除整组配置
```

缺失标量读取为 `nil`，显式零和 false 保留区别。向缺失配置的普通子字段写 `nil` 不会创建空父表；写非空值会创建所需父表。结构体没有可删除的成员槽，清空已存在的成员按原有规则写零；位段需要明确整数。物理布局冲突会报错并回滚。

页面动作仍负责交互规则，例如基础结构体空输入转零、取消启用删除整组配置。字段目录不取代动作脚本，也不把业务含义猜测变成自动修正。

## 版本记录与影响比对

有效目录按 ID 排序生成规范 JSON 和 SHA-256。字段顺序或文件排版改变不影响指纹；路径、位段、研究版本和依据变化会影响指纹。`FieldCatalog.ChangedFields(previous)` 返回变化字段及受父级变化影响的子字段，可再检索 XML 的 `field` 和 Lua 的具名映射，确定功能影响范围。

通过具名字段参与的成功编辑，在工作文档 JSON 的 `interpretation.field_catalogs` 中记录有效目录快照、指纹和使用过的字段 ID。它记录的是提交所用定义，不是逐字段编辑日志；同一事务中参与但最终未改变值的字段也可能被列出。纯读取、整体无变化和失败事务不新增记录。记录随克隆、撤销/重做及失败回滚恢复。

```csharp
foreach (var used in document.FieldCatalogUses)
{
    var previous = FieldCatalog.Parse(used.DefinitionJson);
    var affected = currentCatalog.ChangedFields(previous).Intersect(used.Fields);
    Console.WriteLine(string.Join(", ", affected));
}
```

这些记录只写在 `format=btlfront` 的工作文档中，不写进游戏 BTL 或游戏字段名/ID JSON。底层原始路径操作、尚未迁入目录的功能和纯 C# 修改不会自动获得此记录。目录热加载不会迁移现有数据；目前提供比对 API 和保存依据，尚无迁移向导或影响报告界面。

新增功能优先复用目录；与格式研究无关的交互状态、标签及控件安排留在界面。C# 宿主须在加载源码前调用 `ConfigureFields(bundle.Fields)`，随后加载脚本和文档。
