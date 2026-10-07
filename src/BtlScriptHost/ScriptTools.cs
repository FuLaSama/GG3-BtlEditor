using System.Globalization;
using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting;

public sealed record ToolChoice(string Label, object Value);
public sealed record ToolParameter(string Id, string Label, string Kind, object Default,
    double? Minimum, double? Maximum, bool Required, IReadOnlyList<ToolChoice> Choices);
public sealed record ScriptTool(string Id, string Title, string Description, string Target,
    string Confirmation, IReadOnlyList<ToolParameter> Parameters);

public sealed partial class ScriptHost
{
    sealed record ToolRec(ScriptTool Definition, DynValue Run);
    readonly Dictionary<string, ToolRec> _tools = new(StringComparer.Ordinal);
    static readonly HashSet<string> ToolTargets = new(StringComparer.Ordinal)
    {
        "selected_unit", "all_units", "faction_units", "selected_faction", "all_factions",
        "selected_cell", "all_cells", "stage", "map"
    };
    static readonly HashSet<string> ParameterKinds = new(StringComparer.Ordinal)
    {
        "number", "integer", "boolean", "text", "choice", "faction", "unit_type", "general"
    };

    public IReadOnlyList<ScriptTool> Tools => _tools.Values.Select(t => t.Definition).ToArray();

    public int RunTool(string id, ScriptArgs args = null)
    {
        if (!_tools.TryGetValue(id, out var tool)) throw new FrontEditException("找不到脚本工具：" + id);
        var input = ValidateToolInput(tool.Definition, args?.Input);
        int count = 0;
        Transact(() =>
        {
            _allowWrite = true;
            var game = _script.Globals.Get("game");
            var dispatcher = game.Type == DataType.Table ? game.Table.Get("_run_tool") : DynValue.Nil;
            if (dispatcher.Type != DataType.Function) throw new FrontEditException("缺少游戏对象公共库，请重新加载默认布局");
            var result = Invoke(dispatcher, tool.Run, DynValue.NewString(tool.Definition.Target), MakeCtx(args), ToDyn(input));
            count = AsIndex(result);
        }, true, "工具「" + tool.Definition.Title + "」");
        return count;
    }

