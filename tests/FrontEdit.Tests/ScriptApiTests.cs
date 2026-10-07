using System.Text.Json.Nodes;
using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class ScriptApiTests
{
    static ScriptHost Host(string code, ScriptLimits limits = null)
    {
        var host = new ScriptHost(limits);
        host.Load(new BtlFrontDocument { Root = BtlFrontJson.NewTable() });
        host.Execute(code, "contract.lua");
        return host;
    }

    [Fact]
    public void Runaway_action_cannot_catch_budget_and_leaves_host_usable()
    {
        var host = Host("""
            editor.action("bad", function()
              editor.set("/2/1", "u16", 99)
              while true do pcall(function() while true do end end) end
            end)
            editor.action("good", {get=function() return editor.get("/2/1") end,
              set=function(ctx) editor.set("/2/1", "u16", ctx.value) end})
            """, new ScriptLimits { InstructionBudget = 20_000 });
        string before = BtlFrontJson.Serialize(host.Document);
        var error = Assert.Throws<FrontEditException>(() => host.Run("bad", null));
        Assert.Contains("指令预算", error.Message);
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Equal(0, host.UndoFrames);
        host.Run("good", new ScriptArgs { Value = 7 });
        Assert.Equal(7d, host.CallGet("good"));
        Assert.Equal(1, host.UndoFrames);
    }

    [Fact]
    public void Failed_and_runaway_loads_preserve_previous_registration()
    {
        var host = Host("editor.action('original', {get=function() return 12 end})",
            new ScriptLimits { InstructionBudget = 20_000 });
        Assert.Throws<FrontEditException>(() => host.Execute("""
            editor.action('leaked', function() end)
            editor.action('original', {get=function() return 99 end})
            while true do end
            """, "broken.lua"));
        Assert.False(host.CanRun("leaked"));
        Assert.Equal(12d, host.CallGet("original"));
        var error = Assert.Throws<FrontEditException>(() => host.Execute("error('broken')", "second.lua"));
        Assert.Contains("second.lua", error.Message);
        Assert.Equal(12d, host.CallGet("original"));
    }

    [Fact]
    public void Cancellation_rolls_back_and_recovery_is_not_cancelled()
    {
        var host = Host("editor.action('write', function() editor.set('/2/1', 'u16', 9) end)");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        host.Cancellation = cancellation.Token;
        Assert.Contains("取消", Assert.Throws<FrontEditException>(() => host.Run("write", null)).Message);
        Assert.Empty(host.Document.Root.F);
        host.Cancellation = default;
        host.Run("write", null);
        Assert.Equal(1, host.UndoFrames);
    }

    [Fact]
    public void Time_budget_stops_loop_even_with_a_large_instruction_budget()
    {
        var host = Host("editor.action('loop', function() while true do end end)",
            new ScriptLimits { InstructionBudget = int.MaxValue, TimeBudget = TimeSpan.FromMilliseconds(100) });
        Assert.Contains("运行时间预算", Assert.Throws<FrontEditException>(() => host.Run("loop", null)).Message);
        Assert.Empty(host.Document.Root.F);
    }

    [Fact]
    public void Loading_can_only_register_code_and_action_cannot_register_more_actions()
    {
        var host = Host("editor.action('bad', function() editor.action('injected', function() end) end)");
        Assert.Throws<FrontEditException>(() => host.Run("bad", null));
        Assert.False(host.CanRun("injected"));
        Assert.Throws<FrontEditException>(() => host.Execute("editor.get('/2/1')", "initializer.lua"));
        Assert.True(host.CanRun("bad"));
    }

    [Fact]
    public void Explicit_data_api_preserves_absence_and_supports_struct_vector_layout()
    {
        var host = Host("""
            editor.action('write', function()
              assert(editor.api_version == 2)
              assert(not editor.data.exists('/2/1'))
              editor.data.ensure_table('/2')
              assert(editor.data.exists('/2'))
              editor.data.set('/2/1', 'u16', 0)
              assert(editor.data.exists('/2/1'))
              editor.data.set('/2/1', 'u16', nil)
              assert(not editor.data.exists('/2/1'))
              editor.data.ensure_struct_vector('/1/3', {'u8', 'u8', 'i8', 'i8'})
              local i = editor.data.append('/1/3')
              editor.data.set(editor.data.row('/1/3', i) .. '.2', 'i8', -4)
            end)
            """);
        host.Run("write", null);
        var attrs = (BtlVector)((BtlTable)host.Document.Root.F[1]).F[3];
        Assert.Equal(new[] { "u8", "u8", "i8", "i8" }, attrs.StructLayout);
        Assert.Equal(-4, Convert.ToInt32(((BtlStruct)attrs.V[0]).V[2]));
        Assert.False(((BtlTable)host.Document.Root.F[2]).F.ContainsKey(1));
    }

    [Fact]
    public void Failure_restores_clipboard_state_and_reinitializes_globals_and_closures()
    {
        var host = Host("""
            local count = 0
            marker = "initial"
            editor.action("seed", function()
              editor.data.ensure_vector("/6/0", "table")
              editor.data.append("/6/0")
              editor.data.set("/6/0[0]/7", "u16", 11)
              editor.data.append("/6/0")
              editor.data.set("/6/0[1]/7", "u16", 22)
              editor.clipboard.copy("unit", "/6/0[0]")
              assert(editor.clipboard.has("unit"))
              editor.state.set("settings", {score=3, options={true, false, editor.json.null}})
            end)
            editor.action("fail", function()
              editor.keep("unit", "/6/0[1]")
              editor.state.set("settings", {score=9})
              count = count + 1; marker = "changed"
              editor.data.set("/6/0[0]/7", "u16", 99)
              error("abort")
            end)
            editor.action("paste", function() editor.clipboard.paste("/6/0", "unit") end)
            editor.action("observe", {get=function()
              assert(count == 0 and marker == "initial")
              local settings = editor.state.get("settings")
              assert(settings.options[1] and not settings.options[2])
              assert(settings.options[3] == editor.json.null and editor.state.get("missing") == nil)
              settings.score = 100 -- 返回的是副本。
              return editor.state.get("settings").score
            end})
            """);
        host.Run("seed", null);
        string before = BtlFrontJson.Serialize(host.Document);
        Assert.Throws<FrontEditException>(() => host.Run("fail", null));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Equal(3d, host.CallGet("observe"));
        host.Run("paste", null);
        var vector = (BtlVector)((BtlTable)host.Document.Root.F[6]).F[0];
        Assert.Equal(11, Convert.ToInt32(((BtlScalar)((BtlTable)vector.V[2]).F[7]).V));
        Assert.Equal(2, host.UndoFrames);
    }

    [Fact]
    public void Named_clipboard_paste_rejects_missing_copy_instead_of_adding_empty_row()
    {
        var host = Host("editor.action('paste', function() editor.clipboard.paste('/6/0', 'missing') end)");
        Assert.Contains("剪贴板为空", Assert.Throws<FrontEditException>(() => host.Run("paste", null)).Message);
        Assert.Empty(host.Document.Root.F);
        Assert.Equal(0, host.UndoFrames);
    }

    [Theory]
    [InlineData("editor.action('injected', function() end)")]
    [InlineData("editor.resolve('injected', function() return 0 end)")]
    [InlineData("editor.state.set('x', 1)")]
    [InlineData("editor.keep('x', nil)")]
    [InlineData("editor.data.set('/2/1', 'u16', 1)")]
    public void Getter_rejects_side_effects_without_snapshot(string attempted)
    {
        var host = Host("editor.action('bad', {get=function() " + attempted + " end})");
        string before = BtlFrontJson.Serialize(host.Document);
        Assert.Throws<FrontEditException>(() => host.CallGet("bad"));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.False(host.CanRun("injected"));
        Assert.Equal(0, host.SnapshotCount);
    }

    [Fact]
    public void Reading_json_vector_does_not_repair_metadata()
    {
        var host = Host("""
            editor.action("read", {get=function()
              assert(editor.json.vec_count("/10") == 1)
              return editor.json.get(editor.json.vec_at("/10", 0), "id")
            end})
            """);
        var vector = new BtlVector { T = "vector" };
        vector.V.Add(new JsonObject { ["id"] = 42 });
        host.Document.Root.F[10] = vector;
        string before = BtlFrontJson.Serialize(host.Document);
        Assert.Equal(42d, host.CallGet("read"));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Null(vector.Elem);
        Assert.Null(vector.Enc);
        Assert.Equal(0, host.SnapshotCount);
    }

    [Fact]
    public void Read_scope_shares_cache_only_until_end_and_prevents_commits()
    {
        var host = Host("""
            local reads = 0
            editor.action("read", {get=function(ctx)
              if ctx.cache.shared == nil then reads = reads + 1; ctx.cache.shared = reads end
              return ctx.cache.shared
            end})
            editor.action("write", function() editor.set("/2/1", "u16", 5) end)
            """);
        using (host.BeginRead())
        {
            Assert.Equal(1d, host.CallGet("read"));
            Assert.Equal(1d, host.CallGet("read"));
            Assert.Throws<FrontEditException>(() => host.Run("write", null));
            Assert.Throws<FrontEditException>(() => host.Execute("", "later.lua"));
        }
        Assert.Equal(2d, host.CallGet("read"));
        Assert.Equal(0, host.SnapshotCount);
        host.Run("write", null);
        Assert.Equal(1, host.SnapshotCount);
    }

    [Theory]
    [InlineData("u64", "18446744073709551615")]
    [InlineData("i64", "-9223372036854775808")]
    [InlineData("i64", "9223372036854775807")]
    public void Exact_integer_strings_survive_json_and_failed_transaction(string type, string text)
    {
        var host = Host($$"""
            editor.action("write", function() editor.data.set_integer("/30", "{{type}}", "{{text}}") end)
            editor.action("read", {get=function() return editor.data.get_integer("/30") end})
            editor.action("unsafe", {get=function() return editor.get("/30") end})
            editor.action("fail", function() editor.set("/2/1", "u16", 6); error("abort") end)
            """);
        host.Run("write", null);
        host.Load(BtlFrontJson.Parse(BtlFrontJson.Serialize(host.Document)));
        Assert.Equal(text, host.CallGet("read"));
        Assert.Throws<FrontEditException>(() => host.Run("fail", null));
        Assert.Equal(text, host.CallGet("read"));
        Assert.Contains("精确范围", Assert.Throws<FrontEditException>(() => host.CallGet("unsafe")).Message);
        Assert.Equal(text, host.CallGet("read"));
    }

    [Fact]
    public void Unsafe_double_integer_is_rejected_with_action_path_type_and_value()
    {
        var host = Host("editor.action('bad', function() editor.data.set('/30', 'u64', 9007199254740992) end)");
        string message = Assert.Throws<FrontEditException>(() => host.Run("bad", null)).Message;
        Assert.Contains("bad", message);
        Assert.Contains("contract.lua", message);
        Assert.Contains("/30", message);
        Assert.Contains("u64", message);
        Assert.Contains("9007199254740992", message);
        Assert.Empty(host.Document.Root.F);
    }

    [Fact]
    public void Json_supports_nested_objects_mixed_values_empty_shapes_and_explicit_null()
    {
        var host = Host("""
            editor.action("write", function()
              local root = editor.json.vec_append("/10")
              editor.json.set(root, "payload", {ratio=0.25, count=4294967295,
                values={true, "text", editor.json.null, {nested=3}},
                object=editor.json.object(), array=editor.json.array()})
              editor.json.set(root, "present", editor.json.null)
              editor.json.set(root, "deleted", 1)
              editor.json.set(root, "deleted", nil)
            end)
            """);
        host.Run("write", null);
        var root = (JsonObject)((BtlVector)host.Document.Root.F[10]).V[0];
        var payload = (JsonObject)root["payload"];
        Assert.Equal(0.25, payload["ratio"].GetValue<double>());
        Assert.Equal(4294967295, payload["count"].GetValue<long>());
        Assert.IsType<JsonObject>(payload["object"]);
        Assert.IsType<JsonArray>(payload["array"]);
        Assert.Null(payload["values"][2]);
        Assert.Equal(3, payload["values"][3]["nested"].GetValue<int>());
        Assert.True(root.ContainsKey("present"));
        Assert.False(root.ContainsKey("deleted"));
    }

    [Theory]
    [InlineData("local value = {}; value.self = value")]
    [InlineData("local value = {[1]=1, [3]=3}")]
    [InlineData("local value = {[1]=1, named=2}")]
    [InlineData("local value = 0/0")]
    public void Invalid_json_values_roll_back_instead_of_silently_dropping_keys(string expression)
    {
        var host = Host("editor.action('bad', function() local root = editor.json.vec_append('/10'); "
            + expression + "; editor.json.set(root, 'value', value) end)");
        Assert.Throws<FrontEditException>(() => host.Run("bad", null));
        Assert.Empty(host.Document.Root.F);
    }

    [Fact]
    public void Fill_rejects_sparse_arrays_instead_of_losing_elements()
    {
        var host = Host("editor.action('bad', function() editor.fill('/6/1[0]/1', 'u16', {[1]=4, [3]=8}) end)");
        Assert.Throws<FrontEditException>(() => host.Run("bad", null));
        Assert.Empty(host.Document.Root.F);
    }

    [Fact]
    public void Resolver_rejects_fractional_index_and_handles_expire_between_calls()
    {
        var host = Host("""
            local stored
            editor.resolve("bad", function() return 0.5 end)
            editor.action("retain", function() stored = editor.root() end)
            editor.action("use", {get=function() return editor.field(stored, 2) end})
            """);
        Assert.Throws<FrontEditException>(() => host.ResolveIndex("bad", 0));
        host.Run("retain", null);
        Assert.Contains("句柄", Assert.Throws<FrontEditException>(() => host.CallGet("use")).Message);
    }
}
