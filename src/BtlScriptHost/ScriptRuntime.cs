using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting;

/// <summary>限制一次加载、动作或 getter 的 Lua 指令和运行时间。宿主回调不可抢占。</summary>
public sealed class ScriptLimits
{
    public int InstructionBudget { get; init; } = 5_000_000;
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (InstructionBudget < 1 || TimeBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InstructionBudget), "脚本预算必须大于零");
    }
}

public sealed partial class ScriptHost
{
    const long MaxExactInteger = 9_007_199_254_740_991;
    readonly List<(string Code, string Name)> _sources = new();
    JsonObject _state = new();
    Table _readCache;
    Table _jsonNull;
    int _readDepth;
    bool _running;
    bool _registering;
    int _remainingInstructions;
    Stopwatch _clock;
    DynValue _invocationRunner;
    Coroutine _invocationCoroutine;

    void CreateInvocationRunner()
    {
        _invocationCoroutine = null;
        // MoonSharp 每建一个协程都会分配执行栈。通过私有暂停点复用栈，
        // 每次 Resume 仍执行原来的指令、时间和取消检查；不对用户开放 yield。
        var factory = _script.LoadString("""
            local pause = ...
            local unpack_args = table.unpack or unpack
            return function(fn, args)
              while true do
                fn, args = pause(fn(unpack_args(args, 1, args.n)))
              end
            end
            """, codeFriendlyName: "internal invocation runner");
        _invocationRunner = _script.Call(factory, DynValue.NewCallback((_, args) => DynValue.NewYieldReq(args.GetArray())));
    }
    public ScriptLimits Limits { get; }
    public CancellationToken Cancellation { get; set; }
    /// <summary>供性能诊断：只读调用不产生文档快照。</summary>
    public int SnapshotCount { get; private set; }

    void EnsureIdle()
    {
        if (_running) throw new FrontEditException("脚本宿主不能重入");
    }

    void StartBudget()
    {
        _running = true;
        _remainingInstructions = Limits.InstructionBudget;
        _clock = Stopwatch.StartNew();
    }

    DynValue Invoke(DynValue function, params DynValue[] args)
    {
        var coroutine = _invocationCoroutine ??= _script.CreateCoroutine(_invocationRunner).Coroutine;
        var packed = new Table(_script);
        packed.Set("n", DynValue.NewNumber(args.Length));
        for (int i = 0; i < args.Length; i++) packed.Set(i + 1, args[i]);
        bool first = true;
        while (true)
        {
            if (Cancellation.IsCancellationRequested) throw new FrontEditException("脚本执行已取消");
            if (_clock.Elapsed >= Limits.TimeBudget) throw new FrontEditException("脚本超过运行时间预算");
            if (_remainingInstructions <= 0) throw new FrontEditException("脚本超过指令预算");
            int slice = Math.Min(10_000, _remainingInstructions);
            coroutine.AutoYieldCounter = slice;
            var result = first ? coroutine.Resume(function, DynValue.NewTable(packed)) : coroutine.Resume();
            first = false;
            // MoonSharp 不提供实际计数；强制挂起表示已消耗这一片的预算。
            if (coroutine.State == CoroutineState.ForceSuspended)
            {
                _remainingInstructions -= slice;
                continue;
            }
            if (coroutine.State != CoroutineState.Suspended)
                throw new FrontEditException("编辑动作不能主动挂起");
            if (Cancellation.IsCancellationRequested) throw new FrontEditException("脚本执行已取消");
            if (_clock.Elapsed >= Limits.TimeBudget) throw new FrontEditException("脚本超过运行时间预算");
            return result;
        }
    }

    void ExecuteSource(string code, string name)
    {
        StartBudget();
        _registering = true;
        try { Invoke(_script.LoadString(code, null, name)); }
        finally { _registering = false; _running = false; }
    }

    void RequireRegistration()
    {
        if (!_registering) throw new FrontEditException("action/resolve 只能在加载脚本时登记");
    }

    void RecoverRuntime()
    {
        // 闭包和 Lua 全局表不能可靠地深拷贝。失败后重放已成功加载的源码，
        // 去掉失败动作留下的全局/闭包修改；持久状态保存在宿主的 editor.state。
        _running = false;
        _allowWrite = false;
        _epoch++;
        _readCache = null;
        _actions.Clear();
        _resolvers.Clear();
        _tools.Clear();
        ResetGameOperation();
        CreateRuntime();
        var cancellation = Cancellation;
        Cancellation = default;
        try
        {
            foreach (var source in _sources) ExecuteSource(source.Code, source.Name);
        }
        catch
        {
            _actions.Clear();
            _resolvers.Clear();
            _tools.Clear();
            throw new FrontEditException("脚本运行时恢复失败，请重新加载布局");
        }
        finally
        {
            Cancellation = cancellation;
            if (_readDepth > 0) _readCache = new Table(_script);
        }
    }

