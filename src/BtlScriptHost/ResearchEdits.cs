using System.Globalization;
using System.Text.RegularExpressions;
using BtlCore.Fb;
using BtlCore.Front;

namespace BtlCore.Scripting;

/// <summary>详细配置页的局部编辑；只改明确选择的字段，不复制或补写整个对象。</summary>
public static class ResearchEdits
{
    public static BtlNode Node(BtlFrontDocument doc,string path) => path.Length==0 ? doc.Root : NsPath.NodeAt(doc,path);
    static BtlTable Parent(BtlFrontDocument doc,string path) => Node(doc,path) as BtlTable ?? throw new FrontEditException("上级配置不存在，请先创建配置。");
    static FieldHint Hint(BtlFrontDocument doc,string owner,int id) => doc.Context.TryGet(owner,id,out var hint) ? hint : throw new FrontEditException("当前格式解释尚未包含此字段，请重新打开关卡。");
    public static object Parse(string type,string text)
    {
        if(type=="string") return text;
        if(string.IsNullOrWhiteSpace(text)) return null;
        if(type is "u64" or "i64") return FrontPath.Coerce(type,text.Trim());
        if(type=="bool") return bool.Parse(text);
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double number) || !double.IsFinite(number))
            throw new FrontEditException("请输入有效数字。");
        object value=FrontPath.Coerce(type,number);
        if(value is float f && !float.IsFinite(f)) throw new FrontEditException("数值超出允许范围。");
        return value;
    }
    public static void Set(BtlFrontDocument doc,string parentPath,string owner,int id,string text)
    {
        var hint=Hint(doc,owner,id);
        if(hint.Kind is not (FieldKind.Scalar or FieldKind.Bool or FieldKind.String)) throw new FrontEditException("此字段不是普通值。");
        string type=hint.Kind==FieldKind.String ? "string" : hint.ScalarT;
        object value=Parse(type,text);
        doc.Editing.Execute(()=>{
            var parent=Parent(doc,parentPath);
            if(parent.F.TryGetValue(id,out var old) && old is not BtlScalar) throw new FrontEditException("字段结构与当前解释不同，不能替换未知内容。");
            FrontEdit.SetScalar(parent,id,type,value);
        });
    }
    public static void SetMember(BtlFrontDocument doc,string path,string type,int index,string text)
    {
        if(!doc.Context.TryStructMembers(type,out var members) || index<0 || index>=members.Length) throw new FrontEditException("结构体解释不完整。");
        object value=Parse(members[index].T,text) ?? SoftSchema.DefaultValue(members[index].T);
        doc.Editing.Execute(()=>{
            var structure=Node(doc,path) as BtlStruct ?? throw new FrontEditException("配置已不存在。");
            if(!structure.Layout.SequenceEqual(members.Select(m=>m.T))) throw new FrontEditException("结构体布局不一致，不能猜测修改。");
            FrontEdit.SetMember(structure,index,value);
        });
    }
    public static void Ensure(BtlFrontDocument doc,string parentPath,string owner,int id)
    {
        var hint=Hint(doc,owner,id);
        doc.Editing.Execute(()=>{
            var parent=Parent(doc,parentPath);
            if(parent.F.ContainsKey(id)) return;
            BtlNode node=hint.Kind switch {
                FieldKind.Table=>BtlFrontJson.NewTable(),
                FieldKind.Struct=>FrontEdit.NewStruct(hint.StructMembers.Select(m=>m.T).ToArray()),
                FieldKind.Vector=>BtlFrontJson.NewVector(hint.VectorElem),
                _=>throw new FrontEditException("只能创建表、结构体或列表。")
            };
            if(node is BtlVector vector && hint.VectorElem=="struct") vector.StructLayout.AddRange(hint.VectorStructMembers.Select(m=>m.T));
            FrontEdit.SetField(parent,id,node);
        });
    }
    public static void Clear(BtlFrontDocument doc,string parentPath,int id) => doc.Editing.Execute(()=>FrontEdit.SetField(Parent(doc,parentPath),id,null));
    public static void ReplaceArray(BtlFrontDocument doc,string parentPath,string owner,int id,string text)
    {
        var hint=Hint(doc,owner,id);
        if(hint.Kind!=FieldKind.Vector || hint.VectorElem is "table" or "struct") throw new FrontEditException("此列表需要逐项编辑。");
        if(string.IsNullOrWhiteSpace(text)) { Clear(doc,parentPath,id); return; }
        string[] parts=text.Trim()=="[]" ? Array.Empty<string>() : Regex.Split(text.Trim(),@"[,，;；\s]+");
        if(parts.Length>200_000) throw new FrontEditException("列表超过编辑长度限制。");
        var values=parts.Select(p=>Parse(hint.VectorElem,p) ?? throw new FrontEditException("列表不能包含空值。")).ToArray();
        doc.Editing.Execute(()=>{
            var parent=Parent(doc,parentPath);
            if(parent.F.TryGetValue(id,out var old) && (old is not BtlVector v || !string.IsNullOrEmpty(v.Enc))) throw new FrontEditException("列表结构不匹配或有特殊编码。");
            var vector=FrontEdit.EnsureVec(parent,id,hint.VectorElem);
            if(vector.Elem!=hint.VectorElem) throw new FrontEditException("列表元素类型与当前解释不一致。");
            FrontEdit.Fill(vector,values);
        });
    }
    public static int Add(BtlFrontDocument doc,string parentPath,string owner,int id)
    {
        var hint=Hint(doc,owner,id); int index=-1;
        if(hint.Kind!=FieldKind.Vector || hint.VectorElem is not ("table" or "struct")) throw new FrontEditException("此列表不能追加对象。");
        doc.Editing.Execute(()=>{
            Ensure(doc,parentPath,owner,id);
            var vector=(BtlVector)Parent(doc,parentPath).F[id];
            if(!string.IsNullOrEmpty(vector.Enc) || vector.Elem!=hint.VectorElem) throw new FrontEditException("列表类型不匹配。");
            index=vector.V.Count;
            FrontEdit.Insert(vector,index,hint.VectorElem=="table" ? BtlFrontJson.NewTable() : FrontEdit.NewStruct(hint.VectorStructMembers.Select(m=>m.T).ToArray()));
        }); return index;
    }
    public static void Remove(BtlFrontDocument doc,string path,int index) => doc.Editing.Execute(()=>FrontEdit.RemoveAt(Node(doc,path) as BtlVector,index));
    public static string Text(object value) => value==null ? "" : Convert.ToString(value,CultureInfo.InvariantCulture);
}
