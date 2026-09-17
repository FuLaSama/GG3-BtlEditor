/*
 * Smoke：BtlCore 自检。失败以非 0 退出。
 *
 * 1. 确认 battle.fbs 能加载，Root/1 是 MapTerrain，AgentInfo 10 个成员
 * 2. 合成最小 BTL → 读 → 写 → 再读
 * 3. 国家行为树 PackedJsonStream 编解码，并塞进 Root/10 做 BTL 回环
 * 4. 可选：--roundtrip 目录、--dump-one 单文件
 *
 * 不修改「游戏原版 btl」对照目录。
 */
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;

if (!SoftSchema.TryGet("Root", 1, out var mapHint) || mapHint.Kind != FieldKind.Table
    || mapHint.ChildTable != "MapTerrain")
{
    Console.Error.WriteLine("FAIL: battle.fbs 未加载，Root/1 不是 MapTerrain");
    return 20;
}
if (!SoftSchema.TryStructMembers("AgentInfo", out var agentMembers) || agentMembers.Length != 10)
{
    Console.Error.WriteLine("FAIL: AgentInfo 布局不是 fbs 里的 10 个成员");
    return 21;
}
Console.WriteLine("schema " + (SoftSchema.Schema?.SourcePath ?? "?"));

// 1) 合成样例：读 → 写 → 再读
var bytes = SyntheticBtl.MinimalStage();
var r = BtlToFront.FromBytes(bytes);
Console.WriteLine($"synthetic Ok={r.Ok} issues={r.Issues.Count}");
foreach (var i in r.Issues) Console.WriteLine("  " + i);

if (!(r.Document?.Root?.F.TryGetValue(0, out var ver) == true && ver is BtlScalar vs && Convert.ToInt32(vs.V) == 1))
{
    Console.Error.WriteLine("FAIL: version != 1");
    return 1;
}
if (!(r.Document.Root.F.TryGetValue(1, out var map) && map is BtlTable))
{
    Console.Error.WriteLine("FAIL: missing map table");
    return 2;
}

var built = FrontToBtl.Build(r.Document);
Console.WriteLine($"FrontToBtl Ok={built.Ok} bytes={built.Bytes.Length} issues={built.Issues.Count}");
foreach (var i in built.Issues.Take(20)) Console.WriteLine("  " + i);
if (!built.Ok)
{
    Console.Error.WriteLine("FAIL: FrontToBtl");
    return 3;
}

var r2 = BtlToFront.FromBytes(built.Bytes);
Console.WriteLine($"roundtrip Ok={r2.Ok} issues={r2.Issues.Count}");
if (!(r2.Document?.Root?.F.TryGetValue(0, out var ver2) == true && ver2 is BtlScalar vs2 && Convert.ToInt32(vs2.V) == 1))
{
    Console.Error.WriteLine("FAIL: roundtrip version");
    return 4;
}
if (!(r2.Document.Root.F.TryGetValue(1, out var map2) && map2 is BtlTable mt2
      && mt2.F.TryGetValue(2, out var tiles) && tiles is BtlVector tv && tv.V.Count == 2))
{
    Console.Error.WriteLine("FAIL: roundtrip tiles");
    return 5;
}
Console.WriteLine("roundtrip PASS");

// 1a) 关卡 JSON（字段名 / 字段 ID）能打开，且能从内存再导回去
{
    string stageJson = FindNearbyStageJson();
    if (stageJson != null)
    {
        var notes = new List<string>();
        var jdoc = StageJson.LoadFile(stageJson, SoftSchema.Schema, notes);
        int jsonTiles = CountFrontVec(jdoc.Root, 1, 2);
        int jsonAgents = CountFrontVec(jdoc.Root, 6, 0);
        int jsonEvents = CountFrontVec(jdoc.Root, 5, 0);
        Console.WriteLine($"json {Path.GetFileName(stageJson)} tiles={jsonTiles} agents={jsonAgents} events={jsonEvents} notes={notes.Count}");
        foreach (var n in notes.Take(15)) Console.WriteLine("  json " + n);
        if (jsonTiles != 588 || jsonAgents != 71 || jsonEvents != 65)
        {
            Console.Error.WriteLine("FAIL: stage10101.json 地形/部队/事件数量不对");
            return 30;
        }
        string named = StageJson.ToNamed(jdoc, SoftSchema.Schema);
        string ids = StageJson.ToFieldIds(jdoc, SoftSchema.Schema);
        var namedBack = StageJson.LoadJson(named, SoftSchema.Schema);
        var idBack = StageJson.LoadJson(ids, SoftSchema.Schema);
        if (CountFrontVec(namedBack.Root, 1, 2) != 588 || CountFrontVec(idBack.Root, 6, 0) != 71)
        {
            Console.Error.WriteLine("FAIL: JSON 字段名/字段 ID 往返数量不对");
            return 31;
        }
        Console.WriteLine("json import PASS");
    }
}

