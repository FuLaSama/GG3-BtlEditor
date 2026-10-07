using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class StructLayoutTests
{
    static BtlStruct TenU16(bool fromFbs)
    {
        var values = new object[10];
        for (int i = 0; i < 10; i++) values[i] = (ushort)(i + 1);
        if (fromFbs) return FrontEdit.StructFromFbs("AgentInfo", values);
        var types = new string[10];
        for (int i = 0; i < 10; i++) types[i] = "u16";
        var st = FrontEdit.NewStruct(types);
        for (int i = 0; i < 10; i++) st.V[i] = values[i];
        return st;
    }

    static BtlFrontDocument Hang(BtlStruct info)
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)1);
        var agents = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 6), 0, "table");
        var agent = BtlFrontJson.NewTable();
        agent.F[0] = info;
        agents.V.Add(agent);
        return doc;
    }

    static BtlStruct ReadBack(BtlFrontDocument doc)
    {
        var built = FrontToBtl.Build(doc);
        Assert.True(built.Ok, string.Join("\n", built.Issues));
        var loaded = BtlToFront.FromBytes(built.Bytes);
        Assert.True(loaded.Ok);
        var ai = (BtlTable)loaded.Document.Root.F[6];
        var agents = (BtlVector)ai.F[0];
        var agent = (BtlTable)agents.V[0];
        return (BtlStruct)agent.F[0];
    }

    [Fact]
    public void Script_layout_matches_fbs_bytes_for_agent_info()
    {
        var named = FrontToBtl.Build(Hang(TenU16(true)));
        var declared = FrontToBtl.Build(Hang(TenU16(false)));
        Assert.True(named.Ok && declared.Ok);
        Assert.Equal(named.Bytes, declared.Bytes);

        var back = ReadBack(Hang(TenU16(false)));
        Assert.Equal(Enumerable.Repeat("u16", 10), back.Layout);
        for (int i = 0; i < 10; i++)
            Assert.Equal(i + 1, Convert.ToInt32(back.V[i]));
    }

    [Fact]
    public void Layout_edits_survive_json_and_change_written_size()
    {
        var st = TenU16(false);
        FrontEdit.InsertMember(st, 2, "u8", (byte)9);
        FrontEdit.RemoveMember(st, 4);
        FrontEdit.SetMemberType(st, 0, "u8");

        var again = BtlFrontJson.Clone(st) as BtlStruct;
        Assert.Equal(new[] { "u8", "u16", "u8", "u16", "u16", "u16", "u16", "u16", "u16", "u16" }, again.Layout);
        Assert.Equal(9, Convert.ToInt32(again.V[2]));

        var narrow = FrontEdit.NewStruct(Enumerable.Repeat("u8", 10).ToArray());
        for (int i = 0; i < 10; i++) narrow.V[i] = (byte)(i + 1);
        int wide = FrontToBtl.Build(Hang(TenU16(false))).Bytes.Length;
        int slim = FrontToBtl.Build(Hang(narrow)).Bytes.Length;
        Assert.True(wide > slim);
    }

    [Fact]
    public void Slot_writes_field_number_and_struct_layout()
    {
        var doc = new BtlFrontDocument { Root = BtlFrontJson.NewTable() };
        var events = FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root, 5), 0, "table");
        events.V.Add(BtlFrontJson.NewTable());
        var host = new ScriptHost();
        host.Load(doc);
        host.Execute(@"
editor.action('paint', function(ctx)
  local row = editor.slot(ctx.object, 40, 'table')
  editor.set(row, 7, 'u16', 300)
  local st = editor.slot(row, 1, 'struct', { 'u16', 'u16' })
  editor.set_member(st, 0, 4)
  editor.insert_member(st, 1, 'u8', 5)
  editor.remove_member(st, 2)
  editor.member_type(st, 0, 'u8')
end)
", "layout.lua");
        host.Run("paint", new ScriptArgs { ObjectPath = "Root.trigger_info.events", ObjectIndex = 0 });

        var row = (BtlTable)((BtlTable)events.V[0]).F[40];
        var sc = Assert.IsType<BtlScalar>(row.F[7]);
        Assert.Equal("u16", sc.T);
        Assert.Equal(300, Convert.ToInt32(sc.V));
        var st = (BtlStruct)row.F[1];
        Assert.Equal(new[] { "u8", "u8" }, st.Layout);
        Assert.Equal(4, Convert.ToInt32(st.V[0]));
        Assert.Equal(5, Convert.ToInt32(st.V[1]));
    }
}