    void ReadOnly(string operation, Action read)
    {
        EnsureIdle();
        if (Document?.Root == null) throw new FrontEditException("没有可读取的关卡");
        StartBudget();
        _epoch++;
        _allowWrite = false;
        if (_readDepth == 0) ResetGameOperation();
        try { read(); }
        catch (Exception ex)
        {
            RecoverRuntime();
            throw Describe(operation, ex);
        }
        finally { _epoch++; _allowWrite = false; _running = false; if (_readDepth == 0) ResetGameOperation(); }
    }

    /// <summary>一次 UI 刷新共享 ctx.cache；禁止在此范围内写入或更换文档。</summary>
    public IDisposable BeginRead()
    {
        EnsureIdle();
        if (_readDepth++ == 0)
        {
            ResetGameOperation();
            _readCache = new Table(_script);
        }
        return new ReadScope(this);
    }

    sealed class ReadScope : IDisposable
    {
        ScriptHost _host;
        public ReadScope(ScriptHost host) => _host = host;
        public void Dispose()
        {
            if (_host == null) return;
            if (--_host._readDepth == 0)
            {
                _host._readCache = null;
                _host.ResetGameOperation();
            }
            _host = null;
        }
    }

    static FrontEditException Describe(string operation, Exception error)
    {
        string details = error is InterpreterException lua ? lua.DecoratedMessage ?? lua.Message : error.Message;
        return new FrontEditException(operation + "：" + details);
    }

    DynValue Api(string name, Func<ScriptExecutionContext, CallbackArguments, DynValue> body)
        => DynValue.NewCallback((context, args) =>
        {
            try
            {
                if (_registering && name != "editor.action" && name != "editor.resolve" && name != "editor.tool"
                    && name != "editor.band" && name != "editor.bor" && name != "editor.bxor"
                    && name != "editor.lshift" && name != "editor.rshift" && name != "editor.remap_cell"
                    && name != "editor.data.row")
                    throw new FrontEditException("加载阶段只能定义工具和登记动作，不能访问关卡或会话状态");
                return body(context, args);
            }
            catch (Exception ex)
            {
                var values = Enumerable.Range(0, args.Count).Take(4).Select(i =>
                    args[i].Type == DataType.String ? "\"" + args[i].String + "\""
                        : args[i].Type == DataType.Number ? args[i].Number.ToString(CultureInfo.InvariantCulture)
                        : args[i].Type.ToString());
                throw new ScriptRuntimeException(name + "(" + string.Join(", ", values) + ")：" + ex.Message);
            }
        });

