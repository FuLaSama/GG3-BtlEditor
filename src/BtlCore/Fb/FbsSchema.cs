/*
 * FbsSchema.cs
 *
 * 解析 FlatBuffers schema 文本（battle.fbs，可 include 其它 .fbs）。
 * 产出：每个 table/struct 的 field id → 名字、类型、行尾注释。
 *
 * 类型规范化：
 *   [ushort]     → vector_ushort
 *   MapTerrain   → 原样（再靠 IsTable / IsStruct 判断）
 *   (id: N)      → 字段号 N；没有 id 时按声明顺序 0,1,2…
 *
 * 这是「类型字典」，不是关卡数据。BtlToFront / FrontToBtl 通过 SoftSchema 用它认字段；
 * 编辑器注册表用它显示名字。_Meta / _Enum_* 也会被解析成 struct，但没有字段指向它们。
 */
using System.Text.RegularExpressions;

namespace BtlCore.Fb
{
    /// <summary>fbs 里一条字段声明。</summary>
    public sealed class FbsField
    {
        public string Name { get; init; }
        /// <summary>已去掉 (id:n) 的类型：标量名、表名、或 vector_X。</summary>
        public string Type { get; init; }
        /// <summary>vtable 槽号，对应 Front 里 table.F 的键。</summary>
        public int Id { get; init; }
        /// <summary>行尾 // 注释，给注册表当中文说明。</summary>
        public string Comment { get; init; }
    }

    /// <summary>一份已解析的 .fbs。名字查找忽略大小写。</summary>
    public sealed class FbsSchema
    {
        public string SourcePath { get; private set; }
        public string RootType { get; private set; } = "Root";

        readonly Dictionary<string, Dictionary<int, FbsField>> _byId =
            new Dictionary<string, Dictionary<int, FbsField>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<FbsField>> _structOrder =
            new Dictionary<string, List<FbsField>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _structs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static FbsSchema LoadFile(string path)
        {
            var s = new FbsSchema();
            s.LoadFromFbs(path);
            return s;
        }

        /// <summary>从字符串解析（嵌入资源、单元测试）。include 在没有目录时会被忽略。</summary>
        public static FbsSchema LoadFromText(string content, string sourceName = "battle.fbs")
        {
            var s = new FbsSchema { SourcePath = sourceName ?? "battle.fbs" };
            s.ParseDocument(content ?? "", null);
            return s;
        }

        /// <summary>从当前目录 / 程序目录向上找 schema/battle.fbs，再退回历史路径或旁边的 battle.fbs。</summary>
        public static string FindNearby(params string[] extraStarts)
        {
            var starts = new List<string>();
            if (extraStarts != null)
                starts.AddRange(extraStarts.Where(p => !string.IsNullOrEmpty(p)));
            starts.Add(Directory.GetCurrentDirectory());
            starts.Add(AppContext.BaseDirectory);
            foreach (var start in starts)
            {
                DirectoryInfo dir;
                try { dir = new DirectoryInfo(start); }
                catch { continue; }
                string schemaHit = WalkFor(dir, d => Path.Combine(d, "schema", "battle.fbs"));
                if (schemaHit != null) return schemaHit;
                string legacyHit = WalkFor(dir, d =>
                {
                    string tools = Path.Combine(d, "tools", "TestConsole", "battle.fbs");
                    if (File.Exists(tools)) return tools;
                    string old = Path.Combine(d, "TestConsole", "battle.fbs");
                    if (File.Exists(old)) return old;
                    string local = Path.Combine(d, "battle.fbs");
                    return File.Exists(local) ? local : null;
                });
                if (legacyHit != null) return legacyHit;
            }
            return null;
        }

