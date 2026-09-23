using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class CountryBtParityTests
{
    static string CountryLua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "EditorLayout", "scripts", "country.lua");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("EditorLayout/scripts/country.lua");
    }

    static BtlFrontDocument Sample()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var vec = FrontEdit.EnsureVec(doc.Root, 10, "u8");
        vec.Enc = PackedJsonStream.EncName;
        return doc;
    }

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(CountryLua(), "country.lua");
        return host;
    }

    static Dictionary<string, object> Map(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) d[key] = value;
        return d;
    }

    static int BtInt(JsonObject obj, string key) => obj[key].GetValue<int>();

    static JsonObject Tree(BtlFrontDocument doc, int index) => (JsonObject)((BtlVector)doc.Root.F[10]).V[index];

    static JsonObject NodeAt(JsonObject tree, params int[] path)
    {
        JsonNode cur = tree;
        foreach (int index in path)
            cur = ((JsonObject)cur)["node"][index];
        return (JsonObject)cur;
    }

    [Fact]
    public void Tree_edits_match_script_and_packed_bytes()
    {
        var doc = Sample();
        var host = Host(doc);

        Assert.Equal(PackedJsonStream.EmptyHeader, PackedJsonStream.Encode(((BtlVector)doc.Root.F[10]).V));

        host.Run("add_tree", new ScriptArgs());
        Assert.Equal(1, BtInt(Tree(doc, 0), "btid"));

        host.Run("add_root", new ScriptArgs { Index = 0 });
        host.Run("add_child", new ScriptArgs { Index = 0, Input = Map(("path", new object[] { 0 })) });
        host.Run("add_child", new ScriptArgs { Index = 0, Input = Map(("path", new object[] { 0 })) });
        Assert.Equal("act", NodeAt(Tree(doc, 0), 0, 1)["class"].ToString().Trim('"'));

        host.Run("delete_child", new ScriptArgs
        {
            Index = 0,
            Input = Map(("path", new object[] { 0 }), ("index", 0))
        });

        host.Run("save_bt", new ScriptArgs
        {
            Index = 0,
            Input = Map(
                ("btid", 7), ("id", null), ("name", "  甲  "), ("agent", "  "), ("class", "bt"),
                ("path", new object[] { 0, 0 }),
                ("node_id", null), ("node_class", "   "), ("method", " go "),
                ("params", new object[] { 1, 2 }), ("rounds", Array.Empty<object>()),
                ("result", null), ("count", 3))
        });
        var node = NodeAt(Tree(doc, 0), 0, 0);
        Assert.Equal("甲", Tree(doc, 0)["name"].ToString().Trim('"'));
        Assert.False(Tree(doc, 0).ContainsKey("agent"));
        Assert.False(node.ContainsKey("class"));
        Assert.Equal("go", node["method"].ToString().Trim('"'));
        Assert.False(node.ContainsKey("rounds"));

        host.Run("save_bt", new ScriptArgs
        {
            Index = 0,
            Input = Map(
                ("btid", 7), ("id", null), ("name", "甲"), ("agent", null), ("class", "bt"),
                ("path", new object[] { 0, 0 }),
                ("node_id", null), ("node_class", null), ("method", "go"),
                ("params", Array.Empty<object>()), ("rounds", null),
                ("result", null), ("count", 3))
        });
        Assert.False(NodeAt(Tree(doc, 0), 0, 0).ContainsKey("params"));

        host.Run("delete_child", new ScriptArgs { Index = 0, Input = Map(("path", null), ("index", 0)) });
        host.Run("add_tree", new ScriptArgs());
        host.Run("delete_tree", new ScriptArgs { Index = 0 });
        Assert.Equal(8, BtInt(Tree(doc, 0), "btid"));
        Assert.NotNull(PackedJsonStream.Encode(((BtlVector)doc.Root.F[10]).V));
        Assert.NotEmpty(FrontToBtl.Build(doc).Bytes);

        string before = BtlFrontJson.Serialize(doc);
        host.Execute("editor.action(\"bad\", function(ctx) editor.insert(\"Root.rle_metadata\", 0) end)", "bad.lua");
        Assert.ThrowsAny<Exception>(() => host.Run("bad", new ScriptArgs()));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }
}
