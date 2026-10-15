using System.Xml;

namespace BtlCore.Scripting
{
    public static class LayoutLoader
    {
        public static LayoutLoadResult Load(string builtinDir, string userDir, BtlCore.Fb.SchemaContext context = null)
        {
            var result = new LayoutLoadResult { Bundle = new LayoutBundle() };
            if (string.IsNullOrEmpty(builtinDir) || !File.Exists(Path.Combine(builtinDir, "layout.xml")))
            {
                result.Errors.Add("找不到 EditorLayout/layout.xml");
                return result;
            }
            try
            {
                LoadRoot(builtinDir, result.Bundle, result.Errors, userOverride: false);
                bool hasUserLayout = !string.IsNullOrEmpty(userDir) && File.Exists(Path.Combine(userDir, "layout.xml"));
                if (hasUserLayout)
                    LoadRoot(userDir, result.Bundle, result.Errors, userOverride: true);
                if (!string.IsNullOrEmpty(userDir) && Directory.Exists(userDir))
                    LoadLooseScripts(userDir, result.Bundle, result.Errors, includeScripts: !hasUserLayout);
                result.Bundle.Fields.ValidateReferences();
                foreach (var tab in result.Bundle.Tabs)
                    if (tab.Page != null) result.Bundle.Fields.Bind(tab.Page, context ?? BtlCore.Fb.SoftSchema.DefaultContext);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }
            result.Ok = result.Errors.Count == 0;
            if (!result.Ok) result.Bundle = null;
            return result;
        }

        public static void MarkMissingActions(LayoutPage page, Func<string, bool> canRun, Func<string, bool> canGet)
        {
            if (page == null) return;
            foreach (var section in page.Sections)
            {
                foreach (var field in section.Fields)
                {
                    if (string.IsNullOrEmpty(field.Action) || field.ReadOnly) continue;
                    bool ok = canRun(field.Action) && (canGet == null || canGet(field.Action));
                    if (!ok)
                    {
                        field.Enabled = false;
                        field.DisableReason = "找不到操作 " + field.Action;
                    }
                }
                foreach (var cmd in section.Commands)
                {
                    if (string.IsNullOrEmpty(cmd.Handler) && (string.IsNullOrEmpty(cmd.Script) || !canRun(cmd.Script)))
                    {
                        cmd.Enabled = false;
                        cmd.DisableReason = "找不到操作 " + cmd.Script;
                    }
                }
            }
        }

        static void LoadRoot(string dir, LayoutBundle bundle, List<string> errors, bool userOverride)
        {
            string layoutPath = Path.Combine(dir, "layout.xml");
            var doc = ReadXml(layoutPath);
            var root = doc.DocumentElement;
            if (root == null || root.LocalName != "editorLayout")
            {
                errors.Add(layoutPath + ": 根元素必须是 editorLayout");
                return;
            }
            if (root.GetAttribute("format") != "1")
                errors.Add(layoutPath + ": format 必须为 1");
            RejectUnknown(root, new[] { "format" }, layoutPath, errors);
            foreach (XmlNode child in root.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName == "tabs")
                    LoadTabs(dir, child, bundle, errors, userOverride, layoutPath);
                else if (child.LocalName == "scripts")
                    LoadScripts(dir, child, bundle, errors, layoutPath);
                else if (child.LocalName == "fields")
                {
                    RejectUnknown(child, new[] { "src" }, layoutPath, errors);
                    string source = ((XmlElement)child).GetAttribute("src");
                    if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException(layoutPath + ": fields 缺少 src");
                    bundle.Fields = bundle.Fields.Overlay(FieldCatalog.Parse(File.ReadAllText(Path.Combine(dir, source))));
                }
                else
                    errors.Add(layoutPath + ": 未知元素 " + child.LocalName);
            }
        }

