# BtlCore

BTL（FlatBuffers）↔ BtlFront。从零实现；旧管线若恢复，会放在仓库根的 `BtlCore.old`。

正式 schema 在 [`../../schema/battle.fbs`](../../schema/battle.fbs)。

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

```text
dotnet run --project tests/Smoke/Smoke.csproj
dotnet run --project tests/Smoke/Smoke.csproj -- path\to\stage.btl
```

## 工程

- `Front/` — BtlFront 节点与 JSON
- `Fb/BtlToFront.cs` — 容错读入
- `Fb/SoftSchema.cs` — 读入类型提示
- `Fb/FbsSchema.cs` / `StageJson.cs` — 按 `.fbs` 导入导出
