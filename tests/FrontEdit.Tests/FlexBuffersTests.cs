using System.Reflection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class FlexBuffersTests
{
    static bool SameJson(JsonNode a, JsonNode b)
    {
        if(a is JsonObject x && b is JsonObject y) return x.Count==y.Count && x.All(p=>y.ContainsKey(p.Key) && SameJson(p.Value,y[p.Key]));
        if(a is JsonArray xs && b is JsonArray ys) return xs.Count==ys.Count && xs.Zip(ys).All(p=>SameJson(p.First,p.Second));
        if(a?.GetValueKind()==JsonValueKind.Number && b?.GetValueKind()==JsonValueKind.Number)
            return decimal.Parse(a.ToJsonString(),NumberStyles.Float,CultureInfo.InvariantCulture)==decimal.Parse(b.ToJsonString(),NumberStyles.Float,CultureInfo.InvariantCulture);
        return JsonNode.DeepEquals(a,b);
    }
    static JsonArray Fixtures()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.flexbuffers.json")));
        using var reader = new StreamReader(stream!);
        return JsonNode.Parse(reader.ReadToEnd())["fixtures"].AsArray();
    }
    public static IEnumerable<object[]> OfficialCases() => Fixtures().Select(f => new object[] {
        f["name"].GetValue<string>(), f["bytes"].GetValue<string>(), f["supported"].GetValue<bool>(), f["expected"]?.ToJsonString() });

    [Theory]
    [MemberData(nameof(OfficialCases))]
    public void Reads_independent_official_fixtures_and_preserves_values(string name, string bytes, bool supported, string expected)
    {
        bool result = PackedJsonStream.TryDecode(Convert.FromBase64String(bytes), out var nodes);
        Assert.Equal(supported, result);
        if (!supported) { Assert.Null(nodes); return; }
        Assert.True(SameJson(JsonNode.Parse(expected), new JsonArray(nodes.Select(n=>n.DeepClone()).ToArray())), name);
        byte[] encoded = PackedJsonStream.Encode(nodes.Cast<object>().ToArray());
        Assert.True(PackedJsonStream.TryDecode(encoded, out var again));
        Assert.True(SameJson(JsonNode.Parse(expected), new JsonArray(again.ToArray())), name);
        var doc=new BtlFrontDocument{Root=BtlFrontJson.NewTable()};
        var vector=FrontEdit.EnsureVec(doc.Root,10,"u8"); vector.V.AddRange(Convert.FromBase64String(bytes).Select(b=>(object)b));
        Assert.True(PackedJsonStream.TryUnpackVector(vector));
        Assert.Equal(Convert.FromBase64String(bytes),Write(BtlFrontJson.Parse(BtlFrontJson.Serialize(doc))));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("signed--129")]
    [InlineData("signed--32769")]
    [InlineData("signed-70000")]
    [InlineData("signed--9223372036854775808")]
    public void Simple_writer_layout_matches_official_bytes(string name)
    {
        var fixture=Fixtures().Single(f=>f["name"].GetValue<string>()==name);
        var values=fixture["expected"].AsArray().Select(n=>(object)n).ToArray();
        Assert.Equal(Convert.FromBase64String(fixture["bytes"].GetValue<string>()),PackedJsonStream.Encode(values));
    }

    static (BtlFrontDocument Document, byte[] Bytes) Imported()
    {
        var fixture=Fixtures().Single(f=>f["name"].GetValue<string>()=="nested-gb18030");
        byte[] bytes=Convert.FromBase64String(fixture["bytes"].GetValue<string>());
        var doc=new BtlFrontDocument {Root=BtlFrontJson.NewTable()};
        var vector=FrontEdit.EnsureVec(doc.Root,10,"u8"); vector.V.AddRange(bytes.Select(b=>(object)b));
        Assert.True(PackedJsonStream.TryUnpackVector(vector));
        return (doc,bytes);
    }
    static BtlVector Vector(BtlFrontDocument doc)=>(BtlVector)doc.Root.F[10];
    static byte[] Write(BtlFrontDocument doc)
    {
        Assert.True(PackedJsonStream.ShouldEncode(Vector(doc),out var bytes)); return bytes;
    }

    [Fact]
    public void Original_bytes_survive_clone_work_json_undo_redo_and_unrelated_edits()
    {
        var (doc,bytes)=Imported();
        Assert.Equal(bytes,Write(doc));
        Assert.Equal(bytes,Write(BtlFrontJson.CloneDocument(doc)));
        var restored=StageJson.LoadJson(BtlFrontJson.Serialize(doc));
        Assert.Equal(bytes,Write(restored));
        restored.Editing.Execute(()=>FrontEdit.SetScalar(restored.Root,0,"u16",(ushort)3));
        Assert.Equal(bytes,Write(restored));
        restored.Editing.Execute(()=>((JsonObject)Vector(restored).V[0])["name"]="修改中文");
        Assert.False(bytes.SequenceEqual(Write(restored)));
        Assert.True(restored.Editing.Undo()); Assert.Equal(bytes,Write(restored));
        Assert.True(restored.Editing.Redo());
        Assert.True(PackedJsonStream.TryDecode(Write(restored),out var trees));
        Assert.Equal("修改中文",trees[0]["name"].GetValue<string>());
        Assert.True(restored.Editing.Undo());
        string before=BtlFrontJson.Serialize(restored);
        Assert.Throws<InvalidOperationException>(()=>restored.Editing.Execute(()=>{
            ((JsonObject)Vector(restored).V[0])["name"]="discard"; throw new InvalidOperationException();
        }));
        Assert.Equal(before,BtlFrontJson.Serialize(restored)); Assert.Equal(bytes,Write(restored));
    }

    [Fact]
    public void Invalid_encoded_content_blocks_export_instead_of_clearing_library()
    {
        var (doc,_)=Imported();
        Vector(doc).V.Add(7);
        var result=FrontToBtl.Build(doc);
        Assert.False(result.Ok); Assert.Empty(result.Bytes);
        Assert.Contains(result.Issues,i=>i.Severity==IssueSeverity.Error);
        Vector(doc).V.RemoveAt(Vector(doc).V.Count-1);
        Vector(doc).Enc="unknown_codec";
        Assert.False(FrontToBtl.Build(doc).Ok);
    }

    [Fact]
    public void Unsupported_raw_values_remain_bytes_and_cannot_be_edited_as_a_tree()
    {
        byte[] raw=Convert.FromBase64String(Fixtures().Single(f=>f["name"].GetValue<string>()=="unsupported-blob")["bytes"].GetValue<string>());
        var doc=new BtlFrontDocument {Root=BtlFrontJson.NewTable()};
        FrontEdit.EnsureVec(doc.Root,10,"u8").V.AddRange(raw.Select(b=>(object)b));
        var built=FrontToBtl.Build(doc); Assert.True(built.Ok);
        var read=BtlToFront.FromBytes(built.Bytes); Assert.False(read.HasUnresolvedData);
        var vector=Vector(read.Document);
        Assert.Null(vector.Enc); Assert.Equal(raw,vector.V.Select(Convert.ToByte));
        var host=new ScriptHost(); EditorScripts.Load(host); host.Load(read.Document);
        host.Execute("editor.action('add',function() game.behavior_trees.create() end)","unsupported.lua");
        string before=BtlFrontJson.Serialize(read.Document);
        Assert.Throws<FrontEditException>(()=>host.Run("add",null));
        Assert.Equal(before,BtlFrontJson.Serialize(read.Document));
        Assert.True(FrontToBtl.Build(read.Document).Ok);
    }

    [Fact]
    public void Large_offsets_scalars_and_json_values_are_not_truncated_or_coerced()
    {
        var value=new JsonObject { ["name"]=new string('x',70000),["z"]=70000,["a"]=-1,
            ["float"]=1.25,["nothing"]=null,["bool"]=true,["max"]=ulong.MaxValue,["min"]=long.MinValue };
        byte[] bytes=PackedJsonStream.Encode(new object[]{value});
        Assert.True(PackedJsonStream.TryDecode(bytes,out var nodes));
        Assert.True(JsonNode.DeepEquals(value,nodes[0]));
        Assert.True(nodes[0].AsObject().ContainsKey("nothing"));
        Assert.Throws<InvalidDataException>(()=>PackedJsonStream.Encode(new object[]{JsonNode.Parse("{\"x\":18446744073709551616}")}));
        Assert.Throws<InvalidDataException>(()=>PackedJsonStream.Encode(new object[]{JsonNode.Parse("{\"x\":1e-999}")}));
        Assert.Throws<InvalidDataException>(()=>PackedJsonStream.Encode(new object[]{JsonNode.Parse("{\"x\":9007199254740993.0}")}));
        Assert.ThrowsAny<Exception>(()=>PackedJsonStream.Encode(new object[]{new JsonObject{["name"]="\ud800"}}));
    }

    [Fact]
    public void Wait_rounds_is_scalar_empty_params_are_preserved_and_bad_edits_roll_back()
    {
        var doc=new BtlFrontDocument {Root=BtlFrontJson.NewTable()};
        var vector=FrontEdit.EnsureVec(doc.Root,10,"u8"); vector.Enc=PackedJsonStream.EncName;
        vector.V.Add(JsonNode.Parse("{\"btid\":1,\"id\":-1,\"node\":[{\"class\":\"wait\",\"id\":0,\"rounds\":2}]}"));
        var host=new ScriptHost(); EditorScripts.Load(host,"country/节点属性.lua","country/行为树属性.lua"); host.Load(doc);
        host.Execute("""
            editor.action('read',{get=function() return game.behavior_trees.get(0):node({0}).rounds end})
            editor.action('bad',function() game.behavior_trees.get(0):node({0}).rounds={2} end)
            ""","rounds.lua");
        Assert.Equal(2d,host.CallGet("read"));
        string before=BtlFrontJson.Serialize(doc);
        Assert.Throws<FrontEditException>(()=>host.Run("bad",null)); Assert.Equal(before,BtlFrontJson.Serialize(doc));
        host.Run("save_bt",new ScriptArgs{Index=0,Input=new(){
            ["btid"]=1,["id"]=-1,["name"]="等待",["agent"]="CBTArmyAgent",["class"]="bt",
            ["path"]=new object[]{0},["node_id"]=0,["node_class"]="wait",["rounds"]=3,["params"]=Array.Empty<object>()
        }});
        var node=((JsonObject)Vector(doc).V[0])["node"][0];
        Assert.Equal(3,node["rounds"].GetValue<int>()); Assert.Empty(node["params"].AsArray());
        Assert.True(doc.Editing.Undo()); Assert.Equal(before,BtlFrontJson.Serialize(doc));
        Assert.True(doc.Editing.Redo()); Assert.Equal(3d,host.CallGet("read"));
    }
}