    void InstallApi(Table editor)
    {
        foreach (var pair in editor.Pairs.ToArray())
        {
            var callback = pair.Value.Callback.ClrCallback;
            editor.Set(pair.Key, Api("editor." + pair.Key.String, callback));
        }
        editor.Set("api_version", DynValue.NewNumber(2));
        editor.Set("modules", DynValue.NewTable(new Table(_script)));
        InstallToolsApi(editor);

        var data = new Table(_script);
        void Data(string name, int arguments, Func<ScriptExecutionContext, CallbackArguments, DynValue> callback)
            => data.Set(name, Api("editor.data." + name, (c, a) =>
            {
                if (a.Count != arguments) throw new FrontEditException("需要 " + arguments + " 个参数");
                NumericPath(a);
                return callback(c, a);
            }));
        Data("get", 1, OnGet);
        Data("set", 3, OnSet);
        Data("count", 1, OnCount);
        Data("append", 1, OnAppend);
        Data("remove", 2, OnRemove);
        Data("move", 3, OnMove);
        Data("fill", 3, OnFill);
        Data("exists", 1, (_, a) => DynValue.NewBoolean(NsPath.Look(Document, a[0].String, true) != null));
        Data("ensure_table", 1, (_, a) =>
        {
            RequireWrite(); NsPath.Ensure(Document, a[0].String, null, null); return DynValue.Nil;
        });
        Data("ensure_struct", 2, (_, a) =>
        {
            RequireWrite(); NsPath.Ensure(Document, a[0].String, null, ReadStrings(a[1])); return DynValue.Nil;
        });
        Data("ensure_vector", 2, (_, a) =>
        {
            RequireWrite();
            string elem = ArgString(a, 1);
            if (elem == "struct") throw new FrontEditException("struct 向量请使用 ensure_struct_vector 并提供成员类型");
            NsPath.Ensure(Document, a[0].String, elem, null); return DynValue.Nil;
        });
        Data("ensure_struct_vector", 2, (_, a) =>
        {
            RequireWrite();
            NsPath.Ensure(Document, a[0].String, "struct", ReadStrings(a[1])); return DynValue.Nil;
        });
        Data("row", 2, (_, a) =>
        {
            int index = AsIndex(a[1]);
            if (index < 0) throw new FrontEditException("下标不能小于零");
            return DynValue.NewString(a[0].String + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
        });
        Data("get_integer", 1, (_, a) =>
        {
            var value = NsPath.Get(Document, a[0].String);
            if (value == null) return DynValue.Nil;
            if (value is not (byte or sbyte or ushort or short or uint or int or ulong or long))
                throw new FrontEditException("目标不是整数标量");
            return DynValue.NewString(Convert.ToString(value, CultureInfo.InvariantCulture));
        });
        Data("set_integer", 3, (_, a) =>
        {
            RequireWrite();
            string type = ArgString(a, 1);
            if (type != "u64" && type != "i64") throw new FrontEditException("精确整数接口的类型必须是 u64 或 i64");
            NsPath.Set(Document, a[0].String, type, ArgString(a, 2));
            return DynValue.Nil;
        });
        editor.Set("data", DynValue.NewTable(data));

        var clipboard = new Table(_script);
        clipboard.Set("copy", Api("editor.clipboard.copy", (c, a) =>
        {
            if (a.Count != 2 || !NsPath.IsNs(ArgString(a, 1))) throw new FrontEditException("需要剪贴板名称和 /字段号 路径");
            return OnKeep(c, a);
        }));
        clipboard.Set("has", Api("editor.clipboard.has", OnKept));
        clipboard.Set("clear", Api("editor.clipboard.clear", (_, a) =>
        {
            RequireWrite(); _kept.Remove(ArgString(a, 0)); return DynValue.Nil;
        }));
        clipboard.Set("paste", Api("editor.clipboard.paste", (_, a) =>
        {
            RequireWrite(); NumericPath(a);
            string key = ArgString(a, 1);
            if (!_kept.TryGetValue(key, out var kept) || kept.Node == null) throw new FrontEditException("剪贴板为空：" + key);
            var node = kept.Struct ? (BtlNode)FrontEdit.CloneStruct((BtlStruct)kept.Node) : FrontEdit.CloneTable((BtlTable)kept.Node);
            return DynValue.NewNumber(NsPath.Append(Document, a[0].String, node));
        }));
        editor.Set("clipboard", DynValue.NewTable(clipboard));

        var json = new Table(_script);
        string[] names = { "get", "set", "count", "at", "insert", "remove", "vec_count", "vec_at", "vec_append", "vec_remove" };
        foreach (string name in names) json.Set(name, editor.Get("json_" + name));
        _jsonNull = new Table(_script);
        json.Set("null", DynValue.NewTable(_jsonNull));
        json.Set("object", Api("editor.json.object", (_, a) =>
        {
            RequireWrite();
            return JsonHandle(a.Count == 0 ? new JsonObject() : BoxObject(a[0], new HashSet<Table>(), 0));
        }));
        json.Set("array", Api("editor.json.array", (_, a) =>
        {
            RequireWrite();
            return JsonHandle(a.Count == 0 ? new JsonArray() : BoxArray(a[0], new HashSet<Table>(), 0));
        }));
        editor.Set("json", DynValue.NewTable(json));

        var state = new Table(_script);
        state.Set("get", Api("editor.state.get", (_, a) =>
            _state.TryGetPropertyValue(ArgString(a, 0), out var value) ? JsonToLua(value) : DynValue.Nil));
        state.Set("set", Api("editor.state.set", (_, a) =>
        {
            RequireWrite();
            string key = ArgString(a, 0);
            if (a[1].IsNil()) _state.Remove(key);
            else _state[key] = BoxJson(a[1]);
            return DynValue.Nil;
        }));
        editor.Set("state", DynValue.NewTable(state));
    }

    static void NumericPath(CallbackArguments args)
    {
        if (!NsPath.IsNs(ArgString(args, 0))) throw new FrontEditException("请使用 /字段号 形式的路径");
    }

    DynValue JsonToLua(JsonNode node)
    {
        if (node is JsonValue) return FromJson(node);
        if (node == null) return DynValue.NewTable(_jsonNull);
        var table = new Table(_script);
        if (node is JsonObject obj)
            foreach (var pair in obj) table.Set(pair.Key, JsonToLua(pair.Value));
        else if (node is JsonArray arr)
            for (int i = 0; i < arr.Count; i++) table.Set(i + 1, JsonToLua(arr[i]));
        return DynValue.NewTable(table);
    }
}
