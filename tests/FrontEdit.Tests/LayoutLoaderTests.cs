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
        Assert.Contains(page.Sections, s => s.Kind == "list" && s.Bind == "Root.stage_metadata.targets");
        Assert.Contains(result.Bundle.Scripts, s => s.Name == "stage.lua");

        LayoutLoader.MarkMissingActions(page, name => name != "add_target", name => name == "set_round_limit");
        var targets = page.Sections.Find(s => s.Bind == "Root.stage_metadata.targets");
        Assert.False(targets.Commands.Find(c => c.Script == "add_target").Enabled);
        Assert.True(page.Sections[0].Fields[0].Enabled);
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
}