    void InstallToolsApi(Table editor)
    {
        editor.Set("tool", Api("editor.tool", OnTool));
        // 公共 Lua 库的桥接接口；普通工具作者使用 game 对象，不接触字段路径。
        var bridge = new Table(_script);
        bridge.Set("index_at_cell", Api("editor.internal.index_at_cell", (_, a) =>
        {
            if (a[2].Type != DataType.Number) return DynValue.Nil;
            var vector = NsPath.NodeAt(Document, ArgString(a, 0)) as BtlVector;
            string member = ArgString(a, 1);
            bool last = a.Count > 3 && a[3].CastToBool();
            string required = a.Count > 4 && !a[4].IsNil() ? ArgString(a, 4) : null;
            string alternate = a.Count > 5 && !a[5].IsNil() ? ArgString(a, 5) : null;
            var row = new BtlFrontDocument();
            int found = -1;
            for (int i = 0; i < (vector?.V.Count ?? 0); i++)
            {
                if (vector.V[i] is not BtlTable table) continue;
                row.Root = table;
                var value = NsPath.Get(row, member);
                if (value == null || Convert.ToDouble(value, CultureInfo.InvariantCulture) != a[2].Number) continue;
                if (required != null && NsPath.Get(row, required) == null
                    && (alternate == null || NsPath.Get(row, alternate) == null)) continue;
                found = i;
                if (!last) break;
            }
            return found < 0 ? DynValue.Nil : DynValue.NewNumber(found);
        }));
        bridge.Set("check_context", Api("editor.internal.check_context", (_, a) => { Unwrap(a[0]); return DynValue.Nil; }));
        bridge.Set("anchor", Api("editor.internal.anchor", (_, a) =>
        {
            string path = ArgString(a, 0);
            var node = NsPath.NodeAt(Document, path);
            int bracket = path.LastIndexOf('[');
            var owner = bracket >= 0 && path.EndsWith(']') ? NsPath.NodeAt(Document, path[..bracket]) as BtlVector : null;
            return node is BtlTable table ? UserData.Create(new ScriptHandle { Epoch = _epoch, Table = table, Owner = owner }) : DynValue.Nil;
        }));
        bridge.Set("get", Api("editor.internal.get", (_, a) =>
        {
            var table = Unwrap(a[0]).Table ?? throw new FrontEditException("对象不是表");
            return ToDyn(NsPath.Get(new BtlFrontDocument { Root = table }, ArgString(a, 1)));
        }));
        bridge.Set("exists", Api("editor.internal.exists", (_, a) =>
            DynValue.NewBoolean(NsPath.NodeAt(new BtlFrontDocument { Root = Unwrap(a[0]).Table }, ArgString(a, 1)) != null)));
        bridge.Set("ensure", Api("editor.internal.ensure", (_, a) =>
        {
            RequireWrite();
            var document = new BtlFrontDocument { Root = Unwrap(a[0]).Table };
            string path = ArgString(a, 1);
            var layout = a.Count > 2 && !a[2].IsNil() ? ReadStrings(a[2]) : null;
            if (layout != null && NsPath.NodeAt(document, path) is BtlStruct structure)
            {
                if (structure.Layout.Count == 0)
                {
                    if (structure.V.Count > layout.Count) throw new FrontEditException("结构体包含类型未知的额外成员，无法安全修改");
                    var defaults = FrontEdit.NewStruct(layout);
                    structure.Layout.AddRange(defaults.Layout);
                    while (structure.V.Count < defaults.V.Count) structure.V.Add(defaults.V[structure.V.Count]);
                }
            }
            else NsPath.Ensure(document, path, null, layout);
            return DynValue.Nil;
        }));
        bridge.Set("index", Api("editor.internal.index", (_, a) =>
        {
            var vector = NsPath.NodeAt(Document, ArgString(a, 0)) as BtlVector;
            int index = ObjectIndex(vector, Unwrap(a[1]).Table);
            if (index < 0) throw new FrontEditException("对象已从集合移除");
            return DynValue.NewNumber(index);
        }));
        bridge.Set("array_get", Api("editor.internal.array_get", (_, a) =>
        {
            var vector = NsPath.NodeAt(new BtlFrontDocument { Root = Unwrap(a[0]).Table }, ArgString(a, 1)) as BtlVector;
            return ToDyn(vector?.V.ToArray() ?? Array.Empty<object>());
        }));
        bridge.Set("array_set", Api("editor.internal.array_set", (_, a) =>
        {
            RequireWrite();
            _vectorIndices.Clear();
            NsPath.Fill(new BtlFrontDocument { Root = Unwrap(a[0]).Table }, ArgString(a, 1), ArgString(a, 2), ReadArray(a[3]));
            _vectorIndices.Clear();
            return DynValue.Nil;
        }));
        bridge.Set("set", Api("editor.internal.set", (_, a) =>
        {
            RequireWrite();
            var table = Unwrap(a[0]).Table ?? throw new FrontEditException("对象不是表");
            var document = new BtlFrontDocument { Root = table };
            string path = ArgString(a, 1);
            if (a.Count > 4 && !a[4].IsNil())
            {
                int memberSeparator = path.LastIndexOf('.');
                if (memberSeparator < 0) throw new FrontEditException("结构体布局只能用于成员路径");
                string structPath = path[..memberSeparator];
                var layout = ReadStrings(a[4]);
                var structure = NsPath.NodeAt(document, structPath) as BtlStruct
                    ?? throw new FrontEditException("对象缺少已知结构体");
                if (structure.Layout.Count == 0)
                {
                    if (structure.V.Count > layout.Count)
                        throw new FrontEditException("结构体包含类型未知的额外成员，无法安全修改");
                    var defaults = FrontEdit.NewStruct(layout);
                    structure.Layout.AddRange(defaults.Layout);
                    while (structure.V.Count < defaults.V.Count)
                        structure.V.Add(defaults.V[structure.V.Count]);
                }
            }
            NsPath.Set(document, path, ArgString(a, 2), FromDyn(a[3]));
            return DynValue.Nil;
        }));
        bridge.Set("cell_get", Api("editor.internal.cell_get", OnGameCellGet));
        bridge.Set("flush_cells", Api("editor.internal.flush_cells", (_, _) =>
        {
            RequireWrite(); FlushGameOperation(); ResetGameOperation(); return DynValue.Nil;
        }));
        bridge.Set("cell_set", Api("editor.internal.cell_set", OnGameCellSet));
        bridge.Set("cell_paste", Api("editor.internal.cell_paste", (_, a) =>
        {
            RequireWrite(); Unwrap(a[0]);
            int index = GameCellIndex(a[1]); ProjectGameCells();
            ushort word = Convert.ToUInt16(FrontPath.Coerce("u16", FromDyn(a[2])));
            var cell = _gameCells[index];
            BtlStruct Attribute(DynValue source)
            {
                var attribute = NewGameAttribute(); var values = source.IsNil() ? new List<object>() : ReadArray(source);
                for (int i = 0; i < Math.Min(4, values.Count); i++)
                    attribute.V[i] = FrontPath.Coerce(i < 2 ? "u8" : "i8", values[i] ?? 0);
                return attribute;
            }
            cell.Decor = (word & 1024) != 0 ? Attribute(a[3]) : null;
            cell.Main = (word & 2048) != 0 ? Attribute(a[4]) : null;
            cell.Secondary = (word & 4096) != 0 ? Attribute(a[5]) : null;
            WriteGameWord(index, word); _gameAttributesDirty = true;
            return DynValue.Nil;
        }));
        editor.Set("internal", DynValue.NewTable(bridge));
    }