        static void LoadTabs(string dir, XmlNode tabs, LayoutBundle bundle, List<string> errors, bool userOverride, string file)
        {
            RejectUnknown(tabs, Array.Empty<string>(), file, errors);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode child in tabs.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "tab")
                {
                    errors.Add(file + ": 未知元素 " + child.LocalName);
                    continue;
                }
                var el = (XmlElement)child;
                RejectUnknown(el, new[] { "id", "title", "page", "kind", "placement" }, file, errors);
                string id = el.GetAttribute("id");
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    errors.Add(file + ": tab id 重复或为空");
                var tab = new LayoutTab
                {
                    Id = id,
                    Title = el.GetAttribute("title"),
                    Builtin = el.GetAttribute("kind") == "builtin",
                    Placement = el.GetAttribute("placement")
                };
                if (!tab.Builtin)
                {
                    string pageRel = el.GetAttribute("page");
                    if (string.IsNullOrEmpty(pageRel))
                        errors.Add(file + ": tab " + id + " 缺少 page");
                    else
                    {
                        tab.PagePath = Path.GetFullPath(Path.Combine(dir, pageRel));
                        if (!File.Exists(tab.PagePath))
                            errors.Add(tab.PagePath + ": 页面文件不存在");
                        else
                            tab.Page = LoadPage(tab.PagePath, id, errors);
                    }
                }
                int existing = bundle.Tabs.FindIndex(t => t.Id == id);
                if (existing >= 0 && userOverride)
                    bundle.Tabs[existing] = tab;
                else if (existing < 0)
                    bundle.Tabs.Add(tab);
            }
        }

        static LayoutPage LoadPage(string path, string tabId, List<string> errors)
        {
            var doc = ReadXml(path);
            var root = doc.DocumentElement;
            if (root == null || root.LocalName != "page")
            {
                errors.Add(path + ": 根元素必须是 page");
                return null;
            }
            RejectUnknown(root, new[] { "id" }, path, errors);
            if (root.GetAttribute("id") != tabId)
                errors.Add(path + ": page id 必须与 tab id 相同");
            var page = new LayoutPage { Id = root.GetAttribute("id") };
            foreach (XmlNode child in root.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "section")
                {
                    errors.Add(path + ": 未知元素 " + child.LocalName);
                    continue;
                }
                page.Sections.Add(LoadSection((XmlElement)child, path, errors));
            }
            return page;
        }

        static LayoutSection LoadSection(XmlElement el, string path, List<string> errors)
        {
            RejectUnknown(el, new[] { "id", "title", "kind", "bind", "resolve", "text", "filter", "collapsible", "collapsed", "model", "selection", "omit", "labels", "browse", "prepare" }, path, errors);
            var section = new LayoutSection
            {
                Title = el.GetAttribute("title"),
                Kind = el.GetAttribute("kind"),
                Bind = el.GetAttribute("bind"),
                Resolve = el.GetAttribute("resolve"),
                Id = el.GetAttribute("id"),
                Text = el.GetAttribute("text"),
                Filter = el.GetAttribute("filter"),
                Collapsible = Flag(el,"collapsible",path), Collapsed = Flag(el,"collapsed",path),
                Model = el.GetAttribute("model"), Selection = el.GetAttribute("selection"), Omit = el.GetAttribute("omit"),
                Browse = Flag(el,"browse",path), Prepare = el.GetAttribute("prepare")
            };
            if (section.Collapsed && !section.Collapsible) throw new InvalidDataException(path+": collapsed 需要 collapsible=true");
            if (section.Browse && (section.Kind!="details" || string.IsNullOrEmpty(section.Resolve) && string.IsNullOrEmpty(section.Selection)))
                throw new InvalidDataException(path+": browse 需要 details 栏目及 resolve 或 selection");
            if (!string.IsNullOrEmpty(section.Prepare) && (section.Kind!="details" || section.Browse || string.IsNullOrEmpty(section.Resolve)))
                throw new InvalidDataException(path+": prepare 需要按地图定位的 details 栏目，不能与 browse 混用");
            if (section.Kind == "details")
            {
                if (string.IsNullOrEmpty(section.Model) || string.IsNullOrEmpty(section.Bind) || string.IsNullOrEmpty(el.GetAttribute("labels")))
                    throw new InvalidDataException(path+": details 需要 model、bind 和 labels");
                section.Labels = ResearchLabels.Load(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,el.GetAttribute("labels"))));
            }
            if (section.Kind != "form" && section.Kind != "list" && section.Kind != "tree" && section.Kind != "palettes" && section.Kind != "label" && section.Kind != "details")
                errors.Add(path + ": 不支持的栏目 " + section.Kind);
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName == "field" && section.Kind == "form")
                    section.Fields.Add(LoadField((XmlElement)child, path, errors));
                else if (child.LocalName == "group")
                    section.Groups.Add(LoadGroup((XmlElement)child, section, path, errors));
                else if (child.LocalName == "row")
                    section.Rows.Add(LoadRow((XmlElement)child, section, path, errors));
                else if (child.LocalName == "columns")
                    LoadColumns(child, section, path, errors);
                else if (child.LocalName == "fields")
                    LoadFields(child, section, path, errors);
                else if (child.LocalName == "actions")
                    LoadCommands(child, section, path, errors);
                else if (child.LocalName == "palette" && section.Kind == "palettes")
                {
                    var palette = (XmlElement)child;
                    RejectUnknown(palette, new[] { "title", "target" }, path, errors);
                    string target = palette.GetAttribute("target");
                    if (target != "Climate" && target != "Main" && target != "Secondary" && target != "Decor")
                        errors.Add(path + ": 未知贴图目标 " + target);
                    section.Palettes.Add(new LayoutPalette { Title = palette.GetAttribute("title"), Target = target });
                }
                else
                    errors.Add(path + ": 未知元素 " + child.LocalName);
            }
            return section;
        }

        static void LoadColumns(XmlNode node, LayoutSection section, string path, List<string> errors)
        {
            RejectUnknown(node, Array.Empty<string>(), path, errors);
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "column")
                {
                    errors.Add(path + ": 未知元素 " + child.LocalName);
                    continue;
                }
                var el = (XmlElement)child;
                RejectUnknown(el, new[] { "header", "bind", "field" }, path, errors);
                if (el.HasAttribute("field") && (string.IsNullOrWhiteSpace(el.GetAttribute("field")) || el.HasAttribute("bind")))
                    errors.Add(path + ": column 的 field 必须非空，且不能与 bind 同时声明");
                section.Columns.Add(new LayoutColumn { Header = el.GetAttribute("header"), Bind = el.GetAttribute("bind"), FieldKey = el.GetAttribute("field") });
            }
        }

        static void LoadFields(XmlNode node, LayoutSection section, string path, List<string> errors)
        {
            RejectUnknown(node, Array.Empty<string>(), path, errors);
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "field")
                {
                    errors.Add(path + ": 未知元素 " + child.LocalName);
                    continue;
                }
                section.Fields.Add(LoadField((XmlElement)child, path, errors));
            }
        }

        static LayoutField LoadField(XmlElement el, string path, List<string> errors)
        {
            RejectUnknown(el, new[] { "id", "label", "widget", "type", "action", "bind", "field", "shift", "width", "lookup", "when", "scope", "default", "min", "max", "readonly", "nullable", "tip" }, path, errors);
            if (el.HasAttribute("field") && new[] { "bind", "type", "shift", "width", "action" }.Any(el.HasAttribute))
                errors.Add(path + ": field 不能与 bind/type/shift/width/action 同时声明。");
            if (el.HasAttribute("field") && string.IsNullOrWhiteSpace(el.GetAttribute("field")))
                errors.Add(path + ": field 不能为空。");
            string nullable = el.GetAttribute("nullable");
            if (nullable.Length != 0 && nullable != "true" && nullable != "false")
                errors.Add(path + ": nullable 需要 true 或 false");
            if (nullable == "true" && el.GetAttribute("widget") != "check")
                errors.Add(path + ": nullable 仅用于 check 字段");
            return new LayoutField
            {
                FieldKey = el.GetAttribute("field"),
                Id = el.GetAttribute("id"),
                Label = el.GetAttribute("label"),
                Tip = el.GetAttribute("tip"),
                Widget = string.IsNullOrEmpty(el.GetAttribute("widget")) ? "number" : el.GetAttribute("widget"),
                Type = el.GetAttribute("type"),
                Action = el.GetAttribute("action"),
                Bind = el.GetAttribute("bind"),
                Shift = OptionalInt(el.GetAttribute("shift")),
                Width = OptionalInt(el.GetAttribute("width")),
                Lookup = el.GetAttribute("lookup"),
                When = el.GetAttribute("when"),
                Scope = el.GetAttribute("scope"),
                Default = el.GetAttribute("default"),
                Minimum = OptionalDecimal(el.GetAttribute("min")),
                Maximum = OptionalDecimal(el.GetAttribute("max")),
                ReadOnly = el.GetAttribute("readonly") == "true",
                Nullable = nullable == "true"
            };
        }

        static bool Flag(XmlElement el,string name,string path)
        {
            string value=el.GetAttribute(name);
            if(value.Length!=0 && value!="true" && value!="false") throw new InvalidDataException(path+": "+name+" 需要 true 或 false");
            return value=="true";
        }

        static LayoutGroup LoadGroup(XmlElement el, LayoutSection section, string path, List<string> errors, int depth=0)
        {
            if(depth>16) throw new InvalidDataException(path+": 分组嵌套过深");
            RejectUnknown(el, new[] { "id", "title", "collapsible", "collapsed" }, path, errors);
            var group = new LayoutGroup { Id=el.GetAttribute("id"),Title = el.GetAttribute("title"),
                Collapsible=Flag(el,"collapsible",path),Collapsed=Flag(el,"collapsed",path) };
            if(group.Collapsed && !group.Collapsible) throw new InvalidDataException(path+": collapsed 需要 collapsible=true");
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if(child.LocalName=="group") { group.Groups.Add(LoadGroup((XmlElement)child,section,path,errors,depth+1)); continue; }
                if (child.LocalName != "row")
                {
                    errors.Add(path + ": 未知元素 " + child.LocalName);
                    continue;
                }
                group.Rows.Add(LoadRow((XmlElement)child, section, path, errors));
            }
            return group;
        }

        static LayoutRow LoadRow(XmlElement el, LayoutSection section, string path, List<string> errors)
        {
            RejectUnknown(el, new[] { "label" }, path, errors);
            var row = new LayoutRow { Label = el.GetAttribute("label") };
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "field")
                {
                    errors.Add(path + ": 未知元素 " + child.LocalName);
                    continue;
                }
                var field = LoadField((XmlElement)child, path, errors);
                row.Fields.Add(field);
                section.Fields.Add(field);
            }
            return row;
        }

        static int? OptionalInt(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        static void LoadCommands(XmlNode node, LayoutSection section, string path, List<string> errors)
        {
            RejectUnknown(node, Array.Empty<string>(), path, errors);
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "action")
                {
                    errors.Add(path + ": 未知元素 " + child.LocalName);
                    continue;
                }
                var el = (XmlElement)child;
                RejectUnknown(el, new[] { "id", "label", "script", "confirm", "handler" }, path, errors);
                section.Commands.Add(new LayoutCommand
                {
                    Id = el.GetAttribute("id"),
                    Label = el.GetAttribute("label"),
                    Script = el.GetAttribute("script"),
                    Confirm = el.GetAttribute("confirm"),
                    Handler = el.GetAttribute("handler")
                });
            }
        }

        static decimal? OptionalDecimal(string text) => string.IsNullOrEmpty(text) ? null
            : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        static void LoadScripts(string dir, XmlNode node, LayoutBundle bundle, List<string> errors, string file)
        {
            RejectUnknown(node, Array.Empty<string>(), file, errors);
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName != "script")
                {
                    errors.Add(file + ": 未知元素 " + child.LocalName);
                    continue;
                }
                var el = (XmlElement)child;
                RejectUnknown(el, new[] { "src" }, file, errors);
                AddScript(dir, el.GetAttribute("src"), bundle, errors);
            }
        }

        static void LoadLooseScripts(string dir, LayoutBundle bundle, List<string> errors, bool includeScripts)
        {
            string scripts = Path.Combine(dir, "scripts");
            string tools = Path.Combine(dir, "tools");
            var files = includeScripts && Directory.Exists(scripts) ? Directory.GetFiles(scripts, "*.lua").AsEnumerable() : Enumerable.Empty<string>();
            if (Directory.Exists(tools)) files = files.Concat(Directory.GetFiles(tools, "*.lua", SearchOption.AllDirectories));
            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (bundle.Scripts.Any(s => string.Equals(s.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
                AddScript(dir, Path.GetRelativePath(dir, file), bundle, errors);
            }
        }

        static void AddScript(string dir, string relative, LayoutBundle bundle, List<string> errors)
        {
            if (string.IsNullOrEmpty(relative))
            {
                errors.Add(dir + ": script 缺少 src");
                return;
            }
            string path = Path.GetFullPath(Path.Combine(dir, relative));
            if (!File.Exists(path))
            {
                errors.Add(path + ": 脚本不存在");
                return;
            }
            bundle.Scripts.Add(new LayoutScript
            {
                Name = Path.GetFileName(path),
                Path = path,
                Text = File.ReadAllText(path)
            });
        }

        static XmlDocument ReadXml(string path)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            using var reader = XmlReader.Create(path, settings);
            var doc = new XmlDocument();
            doc.Load(reader);
            return doc;
        }

        static void RejectUnknown(XmlNode node, string[] allowed, string file, List<string> errors)
        {
            if (node.Attributes == null) return;
            var set = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (XmlAttribute attr in node.Attributes)
            {
                if (!set.Contains(attr.Name))
                    errors.Add(file + ": 未知属性 " + node.LocalName + "/@" + attr.Name);
            }
        }
    }
}
