using System;
using System.IO;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace BtldMapEditor.Mobile.Android;

[Activity(
    Label = "将三地编",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        try
        {
            // 1. Get the external files directory (guaranteed read/write access without runtime permissions)
            string filesDir = GetExternalFilesDir(null).AbsolutePath;
            
            // 2. Extract configuration XML/JSON and default BTL maps from APK Assets
            ExtractAssets(filesDir);
            
            // 3. Expose the directory to the platform-agnostic Avalonia App
            App.ExternalDataDir = filesDir;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("BtldMapEditor", $"Startup Asset Extraction failed: {ex.Message}");
        }

        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    private void ExtractAssets(string destDir)
    {
        ExtractAssetFolder("GameData", Path.Combine(destDir, "GameData"));
        ExtractAssetFolder("BTL", Path.Combine(destDir, "BTL"));
        
        try
        {
            string fbsDest = Path.Combine(destDir, "battle.fbs");
            using (var src = Assets.Open("battle.fbs"))
            using (var dst = File.Create(fbsDest))
            {
                src.CopyTo(dst);
            }
            global::Android.Util.Log.Info("BtldMapEditor", $"Extracted asset: battle.fbs -> {fbsDest}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("BtldMapEditor", $"Failed to extract battle.fbs: {ex.Message}");
        }
    }

    private void ExtractAssetFolder(string assetFolder, string destFolder)
    {
        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        string[] files = Assets.List(assetFolder);
        if (files == null) return;

        foreach (string file in files)
        {
            string assetPath = Path.Combine(assetFolder, file);
            string destPath = Path.Combine(destFolder, file);

            // BTL files (user maps) should only be copied if they do not exist (to prevent overwriting user edits).
            // Configuration files can always be updated/overwritten to keep definitions in sync with the package.
            bool onlyIfNotExists = assetFolder == "BTL";
            if (onlyIfNotExists && File.Exists(destPath))
            {
                continue;
            }

            try
            {
                using (var src = Assets.Open(assetPath))
                using (var dst = File.Create(destPath))
                {
                    src.CopyTo(dst);
                }
                global::Android.Util.Log.Info("BtldMapEditor", $"Extracted asset: {assetPath} -> {destPath}");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("BtldMapEditor", $"Failed to extract asset {assetPath}: {ex.Message}");
            }
        }
    }
}