// 1b) 国家行为树 JSON-like 打包：编 → 解 → 再编进 Root/10 → BTL 回环
{
    var src = JsonNode.Parse(
        "{\"btid\":7,\"name\":\"测试国策\",\"agent\":\"Country\",\"class\":\"bt\",\"id\":1," +
        "\"node\":[{\"class\":\"seq\",\"node\":[{\"class\":\"act\",\"method\":\"SetCountryAI\",\"params\":[1,2]}]}]}");
    byte[] packed = PackedJsonStream.Encode(new object[] { src });
    if (packed == null || packed.Length < 8 || PackedJsonStream.IsEmptyHeader(packed))
    {
        Console.Error.WriteLine("FAIL: packed json encode empty");
        return 10;
    }
    if (!PackedJsonStream.LooksLike(packed) || !PackedJsonStream.TryDecode(packed, out var trees) || trees.Count != 1)
    {
        Console.Error.WriteLine("FAIL: packed json decode");
        return 11;
    }
    var t0 = trees[0] as JsonObject;
    if (t0 == null || t0["btid"]?.GetValue<int>() != 7 || t0["name"]?.GetValue<string>() != "测试国策")
    {
        Console.Error.WriteLine("FAIL: packed json fields " + trees[0]);
        return 12;
    }
    byte[] packed2 = PackedJsonStream.Encode(trees.ConvertAll(x => (object)x));
    if (!PackedJsonStream.TryDecode(packed2, out var trees2) || trees2.Count != 1
        || (trees2[0] as JsonObject)?["name"]?.GetValue<string>() != "测试国策")
    {
        Console.Error.WriteLine("FAIL: packed json re-encode");
        return 13;
    }

    var empty = PackedJsonStream.Encode(Array.Empty<object>());
    if (!PackedJsonStream.IsEmptyHeader(empty) || !PackedJsonStream.TryDecode(empty, out var none) || none.Count != 0)
    {
        Console.Error.WriteLine("FAIL: empty packed header");
        return 14;
    }

    var docBt = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
    docBt.Root.F[0] = BtlFrontJson.Scalar("u16", 1);
    var vecBt = BtlFrontJson.NewVector("u8");
    vecBt.Enc = PackedJsonStream.EncName;
    vecBt.V.Add(trees[0]);
    docBt.Root.F[10] = vecBt;
    var builtBt = FrontToBtl.Build(docBt);
    Console.WriteLine($"packed-in-btl Ok={builtBt.Ok} bytes={builtBt.Bytes.Length} issues={builtBt.Issues.Count}");
    foreach (var i in builtBt.Issues.Take(20)) Console.WriteLine("  " + i);
    if (!builtBt.Ok)
    {
        Console.Error.WriteLine("FAIL: FrontToBtl packed json");
        return 15;
    }
    var readBt = BtlToFront.FromBytes(builtBt.Bytes);
    if (!(readBt.Document?.Root?.F.TryGetValue(10, out var n10) == true && n10 is BtlVector v10
          && PackedJsonStream.EncIsPackedJson(v10.Enc) && v10.V.Count == 1 && v10.V[0] is JsonObject o10
          && o10["btid"]?.GetValue<int>() == 7 && o10["name"]?.GetValue<string>() == "测试国策"))
    {
        Console.Error.WriteLine("FAIL: BtlToFront did not unpack Root/10");
        return 16;
    }
    Console.WriteLine("packed json PASS");
}

