/*
 * FrontSession.cs
 *
 * 编辑器打开一份关卡时的会话外壳。唯一文档是 BtlFrontDocument：
 *
 *   打开 .btl  → BtlToFront.FromFile
 *   打开 JSON → StageJson.LoadFile（字段名或字段 ID，旧别名也能认）
 *   新建地图 → FrontNav.NewMap
 *   保存 .btl → FrontToBtl.ToFile（直接读取已提交文档，不回写画布）
 *
 * 保留原始文件解释及读档诊断；存在未解析数据时只允许查看，防止导出丢字段。
 */
using BtlCore.Fb;
using BtlCore.Front;

namespace BtldMapEditor.Front
{
    /// <summary>当前正在编辑的关卡：Document + 读档 Issues。</summary>
    public sealed class FrontSession
    {
        public BtlFrontDocument Document { get; private set; }
        public EditSession Editing => Document.Editing;
        public List<ConvertIssue> Issues { get; private set; } = new List<ConvertIssue>();
        public string OpenedKind { get; private set; } = "BtlFront";
        public BtlBinaryInspection Original { get; private set; }
        public string SourcePath { get; private set; }
        public bool CanExport { get; private set; } = true;

        public static FrontSession FromBtl(string path, SchemaContext context = null)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var r = BtlToFront.FromBytes(bytes, context);
            long cells = (long)FrontNav.MapWidth(r.Document) * FrontNav.MapHeight(r.Document);
            if (cells > 1_000_000)
                r.Issues.Add(new ConvertIssue { Severity = IssueSeverity.Error, Path = "/1/0", Message = "地图格子数超过编辑预算，只保留原文件查看" });
            return new FrontSession
            {
                Document = r.Document,
                Issues = r.Issues,
                OpenedKind = "BTL",
                Original = new BtlBinaryInspection(bytes, r.Document.Context.Schema),
                SourcePath = Path.GetFullPath(path),
                CanExport = r.Ok && !r.HasUnresolvedData
            };
        }

        public static FrontSession FromJsonFile(string path, SchemaContext context = null)
        {
            var notes = new List<string>();
            var doc = StageJson.LoadFile(path, notes: notes, context: context ?? SoftSchema.DefaultContext);
            if (doc?.Root == null)
                throw new InvalidDataException("无法从 JSON 读出关卡");
            var issues = new List<ConvertIssue>();
            issues.AddRange(doc.SourceIssues);
            foreach (var n in notes)
            {
                issues.Add(new ConvertIssue
                {
                    Severity = IssueSeverity.Info,
                    Path = "/",
                    Message = n
                });
            }
            return new FrontSession { Document = doc, Issues = issues, OpenedKind = "JSON", CanExport = !doc.HasUnresolvedData };
        }

        /// <summary>从工作文档 JSON 恢复数据及解释来源。</summary>
        public static FrontSession FromJson(string json, SchemaContext context = null)
        {
            return FromDocument(BtlFrontJson.Parse(json, context));
        }

        public static FrontSession FromDocument(BtlFrontDocument doc)
        {
            return new FrontSession
            {
                Document = doc ?? new BtlFrontDocument { Root = BtlFrontJson.NewTable() },
                OpenedKind = "BtlFront",
                CanExport = !(doc?.HasUnresolvedData ?? false)
            };
        }

        public static FrontSession NewMap(ushort w, ushort h, ushort lm, ushort tm, ushort pw, ushort ph,
            ushort roundLimit, ushort version, SchemaContext context = null)
        {
            return FromDocument(FrontNav.NewMap(w, h, lm, tm, pw, ph, roundLimit, version,context));
        }

        public string ToJson() => BtlFrontJson.Serialize(Document);

        public void SaveFront(string path)
        {
            EnsureCanExport();
            BtlFrontJson.SaveFile(Document, path);
        }

        /// <summary>用 FrontToBtl 整文件重建 .btl。二进制不是当场打补丁。</summary>
        public void SaveBtl(string path)
        {
            EnsureCanExport();
            FrontToBtl.ToFile(Document, path);
        }

        public void EnsureCanExport()
        {
            if (!CanExport || Document.HasUnresolvedData) throw new InvalidDataException("原文件存在未解析或异常数据，当前文档不能安全重建导出。请在注册表原始文件视图中核对字段和诊断。");
        }

        /// <summary>撤销/重做替换整棵树时用。null 则换成空 Root。</summary>
        public void ReplaceDocument(BtlFrontDocument doc)
        {
            Document = doc ?? new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        }
    }
}
