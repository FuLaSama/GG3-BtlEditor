/*
 * ScriptHost.cs
 *
 * 无界面 Lua 宿主。测试和以后的编辑器提交共用。
 * 一次 Run 或 BindSet 是一帧：先拍快照，失败则恢复，成功且树有变化才增加 UndoFrames。
 * get 里调用写入会失败，文档不动。
 */
using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting
{
    public sealed class ScriptArgs
    {
        public object Value;
        public int? Index;
        /// <summary>选中格子，从 0 起。未选中为 null。</summary>
        public int? CellIndex;
        /// <summary>贴图栏放下的项。键如 t、sea、terrain_id、variant、dx、dy、layer。</summary>
        public Dictionary<string, object> Item;
        /// <summary>列表行所在的向量路径，例如 Root.stage_metadata.targets。</summary>
        public string ObjectPath;
        public int? ObjectIndex;
        public Dictionary<string, object> Input;
        /// <summary>按这个名字调用 editor.resolve，用来填 ctx.object。列表行优先用 ObjectPath。</summary>
        public string ResolveName;
    }

    public sealed class ScriptHandle
    {
        internal int Epoch;
        internal BtlTable Table;
        internal BtlStruct Struct;
        internal System.Text.Json.Nodes.JsonNode Json;
        internal string TypeName;
        internal BtlVector Owner;
    }

    public sealed partial class ScriptHost
    {
        readonly Script _script;
        readonly Dictionary<string, ActionRec> _actions = new Dictionary<string, ActionRec>(StringComparer.Ordinal);
        readonly Dictionary<string, Kept> _kept = new Dictionary<string, Kept>(StringComparer.Ordinal);
        readonly Dictionary<string, DynValue> _resolvers = new Dictionary<string, DynValue>(StringComparer.Ordinal);
        int _epoch;
        bool _allowWrite;

        public BtlFrontDocument Document { get; private set; }
        public int UndoFrames { get; private set; }

        static ScriptHost()
        {
            UserData.RegisterType<ScriptHandle>();
        }

        public ScriptHost()
        {
            _script = new Script(CoreModules.Basic | CoreModules.Table | CoreModules.String | CoreModules.Math | CoreModules.ErrorHandling | CoreModules.Metatables);
            var editor = new Table(_script);
            editor.Set("action", DynValue.NewCallback(OnAction));
            editor.Set("resolve", DynValue.NewCallback(OnResolve));
            editor.Set("get", DynValue.NewCallback(OnGet));
            editor.Set("set", DynValue.NewCallback(OnSet));
            editor.Set("ensure", DynValue.NewCallback(OnEnsure));
            editor.Set("append", DynValue.NewCallback(OnAppend));
            editor.Set("insert", DynValue.NewCallback(OnInsert));
            editor.Set("remove", DynValue.NewCallback(OnRemove));
            editor.Set("count", DynValue.NewCallback(OnCount));
            editor.Set("at", DynValue.NewCallback(OnAt));
            editor.Set("member", DynValue.NewCallback(OnMember));
            editor.Set("set_member", DynValue.NewCallback(OnSetMember));
            editor.Set("set_at", DynValue.NewCallback(OnSetAt));
            editor.Set("struct", DynValue.NewCallback(OnStruct));
            editor.Set("clone", DynValue.NewCallback(OnClone));
            editor.Set("fill", DynValue.NewCallback(OnFill));
            editor.Set("keep", DynValue.NewCallback(OnKeep));
            editor.Set("kept", DynValue.NewCallback(OnKept));
            editor.Set("remap_cell", DynValue.NewCallback(OnRemapCell));
            editor.Set("json_get", DynValue.NewCallback(OnJsonGet));
            editor.Set("json_set", DynValue.NewCallback(OnJsonSet));
            editor.Set("json_count", DynValue.NewCallback(OnJsonCount));
            editor.Set("json_at", DynValue.NewCallback(OnJsonAt));
            editor.Set("json_insert", DynValue.NewCallback(OnJsonInsert));
            editor.Set("json_remove", DynValue.NewCallback(OnJsonRemove));
            editor.Set("json_vec_count", DynValue.NewCallback(OnJsonVecCount));
            editor.Set("json_vec_at", DynValue.NewCallback(OnJsonVecAt));
            editor.Set("json_vec_append", DynValue.NewCallback(OnJsonVecAppend));
            editor.Set("json_vec_remove", DynValue.NewCallback(OnJsonVecRemove));
            editor.Set("band", DynValue.NewCallback((c, a) => Bit2(a, (x, y) => x & y)));
            editor.Set("bor", DynValue.NewCallback((c, a) => Bit2(a, (x, y) => x | y)));
            editor.Set("bxor", DynValue.NewCallback((c, a) => Bit2(a, (x, y) => x ^ y)));
            editor.Set("lshift", DynValue.NewCallback(OnLShift));
            editor.Set("rshift", DynValue.NewCallback(OnRShift));
            _script.Globals.Set("editor", DynValue.NewTable(editor));
        }

        public void Load(BtlFrontDocument doc)
        {
            Document = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public void Execute(string code, string sourceName)
        {
            _script.DoString(code ?? "", null, sourceName ?? "script.lua");
        }

        public object CallGet(string name)
        {
            if (!_actions.TryGetValue(name, out var act) || act.Get == null)
                throw new InvalidOperationException("没有 get：" + name);
            object result = null;
            Transact(() =>
            {
                _allowWrite = false;
                var ret = _script.Call(act.Get, MakeCtx(null));
                result = FromDyn(ret);
            }, commitUndo: false);
            return result;
        }

        public void Run(string name, ScriptArgs args)
        {
            if (!_actions.TryGetValue(name, out var act))
                throw new InvalidOperationException("未登记：" + name);
            var fn = act.Fn ?? act.Set;
            if (fn == null)
                throw new InvalidOperationException("不能写入：" + name);
            Transact(() =>
            {
                _allowWrite = true;
                try { _script.Call(fn, MakeCtx(args)); }
                finally { _allowWrite = false; }
            }, commitUndo: true);
        }

        public bool CanRun(string name) =>
            _actions.TryGetValue(name, out var run) && (run.Fn != null || run.Set != null);

        public bool CanGet(string name) =>
            _actions.TryGetValue(name, out var get) && get.Get != null;

        public int Count(string path) => FrontPath.Count(Document, path);

        public object GetAtField(string vectorPath, int index, string field)
        {
            object item = FrontPath.At(Document, vectorPath, index, out string elemType);
            if (item is not BtlTable tbl) return null;
            return FrontPath.GetScalar(tbl, elemType, field);
        }

        public object BindGet(string path) => FrontPath.GetScalar(Document, path);

        public void BindSet(string path, object value)
        {
            Transact(() => FrontPath.SetScalar(Document, path, value), commitUndo: true);
        }

        void Transact(Action act, bool commitUndo)
        {
            if (Document?.Root == null)
                throw new FrontEditException("路径无法定位");
            string before = BtlFrontJson.Serialize(Document);
            _epoch++;
            try
            {
                act();
                if (commitUndo && BtlFrontJson.Serialize(Document) != before)
                    UndoFrames++;
            }
            catch
            {
                var restored = BtlFrontJson.Parse(before);
                Document.Root = restored.Root;
                Document.FormatVersion = restored.FormatVersion;
                throw;
            }
            finally
            {
                _epoch++;
                _allowWrite = false;
            }
        }

        DynValue MakeCtx(ScriptArgs args, bool bindObject = true)
        {
            var ctx = new Table(_script);
            ctx.Set("value", ToDyn(args?.Value));
            ctx.Set("index", args?.Index == null ? DynValue.Nil : DynValue.NewNumber(args.Index.Value));
            ctx.Set("cell_index", args?.CellIndex == null ? DynValue.Nil : DynValue.NewNumber(args.CellIndex.Value));
            ctx.Set("item", ToDyn(args?.Item));
            ctx.Set("object", bindObject ? ResolveObject(args) : DynValue.Nil);
            var input = new Table(_script);
            if (args?.Input != null)
            {
                foreach (var kv in args.Input)
                    input.Set(kv.Key, ToDyn(kv.Value));
            }
            ctx.Set("input", DynValue.NewTable(input));
            return DynValue.NewTable(ctx);
        }

        DynValue ResolveObject(ScriptArgs args)
        {
            if (args != null && !string.IsNullOrEmpty(args.ObjectPath) && args.ObjectIndex != null)
            {
                object item = FrontPath.At(Document, args.ObjectPath, args.ObjectIndex.Value, out string elemType);
                if (item is not BtlTable tbl)
                    return ToDyn(item);
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = tbl, TypeName = elemType });
            }
            if (args == null || string.IsNullOrEmpty(args.ResolveName) || !_resolvers.TryGetValue(args.ResolveName, out var fn))
                return DynValue.Nil;
            bool prev = _allowWrite;
            _allowWrite = false;
            try
            {
                var ret = _script.Call(fn, MakeCtx(args, bindObject: false));
                return ret.Type == DataType.UserData ? ret : DynValue.Nil;
            }
            finally { _allowWrite = prev; }
        }

        void RequireWrite()
        {
            if (!_allowWrite)
                throw new FrontEditException("get 里不能写入");
        }

        ScriptHandle Unwrap(DynValue v)
        {
            var h = v.Type == DataType.UserData ? v.UserData?.Object as ScriptHandle : null;
            if (h == null || h.Epoch != _epoch || (h.Table == null && h.Struct == null && h.Json == null))
                throw new FrontEditException("句柄已失效");
            return h;
        }

        DynValue OnAction(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string name = ArgString(args, 0);
            var body = args[1];
            var rec = new ActionRec();
            if (body.Type == DataType.Function)
                rec.Fn = body;
            else if (body.Type == DataType.Table)
            {
                var get = body.Table.Get("get");
                var set = body.Table.Get("set");
                if (get.Type == DataType.Function) rec.Get = get;
                if (set.Type == DataType.Function) rec.Set = set;
            }
            else
                throw new FrontEditException("action 需要函数或 get/set");
            _actions[name] = rec;
            return DynValue.Nil;
        }

        DynValue OnResolve(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string name = ArgString(args, 0);
            if (args[1].Type != DataType.Function)
                throw new FrontEditException("resolve 需要函数");
            _resolvers[name] = args[1];
            return DynValue.Nil;
        }

        DynValue OnGet(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (args.Count >= 2 && args[0].Type == DataType.UserData)
            {
                var h = Unwrap(args[0]);
                return ToDyn(FrontPath.GetScalar(h.Table, h.TypeName, ArgString(args, 1)));
            }
            return ToDyn(FrontPath.GetScalar(Document, ArgString(args, 0)));
        }

        DynValue OnSet(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            if (args.Count >= 3 && args[0].Type == DataType.UserData)
            {
                var h = Unwrap(args[0]);
                FrontPath.SetScalar(h.Table, h.TypeName, ArgString(args, 1), FromDyn(args[2]));
                return DynValue.Nil;
            }
            FrontPath.SetScalar(Document, ArgString(args, 0), FromDyn(args[1]));
            return DynValue.Nil;
        }

        DynValue OnEnsure(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            if (args.Count >= 2 && args[0].Type == DataType.UserData)
            {
                var h = Unwrap(args[0]);
                if (h.Table == null) throw new FrontEditException("路径无法定位");
                FrontPath.Ensure(h.Table, h.TypeName, ArgString(args, 1));
                return DynValue.Nil;
            }
            FrontPath.Ensure(Document, ArgString(args, 0));
            return DynValue.Nil;
        }

        DynValue OnAppend(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            string path = ArgString(args, 0);
            if (args.Count >= 2 && args[1].Type == DataType.UserData)
                return Attach(path, -1, Unwrap(args[1]));
            return WrapNew(path, FrontPath.AppendEmpty(Document, path));
        }

        DynValue OnInsert(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            string path = ArgString(args, 0);
            int index = AsIndex(args[1]);
            if (args.Count >= 3 && args[2].Type == DataType.UserData)
                return Attach(path, index, Unwrap(args[2]));
            var vec = VectorOf(path);
            int end = vec.V.Count;
            if (index < 0 || index > end)
                throw new FrontEditException("下标越界");
            object created = FrontPath.AppendEmpty(Document, path);
            if (end != index)
            {
                FrontEdit.RemoveAt(vec, end);
                if (end < index) index--;
                FrontEdit.Insert(vec, index, created);
            }
            return WrapNew(path, created);
        }

        DynValue OnRemove(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            FrontPath.Remove(Document, ArgString(args, 0), AsIndex(args[1]));
            return DynValue.Nil;
        }

        DynValue OnCount(ScriptExecutionContext ctx, CallbackArguments args)
        {
            return DynValue.NewNumber(FrontPath.Count(Document, ArgString(args, 0)));
        }

        DynValue OnAt(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string path = ArgString(args, 0);
            object item = FrontPath.At(Document, path, AsIndex(args[1]), out string elemType, out var vector);
            if (item is BtlTable tbl)
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = tbl, TypeName = elemType, Owner = vector });
            if (item is BtlStruct st)
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st, TypeName = elemType, Owner = vector });
            return ToDyn(item);
        }

        DynValue OnMember(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (args.Count >= 2 && args[0].Type == DataType.UserData)
            {
                var h = Unwrap(args[0]);
                string path = ArgString(args, 1);
                if (h.Table != null)
                    return ToDyn(FrontPath.GetMemberOnTable(h.Table, h.TypeName, path));
                if (h.Struct == null) throw new FrontEditException("路径无法定位");
                return ToDyn(FrontPath.ReadStructMember(h.Struct, h.TypeName, path));
            }
            return ToDyn(FrontPath.GetMember(Document, ArgString(args, 0)));
        }

        DynValue OnSetMember(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            if (args[0].Type == DataType.UserData)
            {
                var h = Unwrap(args[0]);
                string path = ArgString(args, 1);
                object value = FromDyn(args[2]);
                if (h.Table != null)
                    FrontPath.SetMemberOnTable(h.Table, h.TypeName, path, value);
                else if (h.Struct != null)
                    FrontPath.SetStructMember(h.Struct, h.TypeName, path, value);
                else
                    throw new FrontEditException("路径无法定位");
                return DynValue.Nil;
            }
            FrontPath.SetMember(Document, ArgString(args, 0), FromDyn(args[1]));
            return DynValue.Nil;
        }

        DynValue OnSetAt(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            string path = ArgString(args, 0);
            int index = AsIndex(args[1]);
            if (args[2].Type == DataType.UserData)
            {
                var h = Unwrap(args[2]);
                FrontPath.SetAtNode(Document, path, index, NodeOf(h));
                h.Owner = VectorOf(path);
                return DynValue.Nil;
            }
            FrontPath.SetAt(Document, path, index, FromDyn(args[2]));
            return DynValue.Nil;
        }

        DynValue OnStruct(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            string name = ArgString(args, 0);
            if (!BtlCore.Fb.SoftSchema.TryStructMembers(name, out _))
                throw new FrontEditException("路径无法定位");
            var st = FrontEdit.StructFromFbs(name);
            return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st, TypeName = name });
        }

        DynValue OnFill(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var values = ReadArray(args[args.Count - 1]);
            if (args[0].Type == DataType.UserData)
            {
                var h = Unwrap(args[0]);
                if (h.Table == null) throw new FrontEditException("路径无法定位");
                FrontPath.Fill(h.Table, h.TypeName, ArgString(args, 1), values);
                return DynValue.Nil;
            }
            FrontPath.Fill(Document, ArgString(args, 0), values);
            return DynValue.Nil;
        }

        DynValue OnKeep(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            string name = ArgString(args, 0);
            if (args.Count < 2 || args[1].IsNil())
            {
                _kept.Remove(name);
                return DynValue.Nil;
            }
            var h = Unwrap(args[1]);
            if (h.Struct != null)
                _kept[name] = new Kept { Struct = true, TypeName = h.TypeName, Node = FrontEdit.CloneStruct(h.Struct) };
            else if (h.Table != null)
                _kept[name] = new Kept { TypeName = h.TypeName, Node = FrontEdit.CloneTable(h.Table) };
            else
                throw new FrontEditException("路径无法定位");
            return DynValue.Nil;
        }

        DynValue OnKept(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string name = ArgString(args, 0);
            if (!_kept.TryGetValue(name, out var kept) || kept?.Node == null) return DynValue.Nil;
            if (kept.Struct)
            {
                var copy = FrontEdit.CloneStruct((BtlStruct)kept.Node);
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = copy, TypeName = kept.TypeName });
            }
            var table = FrontEdit.CloneTable((BtlTable)kept.Node);
            return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = table, TypeName = kept.TypeName });
        }

        DynValue OnRemapCell(ScriptExecutionContext ctx, CallbackArguments args)
        {
            ushort? mapped = FrontEdit.RemapCellIndex(
                AsIndex(args[0]), AsIndex(args[1]), AsIndex(args[2]), AsIndex(args[3]), AsIndex(args[4]), AsIndex(args[5]));
            return mapped == null ? DynValue.Nil : DynValue.NewNumber(mapped.Value);
        }

        static List<object> ReadArray(DynValue v)
        {
            var list = new List<object>();
            if (v == null || v.IsNil()) return list;
            if (v.Type != DataType.Table) throw new FrontEditException("路径无法定位");
            int n = v.Table.Length;
            for (int i = 1; i <= n; i++)
                list.Add(FromDyn(v.Table.Get(i)));
            return list;
        }

        DynValue OnClone(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var h = Unwrap(args[0]);
            if (h.Struct != null)
            {
                var copy = FrontEdit.CloneStruct(h.Struct);
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = copy, TypeName = h.TypeName });
            }
            var table = FrontEdit.CloneTable(h.Table);
            return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = table, TypeName = h.TypeName });
        }

        DynValue Attach(string path, int index, ScriptHandle handle)
        {
            var node = NodeOf(handle);
            var vec = VectorOf(path);
            int at = handle.Owner?.V?.IndexOf(node) ?? -1;
            if (at >= 0)
            {
                bool same = ReferenceEquals(handle.Owner, vec);
                FrontEdit.RemoveAt(handle.Owner, at);
                if (same && index >= 0 && at < index) index--;
            }
            if (index < 0) index = vec.V.Count;
            FrontEdit.Insert(vec, index, node);
            handle.Owner = vec;
            return UserData.Create(handle);
        }

        DynValue WrapNew(string path, object created)
        {
            var vec = VectorOf(path);
            if (created is BtlTable tbl)
            {
                string elemType = FrontPath.ElementType(Document, path);
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = tbl, TypeName = elemType, Owner = vec });
            }
            if (created is BtlStruct st)
            {
                string elemType = FrontPath.ElementType(Document, path);
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st, TypeName = elemType, Owner = vec });
            }
            return DynValue.Nil;
        }

        BtlVector VectorOf(string path) => FrontPath.GetVector(Document, path, create: true);

        static BtlNode NodeOf(ScriptHandle handle)
        {
            if (handle.Struct != null) return handle.Struct;
            if (handle.Table != null) return handle.Table;
            throw new FrontEditException("句柄已失效");
        }

        static DynValue Bit2(CallbackArguments args, Func<long, long, long> op) =>
            DynValue.NewNumber(op(Bits(args[0]), Bits(args[1])));

        static DynValue OnLShift(ScriptExecutionContext ctx, CallbackArguments args)
        {
            int n = ShiftCount(args[1]);
            return DynValue.NewNumber(unchecked((long)((ulong)Bits(args[0]) << n)));
        }

        static DynValue OnRShift(ScriptExecutionContext ctx, CallbackArguments args)
        {
            int n = ShiftCount(args[1]);
            return DynValue.NewNumber(unchecked((long)((ulong)Bits(args[0]) >> n)));
        }

        static long Bits(DynValue v)
        {
            if (v.Type != DataType.Number)
                throw new FrontEditException("整数超出声明类型的范围");
            double n = v.Number;
            if (double.IsNaN(n) || double.IsInfinity(n) || Math.Abs(n - Math.Truncate(n)) > 1e-9)
                throw new FrontEditException("整数超出声明类型的范围");
            return checked((long)n);
        }

        static int ShiftCount(DynValue v)
        {
            long n = Bits(v);
            if (n < 0 || n > 63) throw new FrontEditException("整数超出声明类型的范围");
            return (int)n;
        }

        static string ArgString(CallbackArguments args, int index)
        {
            var v = args[index];
            if (v.Type != DataType.String || string.IsNullOrEmpty(v.String))
                throw new FrontEditException("路径无法定位");
            return v.String;
        }

        static int AsIndex(DynValue v)
        {
            if (v.Type != DataType.Number)
                throw new FrontEditException("下标越界");
            double n = v.Number;
            if (Math.Abs(n - Math.Truncate(n)) > 1e-9)
                throw new FrontEditException("下标越界");
            return checked((int)n);
        }

        DynValue ToDyn(object value)
        {
            if (value == null) return DynValue.Nil;
            if (value is bool b) return DynValue.NewBoolean(b);
            if (value is string s) return DynValue.NewString(s);
            if (value is double d) return DynValue.NewNumber(d);
            if (value is Dictionary<string, object> dict)
            {
                var table = new Table(_script);
                foreach (var kv in dict)
                    table.Set(kv.Key, ToDyn(kv.Value));
                return DynValue.NewTable(table);
            }
            if (value is object[] arr)
            {
                var table = new Table(_script);
                for (int i = 0; i < arr.Length; i++)
                    table.Set(i + 1, ToDyn(arr[i]));
                return DynValue.NewTable(table);
            }
            try { return DynValue.NewNumber(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)); }
            catch { return DynValue.Nil; }
        }

        static object FromDyn(DynValue v)
        {
            if (v == null || v.IsNil()) return null;
            if (v.Type == DataType.Boolean) return v.Boolean;
            if (v.Type == DataType.Number) return v.Number;
            if (v.Type == DataType.String) return v.String;
            throw new FrontEditException("不能把这个值写入标量");
        }

        sealed class ActionRec
        {
            public DynValue Fn;
            public DynValue Get;
            public DynValue Set;
        }

        sealed class Kept
        {
            public BtlNode Node;
            public string TypeName;
            public bool Struct;
        }
    }
}