// 1c) 原版建筑 Sub3 指针落在零填充上：读时不要发明空表，写回也不要写成空对象
{
    string orig10101 = FindStage10101Orig();
    if (orig10101 != null)
    {
            var origFront = BtlToFront.FromFile(orig10101);
            int sub3 = CountBldgSub3(origFront.Document);
            int sub2 = CountBldgNested(origFront.Document, 2);
            int sub4 = CountBldgNested(origFront.Document, 4);
            if (sub3 != 0)
            {
                Console.Error.WriteLine($"FAIL: 读入原版时发明了 {sub3} 张空 Sub3");
                return 17;
            }
            if (sub2 <= 0 || sub4 <= 0)
            {
                Console.Error.WriteLine($"FAIL: 原版建筑 Sub2/Sub4 应保留空表指针 sub2={sub2} sub4={sub4}");
                return 17;
            }
            var rewritten = FrontToBtl.Build(origFront.Document);
            if (!rewritten.Ok)
            {
                Console.Error.WriteLine("FAIL: 原版 FrontToBtl");
                return 18;
            }
            var again = BtlToFront.FromBytes(rewritten.Bytes);
            int sub3b = CountBldgSub3(again.Document);
            int sub2b = CountBldgNested(again.Document, 2);
            int sub4b = CountBldgNested(again.Document, 4);
            if (sub3b != 0)
            {
                Console.Error.WriteLine($"FAIL: 写回后又出现 {sub3b} 张空 Sub3");
                return 19;
            }
            if (sub2b != sub2 || sub4b != sub4)
            {
                Console.Error.WriteLine($"FAIL: 写回丢掉了 Sub2/Sub4 sub2 {sub2}->{sub2b} sub4 {sub4}->{sub4b}");
                return 19;
            }
            Console.WriteLine($"orig Sub3 omit PASS bytes={rewritten.Bytes.Length} sub2={sub2} sub4={sub4} (read-only {Path.GetFileName(orig10101)})");
    }
}

// 2) 命令行 .btl → Front → .btl；或 .btlf/.json → .btl
if (args.Length > 0 && File.Exists(args[0]))
{
    string path = args[0];
    BtlFrontDocument doc;
    string packedLoad = null;
    if (path.EndsWith(".btl", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".btlf", StringComparison.OrdinalIgnoreCase))
    {
        var rr = BtlToFront.FromFile(path);
        Console.WriteLine($"file Ok={rr.Ok} issues={rr.Issues.Count} fields={rr.Document?.Root?.F.Count}");
        DumpPacked(rr.Document, "load");
        packedLoad = SnapshotPacked(rr.Document);
        foreach (var i in rr.Issues.Take(30)) Console.WriteLine("  " + i);
        doc = rr.Document;
        string frontOut = args.Length > 1 ? args[1] : Path.ChangeExtension(path, ".btlfront.json");
        BtlFrontJson.SaveFile(doc, frontOut);
        Console.WriteLine("wrote " + frontOut);
    }
    else
    {
        doc = StageJson.LoadFile(path, SoftSchema.Schema);
        Console.WriteLine($"front fields={doc.Root.F.Count}");
        packedLoad = SnapshotPacked(doc);
        DumpPacked(doc, "load");
    }

    var wb = FrontToBtl.Build(doc);
    Console.WriteLine($"rewrite Ok={wb.Ok} bytes={wb.Bytes.Length} issues={wb.Issues.Count}");
    foreach (var i in wb.Issues.Where(x => x.Severity != IssueSeverity.Info).Take(40))
        Console.WriteLine("  " + i);

    if (wb.Ok)
    {
        string btlOut = args.Length > 2 ? args[2]
            : Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + ".rewrite.btl");
        File.WriteAllBytes(btlOut, wb.Bytes);
        var rr2 = BtlToFront.FromBytes(wb.Bytes);
        int warns = rr2.Issues.Count(i => i.Severity == IssueSeverity.Warning);
        Console.WriteLine($"rewrite-read Ok={rr2.Ok} fields={rr2.Document?.Root?.F.Count} warns={warns}");
        DumpPacked(rr2.Document, "rewrite");
        string packedRw = SnapshotPacked(rr2.Document);
        if (packedLoad != null)
            Console.WriteLine(packedLoad == packedRw ? "packed json snapshot MATCH" : "packed json snapshot DIFF");
        foreach (var i in rr2.Issues.Where(x => x.Severity == IssueSeverity.Warning).Take(20))
            Console.WriteLine("  " + i);
        Console.WriteLine("wrote " + btlOut);
    }
}

static string FindStage10101Orig()
{
    var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (var start in starts)
    {
        DirectoryInfo dir;
        try { dir = new DirectoryInfo(start); }
        catch { continue; }
        while (dir != null)
        {
            string folder = Path.Combine(dir.FullName, "战役相关文件", "BTL");
            if (Directory.Exists(folder))
            {
                foreach (var f in Directory.GetFiles(folder, "stage10101*.btl"))
                    if (f.Contains("原版"))
                        return f;
            }
            dir = dir.Parent;
        }
    }
    return null;
}

static int CountBldgSub3(BtlFrontDocument doc) => CountBldgNested(doc, 3);

