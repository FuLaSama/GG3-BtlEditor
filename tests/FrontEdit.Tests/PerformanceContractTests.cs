using System.Text.Json.Nodes;
using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class PerformanceContractTests
{
    [Fact]
    public void Direct_snapshot_preserves_types_layouts_unknown_fields_and_is_independent()
    {
        var doc = new BtlFrontDocument { FormatVersion = 7, Root = BtlFrontJson.NewTable() };
        doc.Root.F[98] = BtlFrontJson.Scalar("u64", ulong.MaxValue);
        var vector = BtlFrontJson.NewVector("struct");
        vector.StructLayout.AddRange(new[] { "u16", "i8", "f32" });
        var structure = FrontEdit.NewStruct(vector.StructLayout);
        structure.V[0] = (ushort)4321; structure.V[1] = (sbyte)-12; structure.V[2] = 1.25f;
        vector.V.Add(structure); doc.Root.F[97] = vector;
        var packed = BtlFrontJson.NewVector("u8"); packed.Enc = "country_ai_bt";
        packed.V.Add(JsonNode.Parse("{\"unknown\":[1,2,{\"value\":3}]}")); doc.Root.F[96] = packed;
        var clone = BtlFrontJson.CloneDocument(doc);
        Assert.True(BtlFrontJson.ContentEquals(doc, clone));
        Assert.Equal(BtlFrontJson.Serialize(doc), BtlFrontJson.Serialize(clone));
        Assert.IsType<ulong>(((BtlScalar)clone.Root.F[98]).V);
        var copied = (BtlStruct)((BtlVector)clone.Root.F[97]).V[0];
        Assert.IsType<ushort>(copied.V[0]); Assert.IsType<sbyte>(copied.V[1]); Assert.IsType<float>(copied.V[2]);
        copied.V[0] = (ushort)9;
        ((JsonNode)((BtlVector)clone.Root.F[96]).V[0])["unknown"][2]["value"] = 8;
        Assert.Equal((ushort)4321, structure.V[0]);
        Assert.Equal(3, ((JsonNode)packed.V[0])["unknown"][2]["value"].GetValue<int>());
        Assert.False(BtlFrontJson.ContentEquals(doc, clone));
    }

    [Fact]
    public void Snapshot_equality_compares_numeric_values_but_keeps_presence_and_metadata()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        doc.Root.F[2] = BtlFrontJson.Scalar("u16", (long)0);
        var copy = BtlFrontJson.CloneDocument(doc);
        ((BtlScalar)copy.Root.F[2]).V = (ushort)0;
        Assert.True(BtlFrontJson.ContentEquals(doc, copy));
        copy.Root.F.Remove(2);
        Assert.False(BtlFrontJson.ContentEquals(doc, copy));
        copy = BtlFrontJson.CloneDocument(doc); copy.FormatVersion++;
        Assert.False(BtlFrontJson.ContentEquals(doc, copy));
    }

    [Fact]
    public void Vector_lookup_refreshes_after_move_and_remove_append_of_same_size()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var units = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        for (int i = 0; i < 3; i++) units.V.Add(UnitEdits.Create((ushort)i, 1, (ushort)i, 101, 1, 0, 10, 20));
        var host = new ScriptHost(); EditorScripts.Load(host); host.Load(doc);
        host.Execute("""
            editor.action("lookup", function()
              local moved = game.units.get(1)
              assert(moved.hp == 10 and moved.index == 1)
              game.units.move(1, 0)
              assert(moved.index == 0 and moved.cell_index == 1)
              game.units.remove(0)
              game.units.create {cell_index=8,hp=5,max_hp=15}
              assert(not pcall(function() return moved.hp end))
              assert(game.units.index_at_cell(8)==2)
              assert(game.units.index_at_cell(nil)==nil)
            end)
            """, "lookup.lua");
        host.Run("lookup", null);
        Assert.Equal(3, units.V.Count);
    }

    [Fact]
    public void Pooled_invocations_keep_argument_count_and_recover_after_getter_error()
    {
        var host = new ScriptHost(); host.Load(new BtlFrontDocument { Root = BtlFrontJson.NewTable() });
        host.Execute("""
            editor.action("good", {get=function(...) assert(select('#',...)==1); return (...).value end})
            editor.action("bad", {get=function() error('read failed') end})
            """, "pool.lua");
        for (int i = 0; i < 50; i++) Assert.Equal((double)i, host.CallGet("good", new ScriptArgs { Value = i }));
        Assert.Throws<FrontEditException>(() => host.CallGet("bad"));
        Assert.Equal(123d, host.CallGet("good", new ScriptArgs { Value = 123 }));
        Assert.Equal(0, host.SnapshotCount);
    }
}
