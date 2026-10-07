using BtlCore.Front;
using MoonSharp.Interpreter;

namespace BtlCore.Scripting;

public sealed partial class ScriptHost
{
    List<TerrainEdits.Cell> _gameCells;
    List<object> _gameExtraAttributes;
    bool _gameAttributesDirty;
    public int TerrainProjectionCount { get; private set; }

    void ResetGameOperation()
    {
        _vectorIndices.Clear();
        _gameCells = null;
        _gameExtraAttributes = null;
        _gameAttributesDirty = false;
    }

    void FlushGameOperation()
    {
        if (!_gameAttributesDirty) return;
        _vectorIndices.Clear();
        RequireWrite();
        var map = FrontEdit.EnsureTable(Document.Root, 1);
        var attrs = FrontEdit.EnsureVec(map, 3, "struct");
        if (attrs.StructLayout.Count == 0) attrs.StructLayout.AddRange(new[] { "u8", "u8", "i8", "i8" });
        attrs.V.Clear();
        foreach (var cell in _gameCells)
        {
            if ((cell.Terrain & 1024) != 0) attrs.V.Add(cell.Decor);
            if ((cell.Terrain & 2048) != 0) attrs.V.Add(cell.Main);
            if ((cell.Terrain & 4096) != 0) attrs.V.Add(cell.Secondary);
        }
        // 保留已知图层之后的未知属性，避免一次局部修改丢失尾部数据。
        attrs.V.AddRange(_gameExtraAttributes);
        _gameAttributesDirty = false;
    }

    int GameCellIndex(DynValue value)
    {
        int index = AsIndex(value);
        int width = Convert.ToInt32(NsPath.Get(Document, "/1/0.0") ?? 0);
        int height = Convert.ToInt32(NsPath.Get(Document, "/1/0.1") ?? 0);
        long total = (long)width * height;
        if (total <= 0 || total > 1_000_000 || index < 0 || index >= total)
            throw new FrontEditException("格子不在可编辑的地图范围内");
        return index;
    }

    ushort GameWord(int index) => Convert.ToUInt16(NsPath.Get(Document, "/1/2[" + index + "]") ?? 9001);

    void WriteGameWord(int index, ushort word)
    {
        NsPath.Set(Document, "/1/2[" + index + "]", "u16", word);
        if (_gameCells != null) _gameCells[index].Terrain = word;
    }

    void ProjectGameCells()
    {
        if (_gameCells != null) return;
        TerrainProjectionCount++;
        _gameCells = TerrainEdits.Project(Document);
        int used = _gameCells.Sum(c => ((c.Terrain & 1024) != 0 ? 1 : 0)
            + ((c.Terrain & 2048) != 0 ? 1 : 0) + ((c.Terrain & 4096) != 0 ? 1 : 0));
        var attrs = NsPath.NodeAt(Document, "/1/3") as BtlVector;
        if ((attrs?.V.Count ?? 0) < used) throw new FrontEditException("地形标志与属性数量不匹配，不能修改图层");
        if (attrs != null && attrs.V.Take(used).Any(v => v is not BtlStruct st || st.V.Count < 4))
            throw new FrontEditException("地形图层数据不完整，不能修改图层");
        _gameExtraAttributes = attrs?.V.Skip(used).ToList() ?? new List<object>();
    }

    static int LayerMask(string layer) => layer switch
    {
        "decor" => 1024, "main" => 2048, "secondary" => 4096,
        _ => throw new FrontEditException("未知图层：" + layer)
    };

    static BtlStruct GameAttribute(TerrainEdits.Cell cell, string layer) => layer switch
    {
        "decor" => cell.Decor, "main" => cell.Main, "secondary" => cell.Secondary,
        _ => throw new FrontEditException("未知图层：" + layer)
    };

    static void SetGameAttribute(TerrainEdits.Cell cell, string layer, BtlStruct attr)
    {
        switch (layer)
        {
            case "decor": cell.Decor = attr; break;
            case "main": cell.Main = attr; break;
            case "secondary": cell.Secondary = attr; break;
        }
    }