static int CountBldgNested(BtlFrontDocument doc, int fieldId)
{
    if (doc?.Root?.F.TryGetValue(5, out var n5) != true || n5 is not BtlTable trig) return -1;
    if (!trig.F.TryGetValue(0, out var n0) || n0 is not BtlVector evs) return -1;
    int n = 0;
    foreach (var item in evs.V)
    {
        if (item is not BtlTable ev) continue;
        if (!ev.F.TryGetValue(3, out var n3) || n3 is not BtlTable bldg) continue;
        if (bldg.F.TryGetValue(fieldId, out var sub) && sub is BtlTable)
            n++;
    }
    return n;
}

static void DumpPacked(BtlFrontDocument doc, string tag)
{
    if (doc?.Root == null)
    {
        Console.WriteLine($"[{tag}] no root");
        return;
    }
    int packedVecs = 0;
    void Walk(BtlNode n, string path)
    {
        if (n is BtlTable tbl)
        {
            foreach (var kv in tbl.F.OrderBy(x => x.Key))
                Walk(kv.Value, path + "/" + kv.Key);
            return;
        }
        if (n is not BtlVector vec) return;
        if (PackedJsonStream.EncIsPackedJson(vec.Enc) || (vec.V.Count > 0 && vec.V[0] is JsonNode))
        {
            packedVecs++;
            Console.WriteLine($"[{tag}] {path} elem={vec.Elem} enc={vec.Enc} trees={vec.V.Count}");
            for (int i = 0; i < Math.Min(vec.V.Count, 5); i++)
            {
                if (vec.V[i] is JsonObject o)
                    Console.WriteLine($"    [{i}] btid={o["btid"]} name={o["name"]} agent={o["agent"]} class={o["class"]}");
                else
                    Console.WriteLine($"    [{i}] {vec.V[i]?.GetType().Name}");
            }
        }
        else if (path == "/10")
        {
            Console.WriteLine($"[{tag}] /10 raw elem={vec.Elem} enc={vec.Enc} count={vec.V.Count} first={vec.V.FirstOrDefault()?.GetType().Name}");
        }
    }
    Walk(doc.Root, "");
    if (packedVecs == 0) Console.WriteLine($"[{tag}] no packed-json vectors");
}

static string SnapshotPacked(BtlFrontDocument doc)
{
    if (doc?.Root?.F.TryGetValue(10, out var n) != true || n is not BtlVector vec)
        return "";
    byte[] packed = PackedJsonStream.Encode(vec.V);
    return packed.Length + ":" + Convert.ToHexString(packed);
}

if (args.Length > 0 && args[0] == "--dump-one" && args.Length > 1 && File.Exists(args[1]))
    return DumpOne(args[1]);

if (args.Length > 0 && (args[0] == "--roundtrip" || Directory.Exists(args[0])))
{
    string dir = args[0] == "--roundtrip"
        ? (args.Length > 1 ? args[1] : "")
        : args[0];
    return RoundtripDir(dir);
}

static int RoundtripDir(string dir)
{
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
    {
        Console.Error.WriteLine("roundtrip dir missing: " + dir);
        return 20;
    }

    var files = Directory.GetFiles(dir, "stage*.btl")
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    Console.WriteLine($"roundtrip files={files.Length} dir={dir}");
    int match = 0, diff = 0, fail = 0, sameLen = 0;
    int minFirst = int.MaxValue;
    foreach (var file in files)
    {
        byte[] orig = File.ReadAllBytes(file);
        string name = Path.GetFileName(file);
        try
        {
            var loaded = BtlToFront.FromBytes(orig);
            if (loaded.Document?.Root == null)
            {
                Console.WriteLine($"FAIL-READ {name}");
                fail++;
                continue;
            }
            var built = FrontToBtl.Build(loaded.Document);
            byte[] neu = built.Bytes ?? Array.Empty<byte>();
            string h0 = Convert.ToHexString(SHA256.HashData(orig));
            string h1 = Convert.ToHexString(SHA256.HashData(neu));
            if (orig.Length == neu.Length) sameLen++;
            if (h0 == h1 && orig.Length == neu.Length)
            {
                match++;
                continue;
            }
            diff++;
            int first = -1;
            int n = Math.Min(orig.Length, neu.Length);
            for (int i = 0; i < n; i++)
            {
                if (orig[i] != neu[i]) { first = i; break; }
            }
            if (first < 0 && orig.Length != neu.Length) first = n;
            if (first >= 0 && first < minFirst) minFirst = first;
            Console.WriteLine($"DIFF {name} orig={orig.Length} out={neu.Length} first=0x{(first < 0 ? 0 : first):X} sha256 {h0[..8]}→{h1[..8]}");
        }
        catch (Exception ex)
        {
            fail++;
            Console.WriteLine($"EX {name} {ex.GetType().Name}: {ex.Message}");
        }
    }
    Console.WriteLine($"roundtrip MATCH={match} DIFF={diff} FAIL={fail} SAME_LEN={sameLen} MIN_FIRST=0x{(minFirst == int.MaxValue ? 0 : minFirst):X} TOTAL={files.Length}");
    return diff == 0 && fail == 0 ? 0 : 21;
}

