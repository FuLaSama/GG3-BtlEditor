using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class LayoutLoaderTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EditorLayout", "layout.xml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("EditorLayout");
    }

    [Fact]
    public void Stage_page_loads_and_missing_action_disables_control()
    {
        string root = RepoRoot();
        var result = LayoutLoader.Load(Path.Combine(root, "EditorLayout"), null);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        var page = result.Bundle.Tabs.Find(t => t.Id == "stage").Page;
        Assert.Contains(page.Sections, s => s.Kind == "list" && s.Bind == "/2/0");
        Assert.Contains(result.Bundle.Scripts, s => s.Name == "回合.lua");

        LayoutLoader.MarkMissingActions(page, name => name != "add_target", name => name == "set_round_limit");
        var targets = page.Sections.Find(s => s.Bind == "/2/0");
        Assert.False(targets.Commands.Find(c => c.Script == "add_target").Enabled);
        Assert.True(page.Sections[0].Fields[0].Enabled);
    }

    [Fact]
    public void Unit_and_site_pages_keep_the_old_group_boxes()
    {
        string root = RepoRoot();
        var result = LayoutLoader.Load(Path.Combine(root, "EditorLayout"), null);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        var unit = result.Bundle.Tabs.Find(t => t.Id == "unit").Page.Sections[0];
        Assert.Equal(4, unit.Groups.Count);
        Assert.Equal("基础部队信息", unit.Groups[0].Title);
        Assert.Equal("部队AI行为", unit.Groups[1].Title);
        Assert.Equal("将领配置", unit.Groups[2].Title);
        Assert.Equal("特种部队配置", unit.Groups[3].Title);
        Assert.Contains(unit.Fields, f => f.Id == "hp" && f.Bind == "/0.6");
        Assert.Contains(unit.Fields, f => f.Id == "behavior_0" && f.When == "behavior");

        var site = result.Bundle.Tabs.Find(t => t.Id == "site").Page;
        Assert.Equal("建筑配置", site.Sections[0].Title);
        Assert.Equal(6, site.Sections[0].Rows.Count);
        Assert.Equal("军事工事配置", site.Sections[1].Title);
        Assert.Contains(site.Sections[1].Fields, f => f.Id == "fort_id" && f.Lookup == "fort");
    }

    [Fact]
    public void Broken_xml_keeps_failure_and_user_page_overrides()
    {
        string broken = Path.Combine(Path.GetTempPath(), "btl-layout-" + Guid.NewGuid().ToString("N"));
        string user = Path.Combine(Path.GetTempPath(), "btl-layout-user-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(broken);
            File.WriteAllText(Path.Combine(broken, "layout.xml"), "<editorLayout format=\"1\"><nope/></editorLayout>");
            var bad = LayoutLoader.Load(broken, null);
            Assert.False(bad.Ok);
            Assert.Null(bad.Bundle);

            string root = RepoRoot();
            Directory.CreateDirectory(Path.Combine(user, "pages"));
            File.WriteAllText(Path.Combine(user, "layout.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><editorLayout format=\"1\"><tabs><tab id=\"stage\" title=\"用户页\" page=\"pages/stage.xml\"/></tabs></editorLayout>");
            File.Copy(Path.Combine(root, "EditorLayout", "pages", "stage.xml"), Path.Combine(user, "pages", "stage.xml"));
            var over = LayoutLoader.Load(Path.Combine(root, "EditorLayout"), user);
            Assert.True(over.Ok, string.Join("\n", over.Errors));
            Assert.Equal("用户页", over.Bundle.Tabs.Find(t => t.Id == "stage").Title);
        }
        finally
        {
            if (Directory.Exists(broken)) Directory.Delete(broken, true);
            if (Directory.Exists(user)) Directory.Delete(user, true);
        }
    }

    [Fact]
    public void User_tools_are_discovered_with_or_without_manifest_without_double_loading()
    {
        string user = Path.Combine(Path.GetTempPath(), "btl-user-tools-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(user, "tools", "小工具"));
            string path = Path.Combine(user, "tools", "小工具", "生命.lua");
            File.WriteAllText(path, "editor.tool {title='测试工具',target='all_units',run=function(unit) unit:heal() end}");
            var noManifest = LayoutLoader.Load(Path.Combine(RepoRoot(), "EditorLayout"), user);
            Assert.True(noManifest.Ok, string.Join("\n", noManifest.Errors));
            Assert.Single(noManifest.Bundle.Scripts, s => s.Path == path);
            File.WriteAllText(Path.Combine(user, "layout.xml"), "<editorLayout format=\"1\"><scripts><script src=\"tools/小工具/生命.lua\"/></scripts></editorLayout>");
            Directory.CreateDirectory(Path.Combine(user, "scripts"));
            File.WriteAllText(Path.Combine(user, "scripts", "未列出.lua"), "error('应保持未加载')");
            var manifest = LayoutLoader.Load(Path.Combine(RepoRoot(), "EditorLayout"), user);
            Assert.True(manifest.Ok, string.Join("\n", manifest.Errors));
            Assert.Single(manifest.Bundle.Scripts, s => s.Path == path);
            Assert.DoesNotContain(manifest.Bundle.Scripts, s => s.Name == "未列出.lua");
            var host = new ScriptHost();
            foreach (var script in manifest.Bundle.Scripts) host.Execute(script.Text, script.Name);
            Assert.Contains(host.Tools, t => t.Id == "测试工具");
        }
        finally { if (Directory.Exists(user)) Directory.Delete(user, true); }
    }
}