    static int AttributeMember(string property) => property switch
    {
        "id" => 0, "variant" => 1, "dx" => 2, "dy" => 3,
        _ => throw new FrontEditException("未知图层属性：" + property)
    };

    DynValue OnGameCellGet(ScriptExecutionContext context, CallbackArguments args)
    {
        Unwrap(args[0]);
        int index = GameCellIndex(args[1]);
        string property = ArgString(args, 2);
        ushort word = GameWord(index);
        if (args.Count < 4 || args[3].IsNil())
            return property switch
            {
                "climate" => DynValue.NewNumber(word & 7),
                "variant" => DynValue.NewNumber((word >> 3) & 31),
                "sea" => DynValue.NewBoolean((word & 512) != 0),
                "playable" => DynValue.NewBoolean((word & 256) == 0),
                _ => throw new FrontEditException("未知格子属性：" + property)
            };
        string layer = ArgString(args, 3);
        bool present = (word & LayerMask(layer)) != 0;
        if (property == "present") return DynValue.NewBoolean(present);
        int member = AttributeMember(property);
        if (!present) return DynValue.Nil;
        ProjectGameCells();
        var attribute = GameAttribute(_gameCells[index], layer);
        if (attribute == null || attribute.V.Count <= member) throw new FrontEditException("地形图层缺少成员");
        return ToDyn(attribute.V[member]);
    }

    DynValue OnGameCellSet(ScriptExecutionContext context, CallbackArguments args)
    {
        RequireWrite(); Unwrap(args[0]);
        int index = GameCellIndex(args[1]);
        string property = ArgString(args, 2);
        object value = FromDyn(args[3]);
        ushort word = GameWord(index);
        if (args.Count < 5 || args[4].IsNil())
        {
            if (property is "sea" or "playable")
            {
                if (value is not bool on) throw new FrontEditException(property + "需要 true 或 false");
                int mask = property == "sea" ? 512 : 256;
                if (property == "playable") on = !on;
                word = (ushort)(on ? word | mask : word & ~mask);
            }
            else if (property is "climate" or "variant")
            {
                int n = Convert.ToInt32(FrontPath.Coerce("u8", value ?? throw new FrontEditException("属性不能为 nil")));
                int max = property == "climate" ? 7 : 31;
                if (n > max) throw new FrontEditException(property + "不能超过 " + max);
                word = (ushort)(property == "climate" ? (word & ~7) | n : (word & ~248) | (n << 3));
            }
            else throw new FrontEditException("未知格子属性：" + property);
            WriteGameWord(index, word);
            return DynValue.Nil;
        }
        string layer = ArgString(args, 4);
        int layerMask = LayerMask(layer);
        ProjectGameCells();
        var cell = _gameCells[index];
        bool present = (word & layerMask) != 0;
        if (property == "present")
        {
            if (value is not bool enabled) throw new FrontEditException("present 需要 true 或 false");
            if (enabled == present) return DynValue.Nil;
            if (enabled) SetGameAttribute(cell, layer, NewGameAttribute());
            else SetGameAttribute(cell, layer, null);
            WriteGameWord(index, (ushort)(enabled ? word | layerMask : word & ~layerMask));
            _gameAttributesDirty = true;
            return DynValue.Nil;
        }
        int member = AttributeMember(property);
        object coerced = FrontPath.Coerce(member < 2 ? "u8" : "i8", value ?? throw new FrontEditException("图层属性不能为 nil"));
        if (!present)
        {
            SetGameAttribute(cell, layer, NewGameAttribute());
            WriteGameWord(index, (ushort)(word | layerMask));
            _gameAttributesDirty = true;
        }
        var attr = GameAttribute(cell, layer) ?? throw new FrontEditException("地形图层数据不完整");
        FrontEdit.SetMember(attr, member, coerced);
        return DynValue.Nil;
    }

    static BtlStruct NewGameAttribute() => FrontEdit.NewStruct(new[] { "u8", "u8", "i8", "i8" });
}
