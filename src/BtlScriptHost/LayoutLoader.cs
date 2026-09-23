using System.Xml;

namespace BtlCore.Scripting
{
    public static class LayoutLoader
    {
        public static LayoutLoadResult Load(string builtinDir, string userDir)
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
                if (!string.IsNullOrEmpty(userDir) && File.Exists(Path.Combine(userDir, "layout.xml")))
                    LoadRoot(userDir, result.Bundle, result.Errors, userOverride: true);
                else if (!string.IsNullOrEmpty(userDir) && Directory.Exists(userDir))
                    LoadLooseScripts(userDir, result.Bundle, result.Errors);
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
                    if (string.IsNullOrEmpty(field.Action)) continue;
                    bool ok = canRun(field.Action) && (canGet == null || canGet(field.Action));
                    if (!ok)
                    {
                        field.Enabled = false;
                        field.DisableReason = "找不到操作 " + field.Action;
                    }
                }
                foreach (var cmd in section.Commands)
                {
                    if (string.IsNullOrEmpty(cmd.Script) || !canRun(cmd.Script))
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
                RejectUnknown(el, new[] { "id", "title", "page", "kind" }, file, errors);
                string id = el.GetAttribute("id");
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    errors.Add(file + ": tab id 重复或为空");
                var tab = new LayoutTab
                {
                    Id = id,
                    Title = el.GetAttribute("title"),
                    Builtin = el.GetAttribute("kind") == "builtin"
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
            RejectUnknown(el, new[] { "title", "kind", "bind" }, path, errors);
            var section = new LayoutSection
            {
                Title = el.GetAttribute("title"),
                Kind = el.GetAttribute("kind"),
                Bind = el.GetAttribute("bind")
            };
            if (section.Kind != "form" && section.Kind != "list")
                errors.Add(path + ": 不支持的栏目 " + section.Kind);
            foreach (XmlNode child in el.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.LocalName == "field" && section.Kind == "form")
                    section.Fields.Add(LoadField((XmlElement)child, path, errors));
                else if (child.LocalName == "columns")
                    LoadColumns(child, section, path, errors);
                else if (child.LocalName == "fields")
                    LoadFields(child, section, path, errors);
                else if (child.LocalName == "actions")
                    LoadCommands(child, section, path, errors);
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
                RejectUnknown(el, new[] { "header", "bind" }, path, errors);
                section.Columns.Add(new LayoutColumn { Header = el.GetAttribute("header"), Bind = el.GetAttribute("bind") });
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
            RejectUnknown(el, new[] { "id", "label", "widget", "type", "action" }, path, errors);
            return new LayoutField
            {
                Id = el.GetAttribute("id"),
                Label = el.GetAttribute("label"),
                Widget = string.IsNullOrEmpty(el.GetAttribute("widget")) ? "number" : el.GetAttribute("widget"),
                Type = el.GetAttribute("type"),
                Action = el.GetAttribute("action")
            };
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
                RejectUnknown(el, new[] { "id", "label", "script", "confirm" }, path, errors);
                section.Commands.Add(new LayoutCommand
                {
                    Id = el.GetAttribute("id"),
                    Label = el.GetAttribute("label"),
                    Script = el.GetAttribute("script"),
                    Confirm = el.GetAttribute("confirm")
                });
            }
        }

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

        static void LoadLooseScripts(string dir, LayoutBundle bundle, List<string> errors)
        {
            string scripts = Path.Combine(dir, "scripts");
            if (!Directory.Exists(scripts)) return;
            foreach (var file in Directory.GetFiles(scripts, "*.lua").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                AddScript(dir, Path.GetRelativePath(dir, file), bundle, errors);
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
