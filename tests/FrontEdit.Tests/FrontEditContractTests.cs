using System.Text.Json.Nodes;
using BtlCore.Fb;
using BtlCore.Front;
using Xunit;

namespace BtlCore.Front.Tests;

public class FrontEditContractTests
{
    [Fact]
    public void SetScalar_null_removes_slot_and_zero_keeps_it()
    {
        var tbl = BtlFrontJson.NewTable();
        FrontEdit.SetScalar(tbl, 3, "u16", (ushort)4);
        Assert.True(tbl.F.ContainsKey(3));

        FrontEdit.SetScalar(tbl, 3, "u16", null);
        Assert.False(tbl.F.ContainsKey(3));

        FrontEdit.SetScalar(tbl, 3, "u16", (ushort)0);
        Assert.True(tbl.F.ContainsKey(3));
        var sc = Assert.IsType<BtlScalar>(tbl.F[3]);
        Assert.Equal((ushort)0, sc.V);
    }

    [Fact]
    public void SetMember_null_writes_zero_and_struct_stays()
    {
        var tbl = BtlFrontJson.NewTable();
        var st = BtlFrontJson.NewStruct(9);
        FrontEdit.SetField(tbl, 0, st);

        FrontEdit.SetMember(st, 0, null);

        Assert.Same(st, tbl.F[0]);
        Assert.Equal(0, st.V[0]);
    }

    [Fact]
    public void Packed_vector_rejects_raw_edits_and_tree_is_unchanged()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var vec = new BtlVector
        {
            T = "vector",
            Elem = "u8",
            Enc = PackedJsonStream.EncName
        };
        vec.V.Add(new JsonObject { ["btid"] = 1 });
        doc.Root.F[10] = vec;
        string before = BtlFrontJson.Serialize(doc);

        Assert.Throws<FrontEditException>(() => FrontEdit.SetAt(vec, 0, (byte)1));
        Assert.Throws<FrontEditException>(() => FrontEdit.Insert(vec, 1, (byte)2));
        Assert.Throws<FrontEditException>(() => FrontEdit.RemoveAt(vec, 0));
        Assert.Throws<FrontEditException>(() => FrontEdit.Fill(vec, new object[] { (byte)3 }));

        Assert.Equal(before, BtlFrontJson.Serialize(doc));
    }
}
