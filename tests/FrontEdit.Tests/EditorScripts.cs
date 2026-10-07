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
        foreach (string library in new[] { "common/游戏对象.lua", "common/业务对象.lua", "common/地图操作.lua", "common/行为树对象.lua" })
        {
            string libraryPath = Path.Combine(scripts, library);
            host.Execute(File.ReadAllText(libraryPath), Path.GetFileName(libraryPath));
        }
        foreach (string name in relative)
        {
            string path = Path.Combine(scripts, name.Replace('/', Path.DirectorySeparatorChar));
            host.Execute(File.ReadAllText(path), Path.GetFileName(path));
        }
    }
}
