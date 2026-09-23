/*
 * JsonHost.cs
 *
 * 国家行为树的 JSON 接口。向量仍拒绝 insert / remove / fill，
 * 增删整棵树只走 json_vec_*，改节点只走 json_*。
 */
using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting
{
    public sealed partial class ScriptHost
    {
        DynValue OnJsonGet(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (JsonOf(args[0]) is not JsonObject obj) return DynValue.Nil;
            if (!obj.TryGetPropertyValue(ArgString(args, 1), out var node)) return DynValue.Nil;
            return FromJson(node);
        }

        DynValue OnJsonSet(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            if (JsonOf(args[0]) is not JsonObject obj) throw new FrontEditException("路径无法定位");
            string key = ArgString(args, 1);
            if (args.Count < 3 || args[2].IsNil()) obj.Remove(key);
            else obj[key] = BoxJson(args[2]);
            return DynValue.Nil;
        }

        DynValue OnJsonCount(ScriptExecutionContext ctx, CallbackArguments args)
        {
            var node = JsonOf(args[0]);
            if (args.Count >= 2 && args[1].Type == DataType.String)
            {
                if (node is JsonObject obj && obj[ArgString(args, 1)] is JsonArray arr)
                    return DynValue.NewNumber(arr.Count);
                return DynValue.NewNumber(0);
            }
            if (node is JsonArray list) return DynValue.NewNumber(list.Count);
            return DynValue.NewNumber(0);
        }

        DynValue OnJsonAt(ScriptExecutionContext ctx, CallbackArguments args)
        {
            JsonArray arr;
            int index;
            if (args.Count >= 3 && args[1].Type == DataType.String)
            {
                if (JsonOf(args[0]) is not JsonObject obj) throw new FrontEditException("路径无法定位");
                arr = obj[ArgString(args, 1)] as JsonArray ?? throw new FrontEditException("路径无法定位");
                index = AsIndex(args[2]);
            }
            else
            {
                arr = JsonOf(args[0]) as JsonArray ?? throw new FrontEditException("路径无法定位");
                index = AsIndex(args[1]);
            }
            if (index < 0 || index >= arr.Count) throw new FrontEditException("下标越界");
            return FromJson(arr[index]);
        }

        DynValue OnJsonInsert(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            JsonArray arr;
            int index;
            DynValue value;
            if (args[1].Type == DataType.String)
            {
                if (JsonOf(args[0]) is not JsonObject obj) throw new FrontEditException("路径无法定位");
                string key = ArgString(args, 1);
                index = AsIndex(args[2]);
                value = args.Count >= 4 ? args[3] : null;
                if (obj[key] is JsonArray existing) arr = existing;
                else if (obj[key] == null)
                {
                    arr = new JsonArray();
                    obj[key] = arr;
                }
                else throw new FrontEditException("路径无法定位");
            }
            else
            {
                arr = JsonOf(args[0]) as JsonArray ?? throw new FrontEditException("路径无法定位");
                index = AsIndex(args[1]);
                value = args.Count >= 3 ? args[2] : null;
            }
            if (index < 0 || index > arr.Count) throw new FrontEditException("下标越界");
            var created = value == null || value.IsNil() ? new JsonObject() : BoxJson(value);
            arr.Insert(index, created);
            return FromJson(created);
        }

        DynValue OnJsonRemove(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            JsonArray arr;
            int index;
            if (args[1].Type == DataType.String)
            {
                if (JsonOf(args[0]) is not JsonObject obj) throw new FrontEditException("路径无法定位");
                arr = obj[ArgString(args, 1)] as JsonArray ?? throw new FrontEditException("路径无法定位");
                index = AsIndex(args[2]);
            }
            else
            {
                arr = JsonOf(args[0]) as JsonArray ?? throw new FrontEditException("路径无法定位");
                index = AsIndex(args[1]);
            }
            if (index < 0 || index >= arr.Count) throw new FrontEditException("下标越界");
            arr.RemoveAt(index);
            return DynValue.Nil;
        }

        DynValue OnJsonVecCount(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (!TryCountryVec(ArgString(args, 0), create: false, out var vec)) return DynValue.NewNumber(0);
            return DynValue.NewNumber(vec.V.Count);
        }

        DynValue OnJsonVecAt(ScriptExecutionContext ctx, CallbackArguments args)
        {
            var vec = CountryVec(ArgString(args, 0), create: false);
            int index = AsIndex(args[1]);
            if (index < 0 || index >= vec.V.Count) throw new FrontEditException("下标越界");
            if (vec.V[index] is not JsonObject obj) throw new FrontEditException("路径无法定位");
            return JsonHandle(obj);
        }

        DynValue OnJsonVecAppend(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var vec = CountryVec(ArgString(args, 0), create: true);
            var obj = new JsonObject();
            vec.V.Add(obj);
            return JsonHandle(obj);
        }

        DynValue OnJsonVecRemove(ScriptExecutionContext ctx, CallbackArguments args)
        {
            RequireWrite();
            var vec = CountryVec(ArgString(args, 0), create: false);
            int index = AsIndex(args[1]);
            if (index < 0 || index >= vec.V.Count) throw new FrontEditException("下标越界");
            vec.V.RemoveAt(index);
            return DynValue.Nil;
        }

        JsonNode JsonOf(DynValue v)
        {
            var h = Unwrap(v);
            if (h.Json == null) throw new FrontEditException("路径无法定位");
            return h.Json;
        }

        DynValue JsonHandle(JsonNode node) =>
            UserData.Create(new ScriptHandle { Epoch = _epoch, Json = node });

        DynValue FromJson(JsonNode node)
        {
            if (node == null) return DynValue.Nil;
            if (node is JsonValue jv)
            {
                var kind = jv.GetValueKind();
                if (kind is JsonValueKind.True or JsonValueKind.False) return DynValue.NewBoolean(jv.GetValue<bool>());
                if (kind == JsonValueKind.String) return DynValue.NewString(jv.GetValue<string>() ?? "");
                if (kind == JsonValueKind.Number)
                {
                    if (jv.TryGetValue(out int i)) return DynValue.NewNumber(i);
                    if (jv.TryGetValue(out long l)) return DynValue.NewNumber(l);
                    if (jv.TryGetValue(out double d)) return DynValue.NewNumber(d);
                }
                return DynValue.Nil;
            }
            return JsonHandle(node);
        }

        static JsonNode BoxJson(DynValue v)
        {
            if (v.Type == DataType.Boolean) return JsonValue.Create(v.Boolean);
            if (v.Type == DataType.String) return JsonValue.Create(v.String);
            if (v.Type == DataType.Number)
            {
                double n = v.Number;
                if (double.IsNaN(n) || double.IsInfinity(n) || Math.Abs(n - Math.Truncate(n)) > 1e-9
                    || n < int.MinValue || n > int.MaxValue)
                    throw new FrontEditException("整数超出声明类型的范围");
                return JsonValue.Create((int)n);
            }
            if (v.Type == DataType.Table)
            {
                var arr = new JsonArray();
                int count = v.Table.Length;
                for (int i = 1; i <= count; i++)
                {
                    var item = v.Table.Get(i);
                    if (item.Type != DataType.Number)
                        throw new FrontEditException("整数超出声明类型的范围");
                    double n = item.Number;
                    if (Math.Abs(n - Math.Truncate(n)) > 1e-9 || n < int.MinValue || n > int.MaxValue)
                        throw new FrontEditException("整数超出声明类型的范围");
                    arr.Add((int)n);
                }
                return arr;
            }
            throw new FrontEditException("不能把这个值写入标量");
        }

        bool TryCountryVec(string path, bool create, out BtlVector vec)
        {
            vec = null;
            if (!FrontPath.TryGetVector(Document, path, create, out vec)) return false;
            return AcceptCountryVec(vec);
        }

        BtlVector CountryVec(string path, bool create)
        {
            if (!TryCountryVec(path, create, out var vec))
                throw new FrontEditException("路径无法定位");
            return vec;
        }

        bool AcceptCountryVec(BtlVector vec)
        {
            bool field10 = Document.Root.F.TryGetValue(10, out var node) && ReferenceEquals(node, vec);
            if (field10)
            {
                if (string.IsNullOrEmpty(vec.Elem)) vec.Elem = "u8";
                vec.Enc = PackedJsonStream.EncName;
                return true;
            }
            return PackedJsonStream.EncIsPackedJson(vec.Enc);
        }
    }
}