    DynValue OnTool(ScriptExecutionContext context, CallbackArguments args)
    {
        RequireRegistration();
        if (args.Count != 1 || args[0].Type != DataType.Table) throw new FrontEditException("需要一个工具声明表");
        var table = args[0].Table;
        CheckKeys(table, "工具", "id", "title", "description", "target", "params", "run", "confirm");
        string title = Text(table, "title", true);
        string id = Text(table, "id") ?? title;
        string target = Text(table, "target", true);
        if (!ToolTargets.Contains(target)) throw new FrontEditException("未知作用范围 " + target + "；可用：" + string.Join(", ", ToolTargets));
        var run = table.Get("run");
        if (run.Type != DataType.Function) throw new FrontEditException("工具「" + title + "」需要 run 函数");
        var parameters = new List<ToolParameter>();
        var declared = table.Get("params");
        if (!declared.IsNil())
        {
            if (declared.Type != DataType.Table) throw new FrontEditException("params 必须是参数数组");
            var list = declared.Table;
            if (list.Pairs.Count() != list.Length) throw new FrontEditException("params 必须使用连续的 1 起下标");
            for (int i = 1; i <= list.Length; i++)
            {
                var entry = list.Get(i);
                if (entry.Type != DataType.Table) throw new FrontEditException("每个参数必须是一张表");
                var p = entry.Table;
                CheckKeys(p, "参数", "id", "label", "type", "default", "min", "max", "required", "choices");
                string key = Text(p, "id", true);
                if (parameters.Any(v => v.Id == key)) throw new FrontEditException("参数 id 重复：" + key);
                string kind = Text(p, "type") ?? "integer";
                if (!ParameterKinds.Contains(kind)) throw new FrontEditException("未知参数类型：" + kind);
                var choices = new List<ToolChoice>();
                var choiceValue = p.Get("choices");
                if (!choiceValue.IsNil())
                {
                    if (kind != "choice" || choiceValue.Type != DataType.Table) throw new FrontEditException("choices 只用于 choice 类型的数组");
                    if (choiceValue.Table.Pairs.Count() != choiceValue.Table.Length) throw new FrontEditException("choices 必须是连续数组");
                    for (int n = 1; n <= choiceValue.Table.Length; n++)
                    {
                        var item = choiceValue.Table.Get(n);
                        if (item.Type == DataType.Table) CheckKeys(item.Table, "选项", "label", "value");
                        object value = item.Type == DataType.Table ? FromDyn(item.Table.Get("value")) : FromDyn(item);
                        if (value is not (string or bool or double) || value is double number && !double.IsFinite(number))
                            throw new FrontEditException("选项值必须是文本、布尔或有限数字");
                        string label = item.Type == DataType.Table ? Text(item.Table, "label", true) : Convert.ToString(value, CultureInfo.InvariantCulture);
                        choices.Add(new ToolChoice(label, value));
                    }
                }
                if (kind == "choice" && choices.Count == 0) throw new FrontEditException("choice 参数需要 choices");
                double? min = Number(p, "min"), max = Number(p, "max");
                if (min > max) throw new FrontEditException("参数 min 不能大于 max");
                var parameter = new ToolParameter(key, Text(p, "label") ?? key, kind, FromDyn(p.Get("default")), min, max,
                    Boolean(p, "required", true), choices.AsReadOnly());
                if (parameter.Default != null) ValidateParameter(parameter, parameter.Default);
                parameters.Add(parameter);
            }
        }
        if (target == "faction_units" && !parameters.Any(p => p.Id == "faction"))
            parameters.Insert(0, new ToolParameter("faction", "所属势力", "faction", null, 0, 65535, true, Array.Empty<ToolChoice>()));
        _tools[id] = new ToolRec(new ScriptTool(id, title, Text(table, "description"), target,
            Text(table, "confirm"), parameters.AsReadOnly()), run);
        return DynValue.Nil;
    }

