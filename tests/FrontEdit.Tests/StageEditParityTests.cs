using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class StageEditParityTests
{
    static string StageLua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "EditorLayout", "scripts", "stage.lua");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("EditorLayout/scripts/stage.lua");
    }

    static BtlFrontDocument Blank() => new BtlFrontDocument { Root = BtlFrontJson.NewTable() };

    static BtlFrontDocument Clone(BtlFrontDocument doc) => BtlFrontJson.Parse(BtlFrontJson.Serialize(doc));

    static ScriptHost Host(BtlFrontDocument doc)
    {
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(StageLua(), "stage.lua");
        return host;
    }

    static Dictionary<string, object> Input(params (string key, object value)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) d[key] = value;
        return d;
    }

    [Fact]
    public void Round_limit_nil_and_zero_match_bind_and_script()
    {
        var bind = Blank();
        var script = Clone(bind);

        var bindHost = new ScriptHost();
        bindHost.Load(bind);
        bindHost.BindSet("Root.stage_metadata.round_limit", null);
        var scriptHost = Host(script);
        scriptHost.Run("set_round_limit", new ScriptArgs { Value = null });
        Assert.Equal(BtlFrontJson.Serialize(bind), BtlFrontJson.Serialize(script));
        Assert.Null(scriptHost.CallGet("set_round_limit"));

        bindHost.BindSet("Root.stage_metadata.round_limit", 0);
        scriptHost.Run("set_round_limit", new ScriptArgs { Value = 0 });
        Assert.Equal(BtlFrontJson.Serialize(bind), BtlFrontJson.Serialize(script));
        Assert.Equal(0d, scriptHost.CallGet("set_round_limit"));
        Assert.True(scriptHost.UndoFrames >= 1);
    }

    [Fact]
    public void Target_weather_reinforce_lists_match()
    {
        var doc = Blank();
        var host = Host(doc);

        host.Run("add_target", new ScriptArgs
        {
            Input = Input(("type", 6), ("value", -3), ("param1", null), ("param2", 2), ("flag", null))
        });
        host.Run("add_target", new ScriptArgs
        {
            Input = Input(("type", null), ("value", null), ("param1", null), ("param2", null), ("flag", 1))
        });
        host.Run("update_target", new ScriptArgs
        {
            ObjectPath = "Root.stage_metadata.targets",
            ObjectIndex = 0,
            Input = Input(("type", 6), ("value", -3), ("param1", 1), ("param2", 2), ("flag", null))
        });
        host.Run("delete_target", new ScriptArgs { Index = 0 });

        host.Run("add_weather", new ScriptArgs
        {
            Input = Input(("type", null), ("start", null), ("duration", 4))
        });
        host.Run("update_weather", new ScriptArgs
        {
            ObjectPath = "Root.decal_info.decals",
            ObjectIndex = 0,
            Input = Input(("type", 2), ("start", 0), ("duration", 4))
        });

        host.Run("add_reinforce", new ScriptArgs
        {
            Input = Input(("cell", null), ("faction", 3), ("is_key", true), ("flag", null))
        });
        host.Run("update_reinforce", new ScriptArgs
        {
            ObjectPath = "Root.battle_info.reinforce_points",
            ObjectIndex = 0,
            Input = Input(("cell", 8), ("faction", 3), ("is_key", false), ("flag", 1))
        });
        host.Run("delete_reinforce", new ScriptArgs { Index = 0 });

        var targets = (BtlVector)((BtlTable)doc.Root.F[2]).F[0];
        Assert.Single(targets.V);
        Assert.Equal((byte)1, ((BtlScalar)((BtlTable)targets.V[0]).F[4]).V);
        var weather = (BtlTable)((BtlVector)((BtlTable)doc.Root.F[9]).F[0]).V[0];
        Assert.Equal((byte)2, ((BtlScalar)weather.F[0]).V);
        Assert.Equal((ushort)0, ((BtlScalar)weather.F[1]).V);
        Assert.Equal((ushort)4, ((BtlScalar)weather.F[2]).V);
        Assert.Empty(((BtlVector)((BtlTable)doc.Root.F[3]).F[1]).V);
    }

    [Fact]
    public void Failed_action_rolls_back_and_get_cannot_write()
    {
        var doc = Blank();
        var host = Host(doc);
        host.Execute(@"
editor.action(""boom"", function(ctx)
  editor.set(""Root.stage_metadata.round_limit"", 3)
  error(""stop"")
end)
editor.action(""bad_get"", {
  get = function(ctx)
    editor.set(""Root.stage_metadata.round_limit"", 1)
    return 1
  end
})
", "bad.lua");

        string before = BtlFrontJson.Serialize(doc);
        int frames = host.UndoFrames;
        Assert.ThrowsAny<Exception>(() => host.Run("boom", new ScriptArgs()));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.Equal(frames, host.UndoFrames);

        Assert.ThrowsAny<Exception>(() => host.CallGet("bad_get"));
        Assert.Equal(before, BtlFrontJson.Serialize(doc));
        Assert.Equal(frames, host.UndoFrames);
    }
}
