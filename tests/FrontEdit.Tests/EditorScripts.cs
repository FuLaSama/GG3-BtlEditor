using BtlCore.Scripting;

namespace BtlCore.Front.Tests;

static class EditorScripts
{
    public static string Root()
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

    public static void Load(ScriptHost host, params string[] relative)
    {
        string scripts = Path.Combine(Root(), "EditorLayout", "scripts");
        // 单独测试页面动作时，也加载与正式布局一致的对象层依赖。
        var layout = LayoutLoader.Load(Path.Combine(Root(), "EditorLayout"), null);
        if (!layout.Ok) throw new InvalidDataException(string.Join("\n", layout.Errors));
        foreach (var library in layout.Bundle.Scripts.Where(s =>
            s.Path.Replace('\\', '/').Contains("/scripts/common/") || s.Path.Replace('\\', '/').Contains("/scripts/objects/")))
        {
            host.Execute(library.Text, library.Name);
        }
        foreach (string name in relative)
        {
            string path = Path.Combine(scripts, name.Replace('/', Path.DirectorySeparatorChar));
            host.Execute(File.ReadAllText(path), Path.GetFileName(path));
        }
    }
}
