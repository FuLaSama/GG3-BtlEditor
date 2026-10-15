using BtlCore.Fb;
using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class ResearchLayoutTests
{
    static BtlFrontDocument Document() => new() { Root=BtlFrontJson.NewTable(), Context=new SchemaContext(FbsSchema.LoadFromText("""
        struct Pair { index:short; value:ubyte; }
        table Entry { name:string; enabled:bool; }
        table Root { count:byte; enabled:bool; name:string; values:[short]; rows:[Entry]; pair:Pair; }
        root_type Root;
        """)) };

    [Fact]
    public void Local_edits_preserve_unknown_siblings_and_missing_false_zero_and_empty_string()
    {
        var doc=Document(); FrontEdit.SetScalar(doc.Root,90,"u32",(uint)9182);
        ResearchEdits.Set(doc,"","Root",0,"0");
        ResearchEdits.Set(doc,"","Root",1,"false");
        ResearchEdits.Set(doc,"","Root",2,"");
        Assert.Equal((sbyte)0,((BtlScalar)doc.Root.F[0]).V);
        Assert.Equal(false,((BtlScalar)doc.Root.F[1]).V);
        Assert.Equal("",((BtlScalar)doc.Root.F[2]).V);
        ResearchEdits.Set(doc,"","Root",0,"");
        Assert.False(doc.Root.F.ContainsKey(0));
        Assert.Equal((uint)9182,((BtlScalar)doc.Root.F[90]).V);
        string before=BtlFrontJson.Serialize(doc);
        foreach(string value in new[]{"128","-129","1.5","NaN","Infinity","bad"})
        {
            Assert.ThrowsAny<Exception>(()=>ResearchEdits.Set(doc,"","Root",0,value));
            Assert.Equal(before,BtlFrontJson.Serialize(doc));
        }
    }

    [Fact]
    public void Arrays_distinguish_absence_empty_and_signed_values_and_reject_invalid_input_atomically()
    {
        var doc=Document(); ResearchEdits.ReplaceArray(doc,"","Root",3,"-1, 0，32767");
        Assert.Equal(new object[]{(short)-1,(short)0,(short)32767},((BtlVector)doc.Root.F[3]).V);
        string before=BtlFrontJson.Serialize(doc);
        Assert.ThrowsAny<Exception>(()=>ResearchEdits.ReplaceArray(doc,"","Root",3,"1,32768"));
        Assert.Equal(before,BtlFrontJson.Serialize(doc));
        ResearchEdits.ReplaceArray(doc,"","Root",3,"[]"); Assert.Empty(((BtlVector)doc.Root.F[3]).V);
        ResearchEdits.ReplaceArray(doc,"","Root",3,""); Assert.False(doc.Root.F.ContainsKey(3));
        Assert.True(doc.Editing.Undo()); Assert.Empty(((BtlVector)doc.Root.F[3]).V);
    }

    [Fact]
    public void Nested_list_and_inline_struct_edits_use_one_history_frame_and_survive_binary_roundtrip()
    {
        var doc=Document(); doc.Editing.ResetHistory(); int history=doc.Editing.HistoryCount;
        Assert.Equal(0,ResearchEdits.Add(doc,"","Root",4));
        Assert.Equal(history+1,doc.Editing.HistoryCount);
        ResearchEdits.Set(doc,"/4[0]","Entry",0,"新配置");
        ResearchEdits.Set(doc,"/4[0]","Entry",1,"false");
        ResearchEdits.Ensure(doc,"","Root",5);
        ResearchEdits.SetMember(doc,"/5","Pair",0,"-1");
        ResearchEdits.SetMember(doc,"/5","Pair",1,"0");
        var built=FrontToBtl.Build(doc); Assert.True(built.Ok,string.Join(";",built.Issues));
        var read=BtlToFront.FromBytes(built.Bytes,doc.Context); Assert.False(read.HasUnresolvedData);
        var row=(BtlTable)((BtlVector)read.Document.Root.F[4]).V[0];
        Assert.Equal("新配置",((BtlScalar)row.F[0]).V); Assert.Equal(false,((BtlScalar)row.F[1]).V);
        Assert.Equal((short)-1,((BtlStruct)read.Document.Root.F[5]).V[0]);
        ResearchEdits.Remove(doc,"/4",0); Assert.Empty(((BtlVector)doc.Root.F[4]).V);
        Assert.True(doc.Editing.Undo()); Assert.Single(((BtlVector)doc.Root.F[4]).V);
    }

    [Fact]
    public void Mismatched_or_encoded_nodes_cannot_be_replaced_by_generic_edits()
    {
        var doc=Document(); doc.Root.F[0]=BtlFrontJson.NewTable();
        var vector=FrontEdit.EnsureVec(doc.Root,3,"i16"); vector.Enc="custom";
        string before=BtlFrontJson.Serialize(doc);
        Assert.Throws<FrontEditException>(()=>ResearchEdits.Set(doc,"","Root",0,"1"));
        Assert.Throws<FrontEditException>(()=>ResearchEdits.ReplaceArray(doc,"","Root",3,"1"));
        Assert.Equal(before,BtlFrontJson.Serialize(doc));
    }

    [Fact]
    public void Research_overlay_handles_signed_cells_rewards_medals_and_building_buffs_together()
    {
        var host=new ScriptHost(); EditorScripts.Load(host);
        var doc=new BtlFrontDocument {Context=host.CreateSchemaContext(),Root=BtlFrontJson.NewTable()};
        var units=FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,6),0,"table");
        units.V.Add(UnitEdits.Create(65535,0,10,101,256,0,10,20,doc.Context));
        ResearchEdits.Set(doc,"/6/0[0]","AIAgent",5,"-5");
        ResearchEdits.Ensure(doc,"/6/0[0]","AIAgent",11);
        ResearchEdits.Set(doc,"/6/0[0]/11","AIAgentTable11",3,"123");
        var events=FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,5),0,"table"); events.V.Add(BtlFrontJson.NewTable());
        ResearchEdits.Ensure(doc,"/5/0[0]","TriggerEvent",3);
        ResearchEdits.Ensure(doc,"/5/0[0]/3","TriggerEventBldg",1);
        ResearchEdits.SetMember(doc,"/5/0[0]/3/1","BuildingExtraData",0,"2");
        ResearchEdits.Add(doc,"/5/0[0]/3","TriggerEventBldg",3);
        ResearchEdits.Set(doc,"/5/0[0]/3/3[0]","TriggerEventSub3",0,"22");
        var built=FrontToBtl.Build(doc); Assert.True(built.Ok,string.Join(";",built.Issues));
        var read=BtlToFront.FromBytes(built.Bytes,doc.Context); Assert.False(read.HasUnresolvedData,string.Join(";",read.Issues));
        Assert.Equal((short)-1,((BtlStruct)((BtlTable)((BtlVector)((BtlTable)read.Document.Root.F[6]).F[0]).V[0]).F[0]).V[0]);
        Assert.Equal((sbyte)-5,((BtlScalar)((BtlTable)ResearchEdits.Node(read.Document,"/6/0[0]")).F[5]).V);
        Assert.Equal((ushort)123,((BtlScalar)((BtlTable)ResearchEdits.Node(read.Document,"/6/0[0]/11")).F[3]).V);
        Assert.Equal((ushort)22,((BtlScalar)((BtlTable)ResearchEdits.Node(read.Document,"/5/0[0]/3/3[0]")).F[0]).V);
    }

    [Fact]
    public void Labels_are_short_and_all_registered_research_fields_have_names()
    {
        string root=EditorScripts.Root();
        var labels=ResearchLabels.Load(Path.Combine(root,"EditorLayout/fields/research-labels.json"));
        var host=new ScriptHost(); EditorScripts.Load(host); var schema=host.CreateSchemaContext().Schema;
        Assert.Equal(59,labels.Types.Count);
        foreach(var (type,definition) in labels.Types)
        foreach(var (fieldName,label) in definition.Fields)
        {
            var fields=schema.IsStruct(type) ? schema.StructMembers(type) : schema.FieldsOf(type);
            // AgentInfo retains two packed u16 members for existing Lua API compatibility.
            if(type=="AgentInfo" && fieldName is "unit_level" or "move_points") continue;
            Assert.Contains(fields,f=>f.Name==fieldName);
            Assert.InRange(label.Label.Length,1,12);
        }
    }

    [Fact]
    public void Nested_xml_groups_share_bindings_and_reject_invalid_fold_flags()
    {
        string user=Path.Combine(Path.GetTempPath(),"btl-research-layout-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(user,"pages"));
            File.WriteAllText(Path.Combine(user,"layout.xml"),"""
                <editorLayout format="1"><tabs><tab id="unit" title="部队" page="pages/unit.xml"/></tabs></editorLayout>
                """);
            string page="""
                <page id="unit"><section kind="form" bind="/6/0" resolve="unit">
                  <group id="outer" title="外层" collapsible="true" collapsed="true">
                    <group id="inner" title="内层" collapsible="true" collapsed="false">
                      <row label="生命"><field id="hp" field="unit.hp" tip="当前生命值"/></row>
                    </group>
                  </group>
                </section></page>
                """;
            string file=Path.Combine(user,"pages/unit.xml"); File.WriteAllText(file,page);
            var result=LayoutLoader.Load(Path.Combine(EditorScripts.Root(),"EditorLayout"),user);
            Assert.True(result.Ok,string.Join(";",result.Errors));
            var section=result.Bundle.Tabs.Single(t=>t.Id=="unit").Page.Sections[0];
            Assert.True(section.Groups[0].Collapsed); Assert.False(section.Groups[0].Groups[0].Collapsed);
            Assert.Equal("hp",Assert.Single(section.Fields).Id); Assert.Equal("当前生命值",section.Fields[0].Tip);
            File.WriteAllText(file,page.Replace("collapsed=\"true\"","collapsed=\"yes\""));
            Assert.False(LayoutLoader.Load(Path.Combine(EditorScripts.Root(),"EditorLayout"),user).Ok);
            File.WriteAllText(file,page.Replace("collapsible=\"true\"","collapsible=\"false\""));
            Assert.False(LayoutLoader.Load(Path.Combine(EditorScripts.Root(),"EditorLayout"),user).Ok);
        }
        finally { if(Directory.Exists(user))Directory.Delete(user,true); }
    }

    [Fact]
    public void Older_forms_without_medal_preserve_it_but_explicitly_clearing_the_new_control_removes_it()
    {
        var host=new ScriptHost(); EditorScripts.Load(host,"unit/配置表单.lua");
        var doc=new BtlFrontDocument {Context=host.CreateSchemaContext(),Root=BtlFrontJson.NewTable()};
        var unit=UnitEdits.Create(0,0,1,101,256,0,10,20,doc.Context);
        FrontEdit.EnsureVec(FrontEdit.EnsureTable(doc.Root,6),0,"table").V.Add(unit);
        var general=FrontEdit.EnsureTable(unit,11);FrontEdit.SetScalar(general,3,"u16",(ushort)77);
        host.Load(doc);host.Execute("editor.action('save_form',function(ctx) editor.modules.unit_forms.write_all(game.units.get(0),ctx.input) end)","form-test.lua");
        var input=new Dictionary<string,object>{{"general",true},{"general_id",0}};
        host.Run("save_form",new ScriptArgs{Input=input});
        Assert.Equal((ushort)77,((BtlScalar)general.F[3]).V);
        input["__fields"]=new Dictionary<string,object>{{"general_medal",true}};
        host.Run("save_form",new ScriptArgs{Input=input});
        Assert.False(general.F.ContainsKey(3));
    }
}