    static Dictionary<string, object> ValidateToolInput(ScriptTool tool, Dictionary<string, object> supplied)
    {
        if (supplied != null && supplied.Keys.Any(key => !tool.Parameters.Any(p => p.Id == key)))
            throw new FrontEditException("工具「" + tool.Title + "」收到未声明的参数");
        var input = new Dictionary<string, object>();
        foreach (var parameter in tool.Parameters)
        {
            object value = supplied?.GetValueOrDefault(parameter.Id) ?? parameter.Default;
            if (value == null && parameter.Kind == "boolean") value = false;
            if (value == null)
            {
                if (parameter.Required) throw new FrontEditException("请填写「" + parameter.Label + "」");
                continue;
            }
            input[parameter.Id] = ValidateParameter(parameter, value);
        }
        return input;
    }

    static object ValidateParameter(ToolParameter parameter, object value)
    {
        string error = "参数「" + parameter.Label + "」";
        if (parameter.Kind == "boolean")
            return value is bool ? value : throw new FrontEditException(error + "必须是勾选值");
        if (parameter.Kind == "text")
            return value is string text && (!parameter.Required || !string.IsNullOrWhiteSpace(text)) ? value
                : throw new FrontEditException(error + "需要文本");
        if (parameter.Kind == "choice")
        {
            var choice = parameter.Choices.FirstOrDefault(c => Equals(c.Value, value)
                || IsNumber(c.Value) && IsNumber(value)
                && Convert.ToDouble(c.Value, CultureInfo.InvariantCulture) == Convert.ToDouble(value, CultureInfo.InvariantCulture));
            return choice?.Value ?? throw new FrontEditException(error + "不在选项列表中");
        }
        if (value is not (double or float or byte or sbyte or ushort or short or uint or int or ulong or long or decimal))
            throw new FrontEditException(error + "必须是数字");
        double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(n) || parameter.Kind != "number" && n != Math.Truncate(n)) throw new FrontEditException(error + "需要有限的" + (parameter.Kind == "number" ? "数字" : "整数"));
        if (parameter.Minimum != null && n < parameter.Minimum || parameter.Maximum != null && n > parameter.Maximum)
            throw new FrontEditException(error + "超出允许范围（"
                + (parameter.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "不限") + " ～ "
                + (parameter.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "不限") + "）");
        if (parameter.Kind is "faction" or "general" or "unit_type" && (n < 0 || n > ushort.MaxValue))
            throw new FrontEditException(error + "ID 超出范围");
        return n;
    }

    static bool IsNumber(object value) => value is double or float or byte or sbyte or ushort or short or uint or int or ulong or long or decimal;

    static void CheckKeys(Table table, string label, params string[] allowed)
    {
        foreach (var pair in table.Pairs)
            if (pair.Key.Type != DataType.String || !allowed.Contains(pair.Key.String))
                throw new FrontEditException(label + "有未知属性：" + pair.Key.ToPrintString());
    }
    static string Text(Table table, string key, bool required = false)
    {
        var value = table.Get(key);
        if (value.IsNil() && !required) return null;
        if (value.Type != DataType.String || string.IsNullOrWhiteSpace(value.String)) throw new FrontEditException(key + "需要非空文本");
        return value.String;
    }
    static double? Number(Table table, string key)
    {
        var value = table.Get(key);
        if (value.IsNil()) return null;
        if (value.Type != DataType.Number || !double.IsFinite(value.Number)) throw new FrontEditException(key + "需要有限数字");
        return value.Number;
    }
    static bool Boolean(Table table, string key, bool fallback)
    {
        var value = table.Get(key);
        if (value.IsNil()) return fallback;
        if (value.Type != DataType.Boolean) throw new FrontEditException(key + "需要 true 或 false");
        return value.Boolean;
    }
}
