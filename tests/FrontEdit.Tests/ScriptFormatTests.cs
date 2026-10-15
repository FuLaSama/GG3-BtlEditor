using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class ScriptFormatTests
{
    const string Extension = """
        editor.format.schema { id='research', definition=[[
          table Root {
            research_value:uint (id:30);
            payload:[ubyte] (id:31);
            samples:[ResearchSample] (id:32);
          }
          struct ResearchSample { flag:ushort; weight:short; }
        ]] }
        editor.format.codec {
          id='research.packet', revision='1',
          decode=function(bytes)
            assert(#bytes >= 8 and bytes[1] == 123, 'unknown packet')
            return {level=editor.binary.read(bytes,1,'u16'), raw=bytes}
          end,
          encode=function(value)
            editor.binary.write(value.raw,1,'u16',value.level)
            return value.raw
          end
        }
        editor.action('initialize', function()
          editor.data.set('/30','u32',123456)
          editor.data.ensure_vector('/31','u8')
          editor.data.fill('/31','u8',{123,7,0,251,252,253,254,255})
          editor.data.ensure_struct_vector('/32',{'u16','i16'})
          local i=editor.data.append('/32')
          editor.data.set('/32['..i..'].0','u16',9)
          editor.data.set('/32['..i..'].1','i16',-12)
        end)
        editor.action('level', {
          get=function() return editor.format.read('research.packet','/31').level end,
          set=function(ctx)
            local value=editor.format.read('research.packet','/31')
            value.level=ctx.value
            editor.format.write('research.packet','/31',value)
          end
        })
        """;

    static SchemaContext Basis() => new(FbsSchema.LoadFromText("table Root { version:ushort; } root_type Root;"));

    static ScriptHost Host(string extra = "", ScriptLimits limits = null)
    {
        var host = new ScriptHost(limits);
        host.Execute(Extension, "research.lua");
        if (extra.Length != 0) host.Execute(extra, "extra.lua");
        host.Load(new BtlFrontDocument { Context = host.CreateSchemaContext(Basis()), Root = BtlFrontJson.NewTable() });
        host.Run("initialize", null);
        host.Document.Editing.ResetHistory();
        return host;
    }

    static byte[] Build(BtlFrontDocument doc)
    {
        var result = FrontToBtl.Build(doc);
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        return result.Bytes;
    }

    [Fact]
    public void Lua_schema_and_codec_support_binary_roundtrip_without_changing_base_schema()
    {
        var basis = Basis();
        var host = Host();
        var bytes = Build(host.Document);
        Assert.True(BtlToFront.FromBytes(bytes, basis).HasUnresolvedData);
        Assert.False(basis.Schema.TryGetField("Root", 30, out _));
        var interpreted = BtlToFront.FromBytes(bytes, host.CreateSchemaContext(basis));
        Assert.False(interpreted.HasUnresolvedData);
        Assert.Equal((uint)123456, ((BtlScalar)interpreted.Document.Root.F[30]).V);
        Assert.Equal((short)-12, ((BtlStruct)((BtlVector)interpreted.Document.Root.F[32]).V[0]).V[1]);
        host.Load(interpreted.Document);
        string original = BtlFrontJson.Serialize(host.Document);
        Assert.Equal(7d, host.CallGet("level"));
        Assert.Equal(original, BtlFrontJson.Serialize(host.Document));
        host.Run("level", new ScriptArgs { Value = 513 });
        var payload = (BtlVector)host.Document.Root.F[31];
        Assert.Equal(new byte[] {123,1,2,251,252,253,254,255}, payload.V.Select(Convert.ToByte).ToArray());
        Assert.Equal("research.packet", Assert.Single(host.Document.PayloadCodecUses).CodecId);
        var saved = Build(host.Document);
        var reopened = BtlToFront.FromBytes(saved, host.CreateSchemaContext(basis));
        Assert.False(reopened.HasUnresolvedData);
        host.Load(reopened.Document);
        Assert.Equal(513d, host.CallGet("level"));
        Assert.Equal(saved, Build(reopened.Document));
    }

    [Fact]
    public void Codec_edit_history_working_json_and_no_op_preserve_provenance()
    {
        var host = Host();
        string before = BtlFrontJson.Serialize(host.Document);
        host.Run("level", new ScriptArgs { Value = 7 });
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Equal(1, host.Document.Editing.HistoryCount);
        host.Run("level", new ScriptArgs { Value = 12 });
        string after = BtlFrontJson.Serialize(host.Document);
        Assert.Single(BtlFrontJson.Parse(after, host.Document.Context).PayloadCodecUses);
        Assert.Throws<InvalidDataException>(() => BtlFrontJson.Parse(after, Basis()));
        Assert.True(host.Document.Editing.Undo());
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.True(host.Document.Editing.Redo());
        Assert.Equal(after, BtlFrontJson.Serialize(host.Document));
        using (host.Document.Editing.BeginHistoryGroup())
        {
            host.Run("level", new ScriptArgs { Value = 13 });
            host.Run("level", new ScriptArgs { Value = 12 });
        }
        Assert.Equal(after, BtlFrontJson.Serialize(host.Document));
    }

    [Fact]
    public void Invalid_encoder_output_is_atomic_even_when_script_catches_error()
    {
        var host = Host("""
            editor.format.codec {id='bad',revision='1',decode=function(b) return b end,
              encode=function(v) return {1,2,256} end}
            editor.action('caught',function()
              assert(not pcall(function() editor.format.write('bad','/31',{}) end))
            end)
            editor.action('fail',function()
              editor.data.set('/30','u32',99)
              editor.format.write('bad','/31',{})
            end)
            """);
        string before = BtlFrontJson.Serialize(host.Document);
        host.Run("caught", null);
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Throws<FrontEditException>(() => host.Run("fail", null));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Equal(7d, host.CallGet("level"));
    }

    [Fact]
    public void Codec_cannot_mutate_document_and_budget_failure_recovers_host()
    {
        var host = Host("""
            editor.format.codec {id='impure',revision='1',decode=function(b)
              editor.data.set('/30','u32',0); return b end,encode=function(v) return v end}
            editor.format.codec {id='loop',revision='1',decode=function(b) return b end,
              encode=function(v) while true do pcall(function() while true do end end) end end}
            editor.action('impure',function() editor.format.read('impure','/31') end)
            editor.action('loop',function()
              editor.data.set('/30','u32',99)
              editor.format.write('loop','/31',{})
            end)
            editor.action('readonly',{get=function() editor.format.write('research.packet','/31',{}) end})
            """, new ScriptLimits { InstructionBudget = 30_000 });
        string before = BtlFrontJson.Serialize(host.Document);
        Assert.Contains("编解码器只能", Assert.Throws<FrontEditException>(() => host.Run("impure", null)).Message);
        Assert.Contains("指令预算", Assert.Throws<FrontEditException>(() => host.Run("loop", null)).Message);
        Assert.Throws<FrontEditException>(() => host.CallGet("readonly"));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        host.Run("level", new ScriptArgs { Value = 8 });
        Assert.Equal(8d, host.CallGet("level"));
    }

    [Fact]
    public void Schema_reload_does_not_reinterpret_existing_document_and_failed_registration_is_removed()
    {
        var host = Host();
        var context = host.Document.Context;
        Assert.Same(context, host.CreateSchemaContext(context));
        Assert.Throws<FrontEditException>(() => host.Execute("""
            editor.format.schema {id='leaked',definition='table Root { leaked:uint (id:40); }'}
            error('cancel')
            """, "bad.lua"));
        Assert.Equal(context.Fingerprint, host.CreateSchemaContext(context).Fingerprint);
        host.Execute("editor.format.schema {id='new',definition='table Root { later:ushort (id:41); }'}", "later.lua");
        Assert.Same(context, host.Document.Context);
        Assert.NotEqual(context.Fingerprint, host.CreateSchemaContext(context).Fingerprint);
        Assert.Contains("重新打开", Assert.Throws<FrontEditException>(() => host.CallGet("level")).Message);
        Assert.Same(context, host.Document.Context);
    }

    [Fact]
    public void Changing_codec_source_rejects_silent_reinterpretation_of_working_document()
    {
        var original = Host();
        original.Run("level", new ScriptArgs { Value = 8 });
        var replacement = new ScriptHost();
        replacement.Execute(Extension.Replace("revision='1'", "revision='2'"), "research.lua");
        replacement.Load(original.Document);
        Assert.Contains("版本不匹配", Assert.Throws<FrontEditException>(() => replacement.CallGet("level")).Message);
        var reopened = BtlToFront.FromBytes(Build(original.Document), replacement.CreateSchemaContext(Basis()));
        replacement.Load(reopened.Document);
        Assert.Equal(8d, replacement.CallGet("level"));
    }

    [Theory]
    [InlineData("table Root { value:uint; }")]
    [InlineData("table Root { value:Missing (id:30); }")]
    [InlineData("table Root { value:uint (id:512); }")]
    [InlineData("table Root { version:uint (id:30); }")]
    [InlineData("include \"other.fbs\"; table Root { value:uint (id:30); }")]
    [InlineData("struct Root { value:uint; }")]
    public void Invalid_schema_extensions_do_not_change_basis(string definition)
    {
        var basis = Basis();
        string hash = basis.Fingerprint;
        Assert.Throws<InvalidDataException>(() => basis.WithOverlays(new[] { definition }));
        Assert.Equal(hash, basis.Fingerprint);
        Assert.False(basis.Schema.TryGetField("Root", 30, out _));
    }

    [Fact]
    public void Binary_helpers_cover_endian_signed_float_exact64_and_bounds()
    {
        var host = Host("""
            editor.action('binary',function()
              local b=editor.binary.create(24)
              editor.binary.write(b,0,'u32',4294967295,'be')
              assert(b[1]==255 and editor.binary.read(b,0,'u32','be')==4294967295)
              editor.binary.write(b,0,'i16',-1234,'be')
              assert(b[1]==251 and b[2]==46 and editor.binary.read(b,0,'i16','be')==-1234)
              editor.binary.write(b,0,'f32',1.5)
              assert(b[3]==192 and b[4]==63 and editor.binary.read(b,0,'f32')==1.5)
              editor.binary.write(b,0,'f64',-1.25,'be')
              assert(editor.binary.read(b,0,'f64','be')==-1.25)
              editor.binary.write(b,8,'u64','18446744073709551615','be')
              assert(editor.binary.read(b,8,'u64','be')=='18446744073709551615')
              editor.binary.write(b,16,'i64','-9223372036854775808')
              assert(editor.binary.read(b,16,'i64')=='-9223372036854775808')
              assert(not pcall(function() editor.binary.write(b,23,'u16',1) end))
              assert(not pcall(function() editor.binary.write(b,0,'u64',1) end))
              assert(not pcall(function() editor.binary.write(b,0,'u8',1.2) end))
              assert(not pcall(function() editor.binary.write(b,0,'f32',1e100) end))
              assert(not pcall(function() editor.binary.read(b,-1,'u8') end))
              assert(not pcall(function() editor.binary.read(b,0,'u8','middle') end))
            end)
            """);
        host.Run("binary", null);
    }

    [Fact]
    public void Outer_failure_restores_codec_bytes_and_provenance_and_plain_vectors_are_required()
    {
        var host = Host("""
            editor.action('missing',function() editor.format.read('research.packet','/99') end)
            editor.action('packed',function() editor.format.write('research.packet','/31',{}) end)
            """);
        string before = BtlFrontJson.Serialize(host.Document);
        Assert.Throws<InvalidOperationException>(() => host.Document.Editing.Execute(() =>
        {
            host.Run("level", new ScriptArgs { Value = 99 });
            throw new InvalidOperationException("outer failure");
        }));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        Assert.Throws<FrontEditException>(() => host.Run("missing", null));
        Assert.Equal(before, BtlFrontJson.Serialize(host.Document));
        ((BtlVector)host.Document.Root.F[31]).Enc = PackedJsonStream.EncName;
        string packed = BtlFrontJson.Serialize(host.Document);
        Assert.Contains("普通 u8", Assert.Throws<FrontEditException>(() => host.Run("packed", null)).Message);
        Assert.Equal(packed, BtlFrontJson.Serialize(host.Document));
    }

    [Fact]
    public void Schemas_can_reference_later_declarations_and_replace_complete_struct_layouts()
    {
        var basis = new SchemaContext(FbsSchema.LoadFromText("""
            struct Info { x:ushort; }
            table Root { info:Info; }
            root_type Root;
            """));
        var host = new ScriptHost();
        host.Execute("editor.format.schema {id='root',definition='table Root { rows:[Row] (id:30); }'}", "root.lua");
        host.Execute("""
            editor.format.schema {id='types',definition=[[
              struct Info { x:ushort; y:short; }
              table Row { value:uint (id:0); }
            ]]}
            """, "types.lua");
        var context = host.CreateSchemaContext(basis);
        Assert.True(context.TryStructMembers("Info", out var members));
        Assert.Equal(new[] {"u16", "i16"}, members.Select(m => m.T));
        Assert.Single(basis.Schema.StructMembers("Info"));
        var doc = new BtlFrontDocument { Context = context, Root = BtlFrontJson.NewTable() };
        var info = FrontEdit.EnsureStruct(doc.Root, 0, new[] {"u16", "i16"});
        info.V[0] = (ushort)500; info.V[1] = (short)-8;
        var row = BtlFrontJson.NewTable(); FrontEdit.SetScalar(row, 0, "u32", (uint)123);
        FrontEdit.EnsureVec(doc.Root, 30, "table").V.Add(row);
        var reopened = BtlToFront.FromBytes(Build(doc), context);
        Assert.False(reopened.HasUnresolvedData);
        Assert.Equal((short)-8, ((BtlStruct)reopened.Document.Root.F[0]).V[1]);
        Assert.Equal((uint)123, ((BtlScalar)((BtlTable)((BtlVector)reopened.Document.Root.F[30]).V[0]).F[0]).V);
    }

    [Fact]
    public void Installing_only_example_lua_in_user_tools_registers_types_codec_and_tool()
    {
        string root = EditorScripts.Root();
        string user = Path.Combine(Path.GetTempPath(), "lua-format-tools-" + Guid.NewGuid());
        string tools = Path.Combine(user, "tools");
        Directory.CreateDirectory(tools);
        string scriptPath = Path.Combine(tools, "research_packet.lua");
        try
        {
            File.Copy(Path.Combine(root, "EditorLayout/examples/research_packet.lua"), scriptPath);
            var layout = LayoutLoader.Load(Path.Combine(root, "EditorLayout"), user);
            Assert.True(layout.Ok, string.Join("; ", layout.Errors));
            var host = new ScriptHost(); host.ConfigureFields(layout.Bundle.Fields);
            foreach (var script in layout.Bundle.Scripts) host.Execute(script.Text, script.Name);
            var doc = new BtlFrontDocument { Context = host.CreateSchemaContext(), Root = BtlFrontJson.NewTable() };
            FrontEdit.Fill(FrontEdit.EnsureVec(doc.Root, 31, "u8"), new byte[] {123,7,0,251,252,253,254,255}.Cast<object>());
            host.Load(BtlToFront.FromBytes(Build(doc), doc.Context).Document);
            Assert.False(host.Document.HasUnresolvedData);
            Assert.Contains(host.Tools, t => t.Id == "example.research_packet_value");
            host.RunTool("example.research_packet_value", new ScriptArgs { Input = new() { ["value"] = 513 } });
            var read = BtlToFront.FromBytes(Build(host.Document), doc.Context);
            Assert.False(read.HasUnresolvedData);
            Assert.Equal(new byte[] {123,1,2,251,252,253,254,255}, ((BtlVector)read.Document.Root.F[31]).V.Select(Convert.ToByte).ToArray());
        }
        finally
        {
            File.Delete(scriptPath);
            Directory.Delete(tools);
            Directory.Delete(user);
        }
    }
}
