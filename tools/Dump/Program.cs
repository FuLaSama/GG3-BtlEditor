/*
 * Dump 命令行：按 battle.fbs 把一份 .btl 读成带字段名的 JSON。
 *
 *   Dump <stage.btl> [out.json] [--btl out.btl] [--fbs battle.fbs]
 *
 * 加 --btl 时：dump JSON → SchemaDumpToFront → FrontToBtl，用来把「读到的值」
 * 按我们的算法写回，再丢进游戏看能不能跑。不保证和原文件逐字节相同。
 */
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("用法: Dump <stage.btl> [out.json] [--btl out.btl] [--fbs battle.fbs]");
    Console.WriteLine("按 battle.fbs 读出字段；加 --btl 则把读到的数据编回 .btl。");
    return 1;
}

string btlPath = null;
string outPath = null;
string fbsPath = null;
string writeBtl = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--fbs" && i + 1 < args.Length)
    {
        fbsPath = args[++i];
        continue;
    }
    if (args[i] == "--btl" && i + 1 < args.Length)
    {
        writeBtl = args[++i];
        continue;
    }
    if (btlPath == null) btlPath = args[i];
    else if (outPath == null) outPath = args[i];
}

if (string.IsNullOrEmpty(btlPath) || !File.Exists(btlPath))
{
    Console.Error.WriteLine("找不到 btl: " + btlPath);
    return 2;
}

fbsPath = ResolveFbs(fbsPath);
if (fbsPath == null)
{
    Console.Error.WriteLine("找不到 battle.fbs（可用 --fbs 指定）");
    return 3;
}

var schema = FbsSchema.LoadFile(fbsPath);
var result = BtlSchemaReader.FromFile(btlPath, schema);
if (!result.Ok)
{
    foreach (var n in result.Notes) Console.Error.WriteLine(n);
    return 4;
}

outPath ??= Path.Combine(Directory.GetCurrentDirectory(),
    Path.GetFileNameWithoutExtension(btlPath) + ".schema.json");
var jsonOpts = new JsonSerializerOptions(BtlFrontJson.JsonOptions)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
File.WriteAllText(outPath, result.Document.ToJsonString(jsonOpts));

Console.WriteLine($"file={Path.GetFileName(btlPath)} size={result.Document["size"]} fbs={Path.GetFileName(fbsPath)}");
PrintSummary(result.Document["root"] as JsonObject);
Console.WriteLine($"notes={result.Notes.Count}");
foreach (var n in result.Notes.Take(40))
    Console.WriteLine("  " + n);
Console.WriteLine("wrote " + outPath);

if (!string.IsNullOrEmpty(writeBtl))
{
    var convNotes = new List<string>();
    var front = SchemaDumpToFront.ToFront(result.Document, schema, convNotes);
    var built = FrontToBtl.Build(front);
    Console.WriteLine($"rewrite Ok={built.Ok} bytes={built.Bytes?.Length ?? 0} issues={built.Issues.Count} convNotes={convNotes.Count}");
    foreach (var n in convNotes.Take(20))
        Console.WriteLine("  conv " + n);
    foreach (var i in built.Issues.Where(x => x.Severity != IssueSeverity.Info).Take(30))
        Console.WriteLine("  " + i);
    if (!built.Ok)
        return 5;
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(writeBtl)) ?? ".");
    File.WriteAllBytes(writeBtl, built.Bytes);
    Console.WriteLine("wrote-btl " + writeBtl);
}
return 0;

static string ResolveFbs(string given)
{
    if (!string.IsNullOrEmpty(given) && File.Exists(given))
        return Path.GetFullPath(given);
    return FbsSchema.FindNearby();
}

static void PrintSummary(JsonObject root)
{
    if (root == null) return;
    Console.WriteLine("version=" + AsText(root["version"]));

    if (root["map_terrain"] is JsonObject map)
    {
        var size = map["size"] as JsonObject;
        Console.WriteLine(
            $"map {AsText(size?["width"])}x{AsText(size?["height"])} " +
            $"playable={AsText(size?["playable_width"])}x{AsText(size?["playable_height"])} " +
            $"tiles={Count(map["tiles"])} attrs={Count(map["attributes"])} val1={AsText(map["val1"])} val2={AsText(map["val2"])}");
    }

    if (root["stage_metadata"] is JsonObject meta)
        Console.WriteLine($"metadata round_limit={AsText(meta["round_limit"])} targets={Count(meta["targets"])}");

    if (root["battle_info"] is JsonObject battle)
        Console.WriteLine($"battle field_0={AsText(battle["field_0"])} reinforce={Count(battle["reinforce_points"])}");

    if (root["faction_info"] is JsonObject fac)
        Console.WriteLine($"factions={Count(fac["factions"])} cards={Count(fac["faction_cards"])} limits={Count(fac["faction_limits"])} ver={AsText(fac["version"])}");

    if (root["trigger_info"] is JsonObject trig)
        Console.WriteLine($"triggers events={Count(trig["events"])} actions={Count(trig["actions"])} nodes={Count(trig["triggers"])}");

    if (root["ai_info"] is JsonObject ai)
        Console.WriteLine($"ai agents={Count(ai["agents"])} behaviors={Count(ai["behaviors"])} routes={Count(ai["routes"])} faction_id={AsText(ai["faction_id"])}");

    if (root["region_info"] is JsonObject region)
        Console.WriteLine($"region name={AsText(region["name"])} sub={Count(region["sub_regions"])} events_bytes={Count(region["events"])} props_bytes={Count(region["properties"])}");

    Console.WriteLine($"stage_config={(root["stage_config"] is JsonObject sc ? sc["_type"]?.ToString() + " fields" : AsText(root["stage_config"]))}");
    Console.WriteLine($"decal_info={Describe(root["decal_info"])} view_boundary={Describe(root["view_boundary"])}");

    var rle = root["rle_metadata"];
    if (rle is JsonObject pack)
        Console.WriteLine($"rle_metadata bytes={Count(pack["bytes"])} packed_json={Count(pack["packed_json"])}");
    else
        Console.WriteLine($"rle_metadata={Describe(rle)}");
}

static int Count(JsonNode n) => n switch
{
    JsonArray a => a.Count,
    JsonObject o when o["bytes"] is JsonArray b => b.Count,
    JsonObject o when o["packed_json"] is JsonArray p => p.Count,
    _ => n == null || n.GetValueKind() == JsonValueKind.Null ? 0 : 1
};

static string AsText(JsonNode n)
{
    if (n == null || n.GetValueKind() == JsonValueKind.Null) return "null";
    if (n is JsonValue v) return v.ToString();
    return n.GetValueKind().ToString();
}

static string Describe(JsonNode n)
{
    if (n == null || n.GetValueKind() == JsonValueKind.Null) return "null";
    if (n is JsonObject o)
    {
        if (o["_error"] != null) return $"error:{o["_error"]}@0x{o["_ptr"]}";
        if (o["_type"] is JsonValue t) return t.ToString();
    }
    return AsText(n);
}
