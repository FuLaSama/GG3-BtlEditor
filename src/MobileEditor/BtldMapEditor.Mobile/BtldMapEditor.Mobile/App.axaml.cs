using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BtldMapEditor.Mobile.ViewModels;
using BtldMapEditor.Mobile.Views;

namespace BtldMapEditor.Mobile;

public partial class App : Application
{
    public static string? ExternalDataDir { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (string.IsNullOrEmpty(ExternalDataDir))
        {
            string? root = FindRepoRoot();
            if (!string.IsNullOrEmpty(root))
                ExternalDataDir = root;
        }
        if (!string.IsNullOrEmpty(ExternalDataDir))
        {
            GameSettings.SetExternalDataDir(ExternalDataDir);
        }
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    static string? FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo dir;
            try { dir = new DirectoryInfo(start); }
            catch { continue; }
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "schema", "battle.fbs"))
                    || File.Exists(Path.Combine(dir.FullName, "BtldMapEditor.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, "GameData")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        return null;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}