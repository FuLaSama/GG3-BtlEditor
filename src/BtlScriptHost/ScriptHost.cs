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
        public int? UnitIndex;
        public int? FactionIndex;
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
        Script _script;
        readonly Dictionary<string, ActionRec> _actions = new Dictionary<string, ActionRec>(StringComparer.Ordinal);
        readonly Dictionary<string, Kept> _kept = new Dictionary<string, Kept>(StringComparer.Ordinal);
        readonly Dictionary<string, DynValue> _resolvers = new Dictionary<string, DynValue>(StringComparer.Ordinal);
        int _epoch;
        bool _allowWrite;
        readonly Dictionary<BtlVector, (int Count, Dictionary<BtlNode, int> Indices)> _vectorIndices = new();

        int ObjectIndex(BtlVector vector, BtlNode node)
        {
            if (vector == null || node == null) return -1;
            if (_vectorIndices.TryGetValue(vector, out var cached) && cached.Count == vector.V.Count
                && cached.Indices.TryGetValue(node, out int known) && ReferenceEquals(vector.V[known], node))
                return known;
            var indices = new Dictionary<BtlNode, int>();
            for (int i = 0; i < vector.V.Count; i++)
                if (vector.V[i] is BtlNode item) indices.TryAdd(item, i);
            _vectorIndices[vector] = (vector.V.Count, indices);
            return indices.TryGetValue(node, out int index) ? index : -1;
        }

        public BtlFrontDocument Document { get; private set; }
        public int UndoFrames { get; private set; }

        static ScriptHost()
        {
            UserData.RegisterType<ScriptHandle>();
        }

        public ScriptHost(ScriptLimits limits = null)
        {
            Limits = limits ?? new ScriptLimits();
            Limits.Validate();
            CreateRuntime();
        }

        void CreateRuntime()
        {
            _script = new Script(CoreModules.Basic | CoreModules.Table | CoreModules.TableIterators | CoreModules.String | CoreModules.Math | CoreModules.ErrorHandling | CoreModules.Metatables);
            var editor = new Table(_script);
            editor.Set("action", DynValue.NewCallback(OnAction));
            editor.Set("resolve", DynValue.NewCallback(OnResolve));
            editor.Set("root", DynValue.NewCallback(OnRoot));
            editor.Set("field", DynValue.NewCallback(OnField));
            editor.Set("get", DynValue.NewCallback(OnGet));
            editor.Set("set", DynValue.NewCallback(OnSet));
            editor.Set("ensure", DynValue.NewCallback(OnEnsure));
            editor.Set("append", DynValue.NewCallback(OnAppend));
            editor.Set("insert", DynValue.NewCallback(OnInsert));
            editor.Set("remove", DynValue.NewCallback(OnRemove));
            editor.Set("move", DynValue.NewCallback(OnMove));
            editor.Set("count", DynValue.NewCallback(OnCount));
            editor.Set("at", DynValue.NewCallback(OnAt));
            editor.Set("member", DynValue.NewCallback(OnMember));
            editor.Set("set_member", DynValue.NewCallback(OnSetMember));
            editor.Set("slot", DynValue.NewCallback(OnSlot));
            editor.Set("insert_member", DynValue.NewCallback(OnInsertMember));
            editor.Set("remove_member", DynValue.NewCallback(OnRemoveMember));
            editor.Set("member_type", DynValue.NewCallback(OnMemberType));
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
            InstallApi(editor);
            _script.Globals.Set("editor", DynValue.NewTable(editor));
            CreateInvocationRunner();
        }

        public void Load(BtlFrontDocument doc)
        {
            if (_readDepth != 0 && !ReferenceEquals(Document, doc))
                throw new InvalidOperationException("读取期间不能更换文档");
            if (!ReferenceEquals(Document, doc)) _epoch++;
            Document = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public void Execute(string code, string sourceName)
        {
            EnsureIdle();
            if (_readDepth != 0) throw new FrontEditException("读取期间不能加载脚本");
            var source = (Code: code ?? "", Name: sourceName ?? "script.lua");
            try
            {
                ExecuteSource(source.Code, source.Name);
                _sources.Add(source);
            }
            catch (Exception ex)
            {
                RecoverRuntime();
                throw Describe("加载 " + source.Name, ex);
            }
        }

        public object CallGet(string name, ScriptArgs args = null)
        {
            if (!_actions.TryGetValue(name, out var act) || act.Get == null)
                throw new InvalidOperationException("没有 get：" + name);
            object result = null;
            using var read = BeginRead();
            ReadOnly("读取 " + name, () =>
            {
                var ret = Invoke(act.Get, MakeCtx(args));
                result = FromDyn(ret);
            });
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
                try { Invoke(fn, MakeCtx(args)); FlushGameOperation(); }
                finally { _allowWrite = false; }
            }, commitUndo: true, operation: "动作 " + name);
        }

        public bool CanRun(string name) =>
            _actions.TryGetValue(name, out var run) && (run.Fn != null || run.Set != null);

        public bool CanGet(string name) =>
            _actions.TryGetValue(name, out var get) && get.Get != null;

        public int Count(string path) =>
            NsPath.IsNs(path) ? NsPath.Count(Document, path) : FrontPath.Count(Document, path);

        public object ReadBind(string sectionBind, string fieldBind, int? index, int? cell, string widget, int? shift, int? width) =>
            LayoutRead.Value(Document, sectionBind, fieldBind, index, cell, widget, shift, width);

        public int? ResolveIndex(string name, int? cellIndex)
        {
            if (string.IsNullOrEmpty(name) || Document?.Root == null) return null;
            int? index = null;
            using var read = BeginRead();
            ReadOnly("定位 " + name, () =>
            {
                var ret = ResolveObject(new ScriptArgs { ResolveName = name, CellIndex = cellIndex });
                if (ret.Type != DataType.Number) return;
                double n = ret.Number;
                if (!double.IsFinite(n) || n < 0 || n > int.MaxValue || n != Math.Truncate(n))
                    throw new FrontEditException("定位器必须返回非负整数下标或 nil");
                index = (int)n;
            });
            return index;
        }

        public object GetAtField(string vectorPath, int index, string field)
        {
            object item = FrontPath.At(Document, vectorPath, index, out string elemType);
            if (item is not BtlTable tbl) return null;
            return FrontPath.GetScalar(tbl, elemType, field);
        }

        public object BindGet(string path) => FrontPath.GetScalar(Document, path);

        public BtlNode ReadNode(string path) => NsPath.NodeAt(Document, path);

        public void BindSet(string path, object value)
        {
            Transact(() => FrontPath.SetScalar(Document, path, value), commitUndo: true);
        }

        void Transact(Action act, bool commitUndo, string operation = "绑定写入")
        {
            EnsureIdle();
            if (_readDepth != 0) throw new FrontEditException("读取期间不能提交修改");
            if (Document?.Root == null)
                throw new FrontEditException("路径无法定位");
            var before = BtlFrontJson.CloneDocument(Document);
            SnapshotCount++;
            var keptBefore = new Dictionary<string, Kept>(_kept, StringComparer.Ordinal);
            var stateBefore = (System.Text.Json.Nodes.JsonObject)_state.DeepClone();
            StartBudget();
            _epoch++;
            ResetGameOperation();
            try
            {
                act();
                FlushGameOperation();
                if (commitUndo && !BtlFrontJson.ContentEquals(Document, before))
                    UndoFrames++;
            }
            catch (Exception ex)
            {
                Document.Root = before.Root;
                Document.FormatVersion = before.FormatVersion;
                _kept.Clear();
                foreach (var pair in keptBefore) _kept.Add(pair.Key, pair.Value);
                _state = stateBefore;
                _allowWrite = false;
                RecoverRuntime();
                throw Describe(operation, ex);
            }
            finally
            {
                _epoch++;
                _allowWrite = false;
                _running = false;
                ResetGameOperation();
            }
        }

        DynValue MakeCtx(ScriptArgs args, bool bindObject = true)
        {
            var ctx = new Table(_script);
            ctx.Set("value", ToDyn(args?.Value));
            int? row = args?.Index ?? args?.ObjectIndex;
            ctx.Set("index", row == null ? DynValue.Nil : DynValue.NewNumber(row.Value));
            ctx.Set("cell_index", args?.CellIndex == null ? DynValue.Nil : DynValue.NewNumber(args.CellIndex.Value));
            ctx.Set("unit_index", args?.UnitIndex == null ? DynValue.Nil : DynValue.NewNumber(args.UnitIndex.Value));
            ctx.Set("faction_index", args?.FactionIndex == null ? DynValue.Nil : DynValue.NewNumber(args.FactionIndex.Value));
            ctx.Set("item", ToDyn(args?.Item));
            var resolved = bindObject ? ResolveObject(args) : DynValue.Nil;
            if (resolved.Type == DataType.String)
                ctx.Set("path", resolved);
            else if (resolved.Type == DataType.Number)
                ctx.Set("index", resolved);
            else
            {
                ctx.Set("object", resolved);
                ctx.Set("path", RowPath(args));
            }
            var input = new Table(_script);
            if (args?.Input != null)
            {
                foreach (var kv in args.Input)
                    input.Set(kv.Key, ToDyn(kv.Value));
            }
            ctx.Set("input", DynValue.NewTable(input));
            ctx.Set("cache", DynValue.NewTable(_readCache ?? new Table(_script)));
            return DynValue.NewTable(ctx);
        }

        static DynValue RowPath(ScriptArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.ObjectPath) || args.ObjectIndex == null)
                return DynValue.Nil;
            return DynValue.NewString(FrontPath.IndexedRow(args.ObjectPath, args.ObjectIndex.Value));
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
                var ret = Invoke(fn, MakeCtx(args, bindObject: false));
                if (ret.Type == DataType.UserData || ret.Type == DataType.String || ret.Type == DataType.Number) return ret;
                return DynValue.Nil;
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
            if (h.Owner != null && (h.Table != null || h.Struct != null) && ObjectIndex(h.Owner, (BtlNode)h.Table ?? h.Struct) < 0)
                throw new FrontEditException("对象已从集合移除");
            return h;
        }

        DynValue OnAction(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireRegistration();
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
            RequireRegistration();
            string name = ArgString(args, 0);
            if (args[1].Type != DataType.Function)
                throw new FrontEditException("resolve 需要函数");
            _resolvers[name] = args[1];
            return DynValue.Nil;
        }

        DynValue OnRoot(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (Document?.Root == null) return DynValue.Nil;
            return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = Document.Root, TypeName = BtlCore.Fb.SoftSchema.RootType });
        }

        DynValue OnField(ScriptExecutionContext ctx, CallbackArguments args)
        {
            var h = Unwrap(args[0]);
            if (h.Table == null) return DynValue.Nil;
            int id = AsIndex(args[1]);
            if (!h.Table.F.TryGetValue(id, out var node) || node == null) return DynValue.Nil;
            if (node is BtlScalar sc) return ToDyn(sc.V);
            if (node is BtlTable child)
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = child });
            if (node is BtlStruct st)
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st });
            return DynValue.Nil;
        }

        DynValue OnGet(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (args[0].Type == DataType.String && NsPath.IsNs(args[0].String))
                return ToDyn(NsPath.Get(Document, args[0].String));
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
            if (args[0].Type == DataType.String && NsPath.IsNs(args[0].String))
            {
                object value = args.Count >= 3 ? FromDyn(args[2]) : null;
                NsPath.Set(Document, args[0].String, ArgString(args, 1), value);
                return DynValue.Nil;
            }
            if (args.Count >= 4 && args[0].Type == DataType.UserData && args[1].Type == DataType.Number)
            {
                var h = Unwrap(args[0]);
                if (h.Table == null) throw new FrontEditException("路径无法定位");
                string type = FrontEdit.ScalarType(ArgString(args, 2));
                object value = FromDyn(args[3]);
                if (value != null) value = FrontPath.Coerce(type, value);
                FrontEdit.SetScalar(h.Table, AsIndex(args[1]), type, value);
                return DynValue.Nil;
            }
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
            if (args[0].Type == DataType.String && NsPath.IsNs(args[0].String))
            {
                string path = args[0].String;
                if (args.Count >= 2 && args[1].Type == DataType.Table)
                    NsPath.Ensure(Document, path, null, ReadStrings(args[1]));
                else if (args.Count >= 2 && args[1].Type == DataType.String && args[1].String == "struct")
                    NsPath.Ensure(Document, path, "struct", args.Count >= 3 ? ReadStrings(args[2]) : null);
                else if (args.Count >= 2 && args[1].Type == DataType.String)
                    NsPath.Ensure(Document, path, args[1].String, null);
                else
                    NsPath.Ensure(Document, path, null, null);
                return DynValue.Nil;
            }
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
            try
            {
                RequireWrite();
                _vectorIndices.Clear();
                string path = ArgString(args, 0);
                if (NsPath.IsNs(path))
                {
                    BtlNode node = null;
                    if (args.Count >= 2 && args[1].Type == DataType.String && _kept.TryGetValue(args[1].String, out var kept) && kept?.Node != null)
                    {
                        node = kept.Struct
                            ? FrontEdit.CloneStruct((BtlStruct)kept.Node)
                            : FrontEdit.CloneTable((BtlTable)kept.Node);
                    }
                    else if (args.Count >= 2 && args[1].Type == DataType.UserData)
                        node = NodeOf(Unwrap(args[1]));
                    int at = NsPath.Append(Document, path, node);
                    return DynValue.NewNumber(at);
                }
                if (args.Count >= 2 && args[1].Type == DataType.UserData)
                    return Attach(path, -1, Unwrap(args[1]));
                return WrapNew(path, FrontPath.AppendEmpty(Document, path));
            }
            finally { _vectorIndices.Clear(); }
        }

        DynValue OnInsert(ScriptExecutionContext ctx, CallbackArguments args)
        {
            try
            {
                RequireWrite();
                _vectorIndices.Clear();
                string path = ArgString(args, 0);
                int index = AsIndex(args[1]);
                if (NsPath.IsNs(path))
                {
                    var node = args.Count >= 3 && args[2].Type == DataType.UserData ? NodeOf(Unwrap(args[2])) : null;
                    if (node == null) throw new FrontEditException("路径无法定位");
                    NsPath.Insert(Document, path, index, node);
                    return DynValue.Nil;
                }
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
            finally { _vectorIndices.Clear(); }
        }

        DynValue OnMove(ScriptExecutionContext ctx, CallbackArguments args)
        {
            try
            {
                RequireWrite();
                _vectorIndices.Clear();
                NsPath.Move(Document, ArgString(args, 0), AsIndex(args[1]), AsIndex(args[2]));
                return DynValue.Nil;
            }
            finally { _vectorIndices.Clear(); }
        }

        DynValue OnRemove(ScriptExecutionContext ctx, CallbackArguments args)
        {
            try
            {
                RequireWrite();
                _vectorIndices.Clear();
                string path = ArgString(args, 0);
                if (NsPath.IsNs(path))
                {
                    int? index = args.Count >= 2 && args[1].Type == DataType.Number ? AsIndex(args[1]) : null;
                    NsPath.Remove(Document, path, index);
                    return DynValue.Nil;
                }
                FrontPath.Remove(Document, path, AsIndex(args[1]));
                return DynValue.Nil;
            }
            finally { _vectorIndices.Clear(); }
        }

        DynValue OnCount(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string path = ArgString(args, 0);
            int n = NsPath.IsNs(path) ? NsPath.Count(Document, path) : FrontPath.Count(Document, path);
            return DynValue.NewNumber(n);
        }

        DynValue OnAt(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string path = ArgString(args, 0);
            int index = AsIndex(args[1]);
            if (NsPath.IsNs(path))
            {
                var node = NsPath.NodeAt(Document, path + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
                if (node is BtlTable row)
                    return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = row });
                if (node is BtlStruct elem)
                    return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = elem });
                return ToDyn(node is BtlScalar sc ? sc.V : null);
            }
            object item = FrontPath.At(Document, path, index, out string elemType, out var vector);
            if (item is BtlTable tbl)
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = tbl, TypeName = elemType, Owner = vector });
            if (item is BtlStruct st)
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st, TypeName = elemType, Owner = vector });
            return ToDyn(item);
        }

        DynValue OnMember(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (args.Count >= 2 && args[0].Type == DataType.UserData && args[1].Type == DataType.Number)
            {
                var h = Unwrap(args[0]);
                if (h.Struct == null) return DynValue.Nil;
                int index = AsIndex(args[1]);
                if (index < 0 || index >= h.Struct.V.Count) return DynValue.NewNumber(0);
                return ToDyn(h.Struct.V[index]);
            }
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

        DynValue OnSlot(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var h = Unwrap(args[0]);
            if (h.Table == null) throw new FrontEditException("路径无法定位");
            int id = AsIndex(args[1]);
            string kind = ArgString(args, 2);
            if (kind == "table")
            {
                var child = FrontEdit.EnsureTable(h.Table, id);
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Table = child });
            }
            if (kind == "struct")
            {
                var st = FrontEdit.EnsureStruct(h.Table, id, ReadStrings(args[3]));
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st });
            }
            if (kind == "vector")
            {
                string elem = ArgString(args, 3);
                if (elem == "struct")
                    FrontEdit.EnsureStructVector(h.Table, id, ReadStrings(args[4]));
                else if (elem == "table" || elem == "string")
                    FrontEdit.EnsureVec(h.Table, id, elem);
                else
                    FrontEdit.EnsureVec(h.Table, id, FrontEdit.ScalarType(elem));
                return DynValue.Nil;
            }
            throw new FrontEditException("路径无法定位");
        }

        DynValue OnInsertMember(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var h = Unwrap(args[0]);
            if (h.Struct == null) throw new FrontEditException("路径无法定位");
            object value = args.Count >= 4 ? FromDyn(args[3]) : null;
            FrontEdit.InsertMember(h.Struct, AsIndex(args[1]), ArgString(args, 2), value, Shared(h));
            return DynValue.Nil;
        }

        DynValue OnRemoveMember(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var h = Unwrap(args[0]);
            if (h.Struct == null) throw new FrontEditException("路径无法定位");
            FrontEdit.RemoveMember(h.Struct, AsIndex(args[1]), Shared(h));
            return DynValue.Nil;
        }

        DynValue OnMemberType(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var h = Unwrap(args[0]);
            if (h.Struct == null) throw new FrontEditException("路径无法定位");
            FrontEdit.SetMemberType(h.Struct, AsIndex(args[1]), ArgString(args, 2), Shared(h));
            return DynValue.Nil;
        }

        static BtlVector Shared(ScriptHandle h)
        {
            if (h.Owner == null) return null;
            if (h.Owner.Elem == "struct" || h.Owner.StructLayout.Count > 0) return h.Owner;
            return null;
        }

        static List<string> ReadStrings(DynValue v)
        {
            var list = new List<string>();
            foreach (var item in ReadArray(v))
            {
                if (item is not string s) throw new FrontEditException("路径无法定位");
                list.Add(s);
            }
            return list;
        }

        DynValue OnSetMember(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            if (args[0].Type == DataType.UserData && args.Count >= 3 && args[1].Type == DataType.Number)
            {
                var h = Unwrap(args[0]);
                if (h.Struct == null) throw new FrontEditException("路径无法定位");
                int index = AsIndex(args[1]);
                if (index < 0 || index >= h.Struct.Layout.Count)
                    throw new FrontEditException("下标越界");
                object value = FromDyn(args[2]);
                string t = h.Struct.Layout[index];
                FrontEdit.SetMember(h.Struct, index, value == null ? null : FrontPath.Coerce(t, value));
                return DynValue.Nil;
            }
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
            try
            {
                RequireWrite();
                _vectorIndices.Clear();
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
            finally { _vectorIndices.Clear(); }
        }

        DynValue OnStruct(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            if (args[0].Type == DataType.Table)
            {
                var laid = FrontEdit.NewStruct(ReadStrings(args[0]));
                return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = laid });
            }
            string name = ArgString(args, 0);
            if (!BtlCore.Fb.SoftSchema.TryStructMembers(name, out _))
                throw new FrontEditException("路径无法定位");
            var st = FrontEdit.StructFromFbs(name);
            return UserData.Create(new ScriptHandle { Epoch = _epoch, Struct = st, TypeName = name });
        }

        DynValue OnFill(ScriptExecutionContext ctx, CallbackArguments args)
        {
            try
            {
                RequireWrite();
                _vectorIndices.Clear();
                if (args[0].Type == DataType.String && NsPath.IsNs(args[0].String))
                {
                    NsPath.Fill(Document, args[0].String, ArgString(args, 1), ReadArray(args[2]));
                    return DynValue.Nil;
                }
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
            finally { _vectorIndices.Clear(); }
        }

        DynValue OnKeep(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            string name = ArgString(args, 0);
            if (args.Count >= 2 && args[1].Type == DataType.String && NsPath.IsNs(args[1].String))
            {
                var node = NsPath.NodeAt(Document, args[1].String);
                if (node is BtlStruct st)
                    _kept[name] = new Kept { Struct = true, Node = FrontEdit.CloneStruct(st) };
                else if (node is BtlTable tbl)
                    _kept[name] = new Kept { Node = FrontEdit.CloneTable(tbl) };
                else
                    throw new FrontEditException("路径无法定位");
                return DynValue.Nil;
            }
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
            if (!_kept.TryGetValue(name, out var kept) || kept?.Node == null) return DynValue.False;
            return DynValue.True;
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
            if (v.Table.Pairs.Any(p => p.Key.Type != DataType.Number || p.Key.Number < 1
                || p.Key.Number > n || p.Key.Number != Math.Truncate(p.Key.Number)))
                throw new FrontEditException("数组必须使用连续的 1 起下标，不能混合命名键");
            for (int i = 1; i <= n; i++)
            {
                if (v.Table.Get(i).IsNil()) throw new FrontEditException("数组不能有空洞，请明确填入 0");
                list.Add(FromDyn(v.Table.Get(i)));
            }
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
            try
            {
                var node = NodeOf(handle);
                var vec = VectorOf(path);
                int at = ObjectIndex(handle.Owner, node);
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
            finally { _vectorIndices.Clear(); }
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
            if (value is long l && (l > MaxExactInteger || l < -MaxExactInteger)
                || value is ulong u && u > (ulong)MaxExactInteger)
                throw new FrontEditException("整数超过 Lua 精确范围，请用 editor.data.get_integer 读取十进制字符串");
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