        static string WalkFor(DirectoryInfo dir, Func<string, string> pick)
        {
            while (dir != null)
            {
                string hit = pick(dir.FullName);
                if (!string.IsNullOrEmpty(hit) && File.Exists(hit))
                    return Path.GetFullPath(hit);
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// 在目录内找 .fbs：优先 battle.fbs，否则第一份 *.fbs。
        /// </summary>
        public static string FindInDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;
            string battle = Path.Combine(directory, "battle.fbs");
            if (File.Exists(battle)) return battle;
            return Directory.GetFiles(directory, "*.fbs")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public bool IsTable(string name) => !string.IsNullOrEmpty(name) && _tables.Contains(name);
        public bool IsStruct(string name) => !string.IsNullOrEmpty(name) && _structs.Contains(name);

        public bool TryGetField(string typeName, int fieldId, out FbsField field)
        {
            field = null;
            if (string.IsNullOrEmpty(typeName)) return false;
            if (!_byId.TryGetValue(typeName, out var map)) return false;
            return map.TryGetValue(fieldId, out field);
        }

        public string FieldNameOrId(string typeName, int fieldId)
        {
            if (TryGetField(typeName, fieldId, out var f) && !string.IsNullOrEmpty(f.Name))
                return f.Name;
            return fieldId.ToString();
        }

        static readonly Regex AliasRe = new Regex(@"@alias\(([A-Za-z_][A-Za-z0-9_]*)\)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>注释里的 @alias(旧名)，给导入旧 JSON 用。</summary>
        public static string CommentAlias(string comment)
        {
            if (string.IsNullOrEmpty(comment)) return null;
            var m = AliasRe.Match(comment);
            return m.Success ? m.Groups[1].Value : null;
        }

        public bool TryGetFieldByName(string typeName, string fieldName, out FbsField field)
        {
            field = null;
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(fieldName)) return false;
            FbsField aliasHit = null;
            foreach (var f in FieldsOf(typeName))
            {
                if (string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    field = f;
                    return true;
                }
                if (aliasHit == null)
                {
                    string alias = CommentAlias(f.Comment);
                    if (!string.IsNullOrEmpty(alias)
                        && string.Equals(alias, fieldName, StringComparison.OrdinalIgnoreCase))
                        aliasHit = f;
                }
            }
            if (aliasHit != null)
            {
                field = aliasHit;
                return true;
            }
            return false;
        }

        public IReadOnlyList<FbsField> StructMembers(string structName)
        {
            if (structName != null && _structOrder.TryGetValue(structName, out var list))
                return list;
            return Array.Empty<FbsField>();
        }

        /// <summary>表或 struct 的字段，按 .fbs 声明顺序。</summary>
        public IReadOnlyList<FbsField> FieldsOf(string typeName) => StructMembers(typeName);

        public static bool IsVectorType(string t) =>
            !string.IsNullOrEmpty(t) && t.StartsWith("vector_", StringComparison.OrdinalIgnoreCase);

        public static bool IsStringType(string t) =>
            string.Equals(t, "string", StringComparison.OrdinalIgnoreCase);

        public static int ScalarByteSize(string t) => t switch
        {
            "bool" or "ubyte" or "uint8" or "byte" or "int8" => 1,
            "ushort" or "uint16" or "short" or "int16" => 2,
            "uint" or "uint32" or "int" or "int32" or "float" => 4,
            "ulong" or "uint64" or "long" or "int64" or "double" => 8,
            _ => 0
        };

        public static bool IsScalarType(string t) => ScalarByteSize(t) > 0;

        /// <summary>从字段类型推出子表/结构名；向量则去 vector_ 前缀。</summary>
        public string ResolveChildType(string fieldType)
        {
            if (string.IsNullOrEmpty(fieldType)) return null;
            if (fieldType.StartsWith("vector_", StringComparison.OrdinalIgnoreCase))
                return fieldType.Substring("vector_".Length);
            return fieldType;
        }

        void LoadFromFbs(string path)
        {
            SourcePath = Path.GetFullPath(path);
            _loaded.Clear();
            _byId.Clear();
            _structOrder.Clear();
            _tables.Clear();
            _structs.Clear();
            LoadRecursive(SourcePath);
        }

        void LoadRecursive(string path)
        {
            path = Path.GetFullPath(path);
            if (!_loaded.Add(path) || !File.Exists(path)) return;
            ParseDocument(File.ReadAllText(path), Path.GetDirectoryName(path));
        }

        void ParseDocument(string content, string dir)
        {
            // 先剥 /* */，避免注释里的 table 关键字干扰。行注释在 ParseType 里按行切。
            content = Regex.Replace(content ?? "", @"/\*.*?\*/", "", RegexOptions.Singleline);

            if (!string.IsNullOrEmpty(dir))
            {
                foreach (Match m in Regex.Matches(content, @"include\s+""([^""]+)""\s*;"))
                {
                    string inc = Path.Combine(dir, m.Groups[1].Value);
                    LoadRecursive(inc);
                }
            }

            var root = Regex.Match(content, @"root_type\s+(\w+)\s*;");
            if (root.Success)
                RootType = root.Groups[1].Value;

            foreach (Match m in Regex.Matches(content, @"(table|struct)\s+(\w+)\s*\{([^}]*)\}", RegexOptions.Singleline))
            {
                string kind = m.Groups[1].Value;
                string name = m.Groups[2].Value;
                string body = m.Groups[3].Value;
                ParseType(kind, name, body);
            }
        }

        void ParseType(string kind, string name, string body)
        {
            bool isTable = kind == "table";
            if (isTable) _tables.Add(name);
            else _structs.Add(name);

            var byId = new Dictionary<int, FbsField>();
            var order = new List<FbsField>();
            // 没有 (id:n) 时按 FlatBuffers 规则：声明序即 id，遇到显式 id 后下一个自动 +1。
            int autoId = 0;

            foreach (var rawLine in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

                string comment = "";
                int cmt = line.IndexOf("//", StringComparison.Ordinal);
                if (cmt >= 0)
                {
                    comment = line.Substring(cmt + 2).Trim();
                    line = line.Substring(0, cmt).Trim();
                }

                var match = Regex.Match(line, @"^(\w+)\s*:\s*([^;]+)\s*;?$");
                if (!match.Success) continue;

                string fieldName = match.Groups[1].Value.Trim();
                string typePart = match.Groups[2].Value.Trim();

                int id = autoId;
                var idMatch = Regex.Match(typePart, @"\(id:\s*(\d+)\)");
                if (idMatch.Success)
                    id = int.Parse(idMatch.Groups[1].Value);

                string cleanType = Regex.Replace(typePart, @"\(id:\s*\d+\)", "").Trim();
                cleanType = Regex.Replace(cleanType, @"@enum\(\w+\)", "").Trim();
                // 去掉剩余属性括号，例如 (deprecated)
                cleanType = Regex.Replace(cleanType, @"\s*\(.*?\)", "").Trim();
                if (cleanType.StartsWith("[") && cleanType.EndsWith("]"))
                    cleanType = "vector_" + cleanType.Substring(1, cleanType.Length - 2).Trim();

                var field = new FbsField
                {
                    Name = fieldName,
                    Type = cleanType,
                    Id = id,
                    Comment = comment
                };
                byId[id] = field;
                order.Add(field);
                autoId = id + 1;
            }

            _byId[name] = byId;
            // 表和 struct 都保留声明顺序：struct 用来逐步长，表给注册表「按 schema 补空槽」用。
            _structOrder[name] = order;
        }
    }
}
