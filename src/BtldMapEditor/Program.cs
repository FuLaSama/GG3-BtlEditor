/*
 * Program.cs  （BtldMapEditor）
 *
 * WinForms 入口。先 ApplicationConfiguration，再加载 GameData 显示名，然后开主窗体。
 * 没有 CLI 导出开关：命令行 Dump/Smoke 在 tools/Dump、tests/Smoke。
 * GameSettings 与 BTL 编解码无关，失败也不应挡住打开编辑器。
 */
using System;
using System.Windows.Forms;

namespace BtldMapEditor
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            GameSettings.LoadAllSettings();
            Application.Run(new MainEditorForm());
        }
    }
}