static int DumpOne(string path)
{
    byte[] orig = File.ReadAllBytes(path);
    var loaded = BtlToFront.FromBytes(orig);
    Console.WriteLine($"load issues={loaded.Issues.Count}");
    foreach (var i in loaded.Issues.Take(80))
        Console.WriteLine("  " + i);
    var built = FrontToBtl.Build(loaded.Document);
    byte[] neu = built.Bytes ?? Array.Empty<byte>();
    Console.WriteLine($"orig={orig.Length} out={neu.Length} issues={built.Issues.Count}");
    foreach (var i in built.Issues.Take(40))
        Console.WriteLine("  " + i);

    int first = -1;
    int n = Math.Min(orig.Length, neu.Length);
    int diffBytes = 0;
    for (int i = 0; i < n; i++)
    {
        if (orig[i] != neu[i])
        {
            if (first < 0) first = i;
            diffBytes++;
        }
    }
    if (orig.Length != neu.Length && first < 0) first = n;
    Console.WriteLine($"first=0x{(first < 0 ? 0 : first):X} diffBytes={diffBytes}");

    DumpRoot("ORIG", orig);
    DumpRoot("OUT ", neu);

    if (first >= 0)
    {
        int a = Math.Max(0, first - 16);
        int b = Math.Min(n, first + 64);
        Console.WriteLine($"hex ORIG[{a}..{b}): " + HexSlice(orig, a, b - a));
        Console.WriteLine($"hex OUT [{a}..{b}): " + HexSlice(neu, a, b - a));
    }

    if (loaded.Document?.Root?.F.TryGetValue(10, out var n10) == true && n10 is BtlVector v10)
    {
        byte[] packed = PackedJsonStream.ShouldEncode(v10, out var p) ? p : Array.Empty<byte>();
        Console.WriteLine($"packed enc={v10.Enc} trees={v10.V.Count} packedBytes={packed.Length}");
    }
    return 0;
}

static void DumpRoot(string tag, byte[] d)
{
    if (d == null || d.Length < 8) { Console.WriteLine($"{tag} too short"); return; }
    int root = BitConverter.ToInt32(d, 0);
    Console.WriteLine($"{tag} rootOff={root}");
    if (root < 4 || root + 4 > d.Length) return;
    int soff = BitConverter.ToInt32(d, root);
    int vt = root - soff;
    if (vt < 0 || vt + 4 > d.Length) { Console.WriteLine($"{tag} bad vtable"); return; }
    ushort vs = BitConverter.ToUInt16(d, vt);
    ushort os = BitConverter.ToUInt16(d, vt + 2);
    int slots = (vs - 4) / 2;
    Console.WriteLine($"{tag} vt@{vt} vs={vs} os={os} slots={slots}");
    var sb = new StringBuilder();
    for (int i = 0; i < slots && i < 16; i++)
    {
        ushort off = BitConverter.ToUInt16(d, vt + 4 + i * 2);
        int abs = off == 0 ? 0 : root + off;
        int ptr = 0;
        if (off != 0 && abs + 4 <= d.Length)
            ptr = abs + BitConverter.ToInt32(d, abs);
        sb.Append($" f{i}off={off}");
        if (off != 0) sb.Append($"->@{ptr}");
    }
    Console.WriteLine($"{tag}{sb}");
}

static string HexSlice(byte[] d, int start, int len)
{
    int n = Math.Min(len, d.Length - start);
    if (n <= 0) return "";
    return Convert.ToHexString(d.AsSpan(start, n));
}

static string FindNearbyStageJson()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            string a = Path.Combine(dir.FullName, "战役相关文件", "BTL", "stage10101.json");
            if (File.Exists(a)) return a;
            dir = dir.Parent;
        }
    }
    return null;
}

static int CountFrontVec(BtlTable root, int tableId, int vecId)
{
    if (root?.F.TryGetValue(tableId, out var n) != true || n is not BtlTable tbl)
        return -1;
    if (tbl.F.TryGetValue(vecId, out var v) && v is BtlVector vec)
        return vec.V.Count;
    return -1;
}

Console.WriteLine("PASS");
return 0;
