using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

[CollectionDefinition("Schema defaults", DisableParallelization = true)]
public class SchemaDefaultCollection { }

[Collection("Schema defaults")]
public class SchemaContextTests
{
    static SchemaContext Context(string type) => new(FbsSchema.LoadFromText(
        $"struct Data {{ amount:{type}; }} table Root {{ value:{type}; data:Data; }} root_type Root;"));

    [Fact]
    public void Documents_keep_their_schema_across_default_changes_and_script_node_creation()
    {
        var saved = SoftSchema.Schema;
        try
        {
            var unsigned = Context("ushort");
            var signed = Context("short");
            SoftSchema.SetSchema(unsigned.Schema);
            var a = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
            var b = new BtlFrontDocument { Context = signed, Root = BtlFrontJson.NewTable() };
            SoftSchema.SetSchema(signed.Schema);
            var ha = new ScriptHost(); ha.Load(a);
            var hb = new ScriptHost(); hb.Load(b);
            ha.BindSet("Root.value", 65535);
            hb.BindSet("Root.value", -1);
            foreach (var host in new[] { ha, hb })
            {
                host.Execute("editor.action('create', function() editor.ensure('Root.data') end)", "create.lua");
                host.Run("create", new());
            }
            Assert.Equal("u16", ((BtlStruct)a.Root.F[1]).Layout[0]);
            Assert.Equal("i16", ((BtlStruct)b.Root.F[1]).Layout[0]);
            Assert.Equal("u16", ((BtlScalar)a.Root.F[0]).T);
            Assert.Equal("i16", ((BtlScalar)b.Root.F[0]).T);
            foreach (var doc in new[] { a, b })
            {
                var built = FrontToBtl.Build(doc);
                Assert.True(built.Ok, string.Join("\n", built.Issues));
                var loaded = BtlToFront.FromBytes(built.Bytes, doc.Context);
                Assert.True(loaded.Ok);
                Assert.Equal(((BtlScalar)doc.Root.F[0]).V, ((BtlScalar)loaded.Document.Root.F[0]).V);
                Assert.Same(doc.Context, BtlFrontJson.CloneDocument(doc).Context);
            }
            Assert.Throws<InvalidDataException>(() => BtlFrontJson.Parse(BtlFrontJson.Serialize(a)));
            Assert.Same(a.Context, BtlFrontJson.Parse(BtlFrontJson.Serialize(a), a.Context).Context);
        }
        finally { SoftSchema.SetSchema(saved); }
    }

    [Fact]
    public void Unresolved_source_cannot_be_exported_after_clone_or_work_document_reload()
    {
        var complete = Context("ushort");
        var doc = new BtlFrontDocument { Context = complete, Root = BtlFrontJson.NewTable() };
        FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)123);
        var unknown = new SchemaContext(FbsSchema.LoadFromText("table Root {} root_type Root;"));
        var loaded = BtlToFront.FromBytes(FrontToBtl.Build(doc).Bytes, unknown);
        Assert.True(loaded.HasUnresolvedData);
        Assert.Contains(loaded.Issues, i => i.Code == "unknown_encoding" && i.IsGuess);
        foreach (var copy in new[] { loaded.Document, BtlFrontJson.CloneDocument(loaded.Document),
            BtlFrontJson.Parse(BtlFrontJson.Serialize(loaded.Document), unknown) })
        {
            Assert.True(copy.HasUnresolvedData);
            Assert.False(FrontToBtl.Build(copy).Ok);
            Assert.Equal(loaded.Document.SourceSha256, copy.SourceSha256);
        }
    }

    [Fact]
    public void Named_json_import_retains_the_schema_used_to_interpret_it()
    {
        var context = Context("short");
        var doc = StageJson.LoadJson("{\"value\":-12}", context: context);
        Assert.Same(context, doc.Context);
        Assert.Same(context.Schema, doc.Context.Schema);
        Assert.Contains("value", StageJson.ToNamed(doc));
        var built = FrontToBtl.Build(doc);
        Assert.True(built.Ok);
        Assert.Equal((short)-12, ((BtlScalar)BtlToFront.FromBytes(built.Bytes, context).Document.Root.F[0]).V);
    }

    [Fact]
    public void Lossy_writer_fallback_is_not_success_and_does_not_overwrite_a_file()
    {
        var context = new SchemaContext(FbsSchema.LoadFromText("table Child { x:int; } table Root { child:Child; } root_type Root;"));
        var doc = new BtlFrontDocument { Context = context, Root = BtlFrontJson.NewTable() };
        FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)123);
        var result = FrontToBtl.Build(doc);
        Assert.False(result.Ok);
        Assert.Contains(result.Issues, i => i.Code == "lossy_write");
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original");
            Assert.Throws<InvalidDataException>(() => FrontToBtl.ToFile(doc, path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
