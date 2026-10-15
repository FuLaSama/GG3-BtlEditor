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
        /// <summary>schema 声明的缺省值原文；null 表示采用类型的隐式缺省值。</summary>
        public string DefaultLiteral { get; init; }
        public bool Required { get; init; }
    }

    /// <summary>一份已解析的 .fbs。名字查找忽略大小写。</summary>
    public sealed class FbsSchema
    {
        readonly List<string> _sourceHashes = new();
        /// <summary>所有已解析文本（含 include）的内容指纹，不依赖本机路径。</summary>
        public string ContentHash => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", _sourceHashes))));
        public string SourcePath { get; private set; }
        public string RootType { get; private set; } = "Root";

        readonly Dictionary<string, Dictionary<int, FbsField>> _byId =
            new Dictionary<string, Dictionary<int, FbsField>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<FbsField>> _structOrder =
            new Dictionary<string, List<FbsField>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _structs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int _schemaTextSize;
        readonly Dictionary<string, string> _enumTypes = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dictionary<string, long>> _enumValues = new(StringComparer.OrdinalIgnoreCase);

        public string ResolveScalarType(string type) =>
            type != null && _enumTypes.TryGetValue(type, out var scalar) ? scalar : type;

        public bool TryEnumValue(string type, string name, out long value)
        {
            value = 0;
            return type != null && _enumValues.TryGetValue(type, out var values) && values.TryGetValue(name, out value);
        }

        public static FbsSchema LoadFile(string path)
        {
            var s = new FbsSchema();
            s.LoadFromFbs(path);
            return s;
        }

        /// <summary>从字符串解析（研究覆盖、单元测试）。include 在没有目录时会被忽略。</summary>
        public static FbsSchema LoadFromText(string content, string sourceName = "battle.fbs")
        {
            var s = new FbsSchema { SourcePath = sourceName ?? "battle.fbs" };
            s.ParseDocument(content ?? "", null);
            return s;
        }

        /// <summary>复制后应用研究扩展。表按显式字段 ID 合并，struct 整体替换；不修改原 schema。</summary>
        public FbsSchema WithOverlays(IEnumerable<string> definitions)
        {
            var result = new FbsSchema { SourcePath = SourcePath, RootType = RootType, _schemaTextSize = _schemaTextSize };
            result._sourceHashes.AddRange(_sourceHashes);
            foreach (var pair in _byId) result._byId.Add(pair.Key, new(pair.Value));
            foreach (var pair in _structOrder) result._structOrder.Add(pair.Key, new(pair.Value));
            result._tables.UnionWith(_tables);
            result._structs.UnionWith(_structs);
            foreach (var pair in _enumTypes) result._enumTypes.Add(pair.Key, pair.Value);
            foreach (var pair in _enumValues) result._enumValues.Add(pair.Key, new(pair.Value));
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string definition in definitions)
            {
                if (definition == null || definition.Length > 1_000_000) throw new InvalidDataException("格式声明为空或超过预算");
                string clean = Regex.Replace(definition, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
                const string declaration = @"(table|struct)\s+(\w+)\s*\{([^}]*)\}";
                var matches = Regex.Matches(clean, declaration, RegexOptions.Singleline);
                if (matches.Count == 0 || !string.IsNullOrWhiteSpace(Regex.Replace(clean, declaration, "", RegexOptions.Singleline)))
                    throw new InvalidDataException("格式扩展只接受 table/struct 声明，不支持 include、root_type 或其他声明");
                foreach (Match match in matches)
                    if (match.Groups[1].Value == "table")
                        foreach (string field in match.Groups[3].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                            if (!string.IsNullOrWhiteSpace(field) && !Regex.IsMatch(field, @"\(\s*id\s*:\s*\d+\s*\)"))
                                throw new InvalidDataException("扩展 table 的每个字段必须明确写出 (id: N)");
                var patch = LoadFromText(definition, "Lua schema");
                result._schemaTextSize = checked(result._schemaTextSize + definition.Length);
                if (result._schemaTextSize > 8_000_000) throw new InvalidDataException("schema 总文本超过支持的预算");
                result._sourceHashes.AddRange(patch._sourceHashes);
                foreach (var pair in patch._byId)
                {
                    string name = pair.Key;
                    bool structure = patch.IsStruct(name);
                    if (result._enumTypes.ContainsKey(name) || result.IsTable(name) && structure || result.IsStruct(name) && !structure)
                        throw new InvalidDataException("不能改变已有类型的种类：" + name);
                    var fields = !structure && result._byId.TryGetValue(name, out var existing)
                        ? new Dictionary<int, FbsField>(existing) : new Dictionary<int, FbsField>();
                    foreach (var field in pair.Value)
                    {
                        if (!structure && field.Key > 511) throw new InvalidDataException("BTL 字段 ID 只能为 0–511");
                        fields[field.Key] = field.Value;
                    }
                    if (fields.Values.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count)
                        throw new InvalidDataException("合并后字段名重复：" + name);
                    result._byId[name] = fields;
                    result._structOrder[name] = structure ? new(patch._structOrder[name]) : fields.OrderBy(p => p.Key).Select(p => p.Value).ToList();
                    if (structure) result._structs.Add(name); else result._tables.Add(name);
                    changed.Add(name);
                }
            }
            foreach (string name in changed)
                foreach (var field in result.FieldsOf(name))
                {
                    string type = result.ResolveScalarType(result.ResolveChildType(field.Type));
                    if (result.IsStruct(name) && !IsScalarType(type))
                        throw new InvalidDataException("扩展 struct 目前只支持标量成员：" + name + "." + field.Name);
                    if (!IsScalarType(type) && !IsStringType(type) && !result.IsTable(type) && !result.IsStruct(type))
                        throw new InvalidDataException("未定义的扩展类型：" + type);
                }
            return result;
        }

        /// <summary>默认格式文件只取运行程序同目录，不搜索工作区、当前目录或上级目录。</summary>
        public static string ProgramSchemaPath => Path.Combine(AppContext.BaseDirectory,"battle.fbs");

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

        /// <summary>隐藏机器使用的别名标记，保留其前后的人类说明。</summary>
        public static string DisplayComment(string comment) =>
            string.IsNullOrWhiteSpace(comment) ? null : AliasRe.Replace(comment, "").Trim();

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
            _enumTypes.Clear();
            _enumValues.Clear();
            _schemaTextSize = 0;
            LoadRecursive(SourcePath);
        }

        void LoadRecursive(string path)
        {
            path = Path.GetFullPath(path);
            if (!_loaded.Add(path)) return;
            if (_loaded.Count > 256) throw new InvalidDataException("schema include 文件数超过支持的预算");
            if (!File.Exists(path)) throw new FileNotFoundException("找不到 schema 或 include 文件", path);
            if (new FileInfo(path).Length > 16_000_000) throw new InvalidDataException("schema 文件过大");
            ParseDocument(File.ReadAllText(path), Path.GetDirectoryName(path));
        }

        void ParseDocument(string content, string dir)
        {
            _sourceHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")))));
            _schemaTextSize = checked(_schemaTextSize + (content?.Length ?? 0));
            if (_schemaTextSize > 8_000_000) throw new InvalidDataException("schema 总文本超过支持的预算");
            // 先剥 /* */，避免注释里的 table 关键字干扰。行注释在 ParseType 里按行切。
            content = Regex.Replace(content ?? "", @"/\*.*?\*/", "", RegexOptions.Singleline);
            content = Regex.Replace(content, @"(?m)^\s*//[^\r\n]*", "");
            string declarations = Regex.Replace(content, @"//[^\r\n]*", m => new string(' ', m.Length));

            if (!string.IsNullOrEmpty(dir))
            {
                foreach (Match m in Regex.Matches(declarations, @"include\s+""([^""]+)""\s*;"))
                {
                    string inc = Path.Combine(dir, m.Groups[1].Value);
                    LoadRecursive(inc);
                }
            }

            var root = Regex.Match(declarations, @"root_type\s+([^;]+)\s*;");
            if (root.Success)
            {
                string rootType = root.Groups[1].Value.Trim();
                if (!Regex.IsMatch(rootType, @"^\w+$")) throw new InvalidDataException("暂不支持带限定名的 root_type: " + rootType);
                RootType = rootType;
            }

            foreach (Match m in Regex.Matches(declarations, @"enum\s+(\w+)\s*:\s*(\w+)\s*\{([^}]*)\}", RegexOptions.Singleline))
            {
                string name = m.Groups[1].Value;
                if (_enumTypes.ContainsKey(name) || _byId.ContainsKey(name)) throw new InvalidDataException("schema 类型名重复: " + name);
                _enumTypes[name] = m.Groups[2].Value;
                var values = new Dictionary<string, long>(StringComparer.Ordinal);
                long next = 0;
                string body = Regex.Replace(m.Groups[3].Value, @"//[^\r\n]*", "");
                foreach (string entry in body.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = Regex.Match(entry.Trim(), @"^(\w+)(?:\s*=\s*(-?\d+))?$");
                    if (!item.Success) throw new InvalidDataException("不支持的 enum 声明: " + entry.Trim());
                    if (item.Groups[2].Success) next = long.Parse(item.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    values.Add(item.Groups[1].Value, next);
                    next = checked(next + 1);
                }
                _enumValues[name] = values;
            }

            foreach (Match m in Regex.Matches(declarations, @"(table|struct)\s+(\w+)\s*\{([^}]*)\}", RegexOptions.Singleline))
            {
                string kind = m.Groups[1].Value;
                string name = m.Groups[2].Value;
                string body = content.Substring(m.Groups[3].Index, m.Groups[3].Length);
                ParseType(kind, name, body);
            }
        }

        void ParseType(string kind, string name, string body)
        {
            if (_byId.ContainsKey(name) || _enumTypes.ContainsKey(name)) throw new InvalidDataException("schema 类型名重复: " + name);
            bool isTable = kind == "table";
            if (isTable) _tables.Add(name);
            else _structs.Add(name);

            var byId = new Dictionary<int, FbsField>();
            var order = new List<FbsField>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            // 没有 (id:n) 时按 FlatBuffers 规则：声明序即 id，遇到显式 id 后下一个自动 +1。
            int autoId = 0;
            const string fieldPattern = @"(\w+)\s*:\s*([^;]+);[ \t]*(?://([^\r\n]*))?";
            string remainder = Regex.Replace(body, fieldPattern, "");
            remainder = Regex.Replace(remainder, @"//[^\r\n]*", "");
            if (!string.IsNullOrWhiteSpace(remainder)) throw new InvalidDataException($"{name} 包含不支持或不完整的字段声明: {remainder.Trim()}");

            foreach (Match declaration in Regex.Matches(body, fieldPattern))
            {
                string comment = declaration.Groups[3].Value.Trim();
                string fieldName = declaration.Groups[1].Value.Trim();
                string typePart = declaration.Groups[2].Value.Trim();

                int id = autoId;
                var idMatch = Regex.Match(typePart, @"\bid\s*:\s*([+-]?\d+)\b");
                if (idMatch.Success)
                    id = int.Parse(idMatch.Groups[1].Value);

                string cleanType = Regex.Replace(typePart, @"\(id:\s*\d+\)", "").Trim();
                cleanType = Regex.Replace(cleanType, @"@enum\(\w+\)", "").Trim();
                // 去掉剩余属性括号，例如 (deprecated)
                cleanType = Regex.Replace(cleanType, @"\s*\(.*?\)", "").Trim();
                string defaultLiteral = null;
                int equals = cleanType.IndexOf('=');
                if (equals >= 0)
                {
                    defaultLiteral = cleanType[(equals + 1)..].Trim();
                    cleanType = cleanType[..equals].Trim();
                }
                if (cleanType.StartsWith("[") && cleanType.EndsWith("]"))
                    cleanType = "vector_" + cleanType.Substring(1, cleanType.Length - 2).Trim();

                var field = new FbsField
                {
                    Name = fieldName,
                    Type = cleanType,
                    Id = id,
                    Comment = comment,
                    DefaultLiteral = defaultLiteral,
                    Required = Regex.IsMatch(typePart, @"\([^)]*\brequired\b[^)]*\)")
                };
                if (id < 0 || id > 32764 || byId.ContainsKey(id))
                    throw new InvalidDataException($"{name} 字段 ID 重复或超出范围: {id}");
                if (!names.Add(fieldName)) throw new InvalidDataException($"{name} 字段名重复: {fieldName}");
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
