using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;

// 从仓库根运行；只读样本，所有输出写到明确指定的结果目录。
string root=Directory.GetCurrentDirectory();
string output=Path.GetFullPath(args.Length>0 ? args[0] : "artifacts/flex-interop");
Directory.CreateDirectory(output);
var manifest=new JsonArray();
void Emit(string name, JsonArray values, string original=null, bool changed=false)
{
    byte[] bytes=PackedJsonStream.Encode(values.Select(n=>(object)n).ToArray());
    File.WriteAllBytes(Path.Combine(output,name+".flex"),bytes);
    manifest.Add(new JsonObject{["name"]=name,["expected"]=values.DeepClone(),["original_btl"]=original,["changed"]=changed});
}
byte[] Payload(byte[] bytes)
{
    int table=BitConverter.ToInt32(bytes,0),vt=table-BitConverter.ToInt32(bytes,table);
    int slot=table+BitConverter.ToUInt16(bytes,vt+24),vector=slot+BitConverter.ToInt32(bytes,slot);
    return bytes.AsSpan(vector+4,BitConverter.ToInt32(bytes,vector)).ToArray();
}
var fixtures=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"tests/FrontEdit.Tests/Fixtures/flexbuffers.json")))["fixtures"].AsArray();
int fixtureCount=0,sampleCount=0,changedCount=0;
foreach(var fixture in fixtures.Where(f=>f["supported"].GetValue<bool>()))
{
    byte[] bytes=Convert.FromBase64String(fixture["bytes"].GetValue<string>());
    if(!PackedJsonStream.TryDecode(bytes,out var nodes)) throw new Exception("Official fixture rejected: "+fixture["name"]);
    Emit("fixture-"+fixture["name"].GetValue<string>(),new JsonArray(nodes.ToArray()));
    manifest[^1]["expected"]=fixture["expected"].DeepClone(); fixtureCount++;
}
Emit("large-offset-and-scalar",new JsonArray(new JsonObject{
    ["name"]=new string('中',70000),["a"]=-1,["z"]=70000,["max"]=ulong.MaxValue,["min"]=long.MinValue,
    ["unknown"]=new JsonArray(true,null,1.25),["negative_zero"]=JsonValue.Create(-0.0)
}));
if(args.Length>1)
{
    foreach(string file in Directory.GetFiles(Path.GetFullPath(args[1]),"*.btl",SearchOption.AllDirectories).Order())
    {
        byte[] raw=Payload(File.ReadAllBytes(file));
        if(!PackedJsonStream.TryDecode(raw,out var nodes)) throw new Exception("Sample rejected: "+file);
        Emit("sample-"+sampleCount,new JsonArray(nodes.Select(n=>n.DeepClone()).ToArray()),file);
        // 检查 work JSON、克隆及完整导出链路中未改 payload 的字节保留。
        var doc=new BtlFrontDocument{Root=BtlFrontJson.NewTable()};
        var vector=FrontEdit.EnsureVec(doc.Root,10,"u8"); vector.V.AddRange(raw.Select(b=>(object)b));
        if(!PackedJsonStream.TryUnpackVector(vector)) throw new Exception("Unpack failed");
        var restored=BtlFrontJson.CloneDocument(BtlFrontJson.Parse(BtlFrontJson.Serialize(doc)));
        var built=FrontToBtl.Build(restored);
        if(!built.Ok || !raw.SequenceEqual(Payload(built.Bytes))) throw new Exception("Original payload changed: "+file);
        if(nodes.Count>0)
        {
            nodes[0]["name"]=nodes[0]["name"].GetValue<string>()+"（核验）";
            nodes[0]["audit_extra"]=new JsonArray(JsonValue.Create(ulong.MaxValue),-1,70000,1.25,true,null);
            Emit("changed-"+sampleCount,new JsonArray(nodes.ToArray()),file,true); changedCount++;
        }
        sampleCount++;
    }
}
File.WriteAllText(Path.Combine(output,"manifest.json"),manifest.ToJsonString());
Console.WriteLine($"Wrote official fixture cases={fixtureCount}, original cases={sampleCount}, modified libraries={changedCount}, large-offset case=1. Run verify.py for independent verification.");
