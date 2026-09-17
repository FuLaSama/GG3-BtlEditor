/*
 * FrontSession.cs
 *
 * 编辑器打开一份关卡时的会话外壳。唯一文档是 BtlFrontDocument：
 *
 *   打开 .btl  → BtlToFront.FromFile
 *   打开 JSON → StageJson.LoadFile（字段名或字段 ID，旧别名也能认）
 *   新建地图 → FrontNav.NewMap
 *   保存 .btl → FrontToBtl.ToFile（调用方应先 FrontNav.SyncGrid）
 *
 * Issues 是打开时的读档提示，不阻止编辑。不经过 StageModel / BtlCore.old。
 */
using BtlCore.Fb;
using BtlCore.Front;

namespace BtldMapEditor.Front
{
    /// <summary>当前正在编辑的关卡：Document + 读档 Issues。</summary>
    public sealed class FrontSession
    {
        public BtlFrontDocument Document { get; private set; }
        public List<ConvertIssue> Issues { get; private set; } = new List<ConvertIssue>();
        public string OpenedKind { get; private set; } = "BtlFront";

        public static FrontSession FromBtl(string path)
        {
            var r = BtlToFront.FromFile(path);
            if (r.Document?.Root == null)
                throw new InvalidDataException("无法从 BTL 读出 BtlFront");
            return new FrontSession { Document = r.Document, Issues = r.Issues, OpenedKind = "BTL" };
        }

        public static FrontSession FromJsonFile(string path)
        {
            var notes = new List<string>();
            var doc = StageJson.LoadFile(path, SoftSchema.Schema, notes);
            if (doc?.Root == null)
                throw new InvalidDataException("无法从 JSON 读出关卡");
            var issues = new List<ConvertIssue>();
            foreach (var n in notes)
            {
                issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Info,
                    Path = "/",
                    Message = n
                });
            }
            return new FrontSession { Document = doc, Issues = issues, OpenedKind = "JSON" };
        }

        /// <summary>撤销/重做：整份 BtlFront JSON 快照。</summary>
        public static FrontSession FromJson(string json)
        {
            return new FrontSession { Document = BtlFrontJson.Parse(json), OpenedKind = "BtlFront" };
        }

        public static FrontSession FromDocument(BtlFrontDocument doc)
        {
            return new FrontSession
            {
                Document = doc ?? new BtlFrontDocument { Root = BtlFrontJson.NewTable() },
                OpenedKind = "BtlFront"
            };
        }

        public static FrontSession NewMap(ushort w, ushort h, ushort lm, ushort tm, ushort pw, ushort ph,
            ushort roundLimit, ushort version)
        {
            return FromDocument(FrontNav.NewMap(w, h, lm, tm, pw, ph, roundLimit, version));
        }

        public string ToJson() => BtlFrontJson.Serialize(Document);

        public void SaveFront(string path) => BtlFrontJson.SaveFile(Document, path);

        /// <summary>用 FrontToBtl 整文件重建 .btl。二进制不是当场打补丁。</summary>
        public void SaveBtl(string path)
        {
            FrontToBtl.ToFile(Document, path);
        }

        /// <summary>撤销/重做替换整棵树时用。null 则换成空 Root。</summary>
        public void ReplaceDocument(BtlFrontDocument doc)
        {
            Document = doc ?? new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        }
    }
}
