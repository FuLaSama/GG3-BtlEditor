using System.Text.RegularExpressions;
using BtlCore.Fb;
using BtlCore.Front;
using BtlCore.Scripting;

namespace BtldMapEditor;

public partial class LayoutPageControl
{
    sealed record DetailView(Control Control,Action Refresh);
    sealed class DetailBinding
    {
        public LayoutSection Section;
        public BtlFrontDocument Document;
        public string Path;
        public int? Cell;
        public bool Pending;
    }
    sealed class DetailsSection(LayoutSection section,Panel panel)
    {
        public LayoutSection Section=section;
        public Panel Panel=panel;
        public DetailBinding Binding;
        public DetailView View;
        public SchemaContext Context;
        public int? BrowseIndex;
        public int? FollowIndex;
    }
    readonly List<DetailsSection> _details=new();
    readonly Dictionary<string,int> _detailRows=new();
    int _detailsRefresh;
    bool _refreshingDetails;
    Control BuildDetails(LayoutSection section)
    {
        var panel=new BufferedPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top};
        _details.Add(new DetailsSection(section,panel));return panel;
    }
    void RefreshDetails(bool selectionOnly=false,string selectionBind=null)
    {
        if(_refreshingDetails || _host.Document==null)return;
        _detailsRefresh++;_refreshingDetails=true;
        using var update=BeginUiUpdate();
        try
        {
            foreach(var item in _details)
            {
                var section=item.Section;
                if(selectionOnly && string.IsNullOrEmpty(section.Resolve) && string.IsNullOrEmpty(section.Selection))continue;
                if(selectionBind!=null && section.Selection!=selectionBind)continue;
                if(item.View==null || !ReferenceEquals(item.Context,_host.Document.Context))
                {
                    item.View?.Control.Dispose();item.Panel.Controls.Clear();
                    item.Binding=new DetailBinding{Section=section,Document=_host.Document};item.Context=_host.Document.Context;
                    var omitted=(section.Omit??"").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet();
                    item.View=DetailFold(item.Binding,()=>section.Title,SectionKey(section),section.Collapsed,
                        ()=>section.Browse ? BrowseDetails(item,omitted) : !string.IsNullOrEmpty(section.Prepare)
                            ? CellDetails(item.Binding,omitted) : DetailsObject(item.Binding,()=>item.Binding.Path,section.Model,omitted,0));
                    item.Panel.Controls.Add(item.View.Control);
                }
                item.Binding.Document=_host.Document;
                item.Binding.Cell=_cell;
                item.Binding.Pending=false;
                string path=section.Bind=="/" ? "" : section.Bind;
                if(section.Browse || !string.IsNullOrEmpty(section.Resolve) || !string.IsNullOrEmpty(section.Selection))
                {
                    int? index=!string.IsNullOrEmpty(section.Selection) ? SelectedRow(section.Selection) : null;
                    index ??= !string.IsNullOrEmpty(section.Resolve) ? _host.ResolveIndex(section.Resolve,_cell) : null;
                    if(section.Browse)
                    {
                        var vector=ResearchEdits.Node(_host.Document,path) as BtlVector;
                        int count=vector?.V.Count??0;
                        if(index.HasValue && (item.BrowseIndex==null || index!=item.FollowIndex))item.BrowseIndex=index;
                        item.FollowIndex=index;
                        item.BrowseIndex=count==0 ? null : Math.Clamp(item.BrowseIndex??0,0,count-1);
                        index=item.BrowseIndex;
                    }
                    if(index==null && !string.IsNullOrEmpty(section.Prepare) && _cell>=0
                        && _cell<Front.FrontNav.MapWidth(_host.Document)*Front.FrontNav.MapHeight(_host.Document))
                    {
                        item.Binding.Pending=true;
                        index=(ResearchEdits.Node(_host.Document,path) as BtlVector)?.V.Count??0;
                    }
                    path=index.HasValue ? path+"["+index+"]" : null;
                }
                item.Binding.Path=path;
                item.View.Refresh();
            }
        }
        finally {_refreshingDetails=false;}
    }
    DetailView CellDetails(DetailBinding binding,HashSet<string> omitted)
    {
        var body=new DetailsStack();
        var label=new Label{Name="detail.cell"+binding.Section.Bind,AutoSize=true};body.Add(label);
        var detail=DetailsObject(binding,()=>binding.Path,binding.Section.Model,omitted,0);body.Add(detail.Control);
        return new DetailView(body,()=>
        {
            DetailText(label,binding.Path!=null ? "地块 ID："+binding.Cell : "请先在地图上选择地块。");
            detail.Refresh();
        });
    }
    DetailView BrowseDetails(DetailsSection item,HashSet<string> omitted)
    {
        var binding=item.Binding;var section=item.Section;
        var body=new DetailsStack();
        body.Add(new Label{AutoSize=true,Text="查看对象（也可直接在地图或上方列表选择）"});
        var picker=new ComboBox{Name="detail.browse"+section.Bind,DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top};
        body.Add(picker);
        var detail=DetailsObject(binding,()=>binding.Path,section.Model,omitted,0);body.Add(detail.Control);
        bool changing=false;
        picker.SelectedIndexChanged+=(_,_)=>
        {
            if(changing || _loading || _refreshingDetails || picker.SelectedIndex<0)return;
            using var update=BeginUiUpdate();
            item.BrowseIndex=picker.SelectedIndex;binding.Path=section.Bind+"["+item.BrowseIndex+"]";
            // 与本页势力列表保持一致；地图上的选择不因浏览详情而改变。
            var list=_lists.FirstOrDefault(l=>l.Section.Bind==section.Selection);
            if(list!=null)
            {
                int row=list.Rows.IndexOf(picker.SelectedIndex);
                bool loading=_loading;_loading=true;
                try {list.View.SelectedIndices.Clear();if(row>=0)list.View.Items[row].Selected=true;ShowSelection(list);}
                finally {_loading=loading;}
                item.FollowIndex=SelectedRow(section.Selection);
            }
            detail.Refresh();
        };
        return new DetailView(body,()=>
        {
            var vector=ResearchEdits.Node(binding.Document,section.Bind) as BtlVector;
            var names=(vector?.V??new List<object>()).Select((row,index)=>DetailChoice(section.Model,row,index)).ToArray();
            changing=true;picker.BeginUpdate();
            try
            {
                if(!picker.Items.Cast<string>().SequenceEqual(names)) {picker.Items.Clear();picker.Items.AddRange(names);}
                picker.SelectedIndex=item.BrowseIndex is int selected && selected<names.Length ? selected : -1;picker.Enabled=names.Length>0;
            }
            finally {picker.EndUpdate();changing=false;}
            detail.Refresh();
        });
    }
    static string DetailChoice(string model,object row,int index)
    {
        var table=row as BtlTable;var info=table?.F.GetValueOrDefault(0);
        string identity=model=="TriggerEvent" && info is BtlScalar cell ? "地块 ID "+ResearchEdits.Text(cell.V)
            : model=="Faction" && info is BtlStruct faction && faction.V.Count>1
                ? "势力 "+ResearchEdits.Text(faction.V[0])+" · 国家 "+ResearchEdits.Text(faction.V[1]) : "对象";
        return "第 "+(index+1)+" 项 · "+identity;
    }
    string DetailKey(DetailBinding binding,string path) => SectionKey(binding.Section)+"/detail"+Regex.Replace(path??"",@"\[\d+\]","[]");
    static void DetailName(Control control,string prefix,string path)
    {
        string name=prefix+path;if(control.Name!=name)control.Name=name;
    }
    static void DetailText(Control control,string text) {if(control.Text!=text)control.Text=text;}
    bool DetailWritable(DetailBinding binding) => ReferenceEquals(binding.Document,_host.Document) && !binding.Document.HasUnresolvedData;
    static BtlNode DetailNode(DetailBinding binding,Func<string> path) => binding.Pending ? null
        : path() is string current ? ResearchEdits.Node(binding.Document,current) : null;
    Button DetailButton(DetailBinding binding,string text,Action action)
    {
        var button=new Button{Text=text,AutoSize=true,Margin=new Padding(3)};
        button.Click+=(_,_)=>
        {
            if(_loading || _refreshingDetails)return;
            using var update=BeginUiUpdate();
            try
            {
                if(!DetailWritable(binding))throw new FrontEditException("当前配置不可编辑，请重新选择对象。");
                int refreshed=_detailsRefresh;
                if(binding.Pending)
                {
                    // 首次写入时才补地块记录，并与实际修改共用一个事务；校验失败一并回滚。
                    binding.Document.Editing.Execute(()=>
                    {
                        _host.Run(binding.Section.Prepare,new ScriptArgs{CellIndex=binding.Cell});
                        int index=_host.ResolveIndex(binding.Section.Resolve,binding.Cell)
                            ?? throw new FrontEditException("无法创建当前地块的附加记录。");
                        binding.Path=binding.Section.Bind+"["+index+"]";binding.Pending=false;
                        action();
                    });
                }
                else action();
                _committed?.Invoke();
                if(refreshed==_detailsRefresh)RefreshDetails();
            }
            catch(Exception ex) {ShowError(ex);RefreshDetails();}
        };
        return button;
    }
    DetailView DetailFold(DetailBinding binding,Func<string> title,string key,bool collapsed,Func<DetailView> create)
    {
        DetailView child=null;
        var fold=Fold(title(),key,collapsed,()=>{child=create();child.Refresh();return child.Control;});
        void Refresh()
        {
            fold.SetTitle(title());
            if(!fold.Collapsed)child?.Refresh();
        }
        fold.StateChanged+=closed=>{if(!closed){using var update=BeginUiUpdate();child?.Refresh();}};
        return new DetailView(fold,Refresh);
    }
    DetailView DetailsObject(DetailBinding binding,Func<string> path,string type,HashSet<string> omitted,int depth)
    {
        var stack=new DetailsStack();
        if(depth>16)
        {
            stack.Add(new Label{AutoSize=true,Text="配置嵌套过深，请使用注册表查看。"});return new DetailView(stack,()=>{});
        }
        var status=new Label{AutoSize=true};stack.Add(status);
        var create=DetailButton(binding,"创建配置",()=>
        {
            string current=path();int cut=current.LastIndexOf('/');
            string parent=current[..cut];int id=int.Parse(current[(cut+1)..]);
            ResearchEdits.Ensure(binding.Document,parent,OwnerType(binding.Document,parent),id);
        });stack.Add(create);
        var fields=new DetailsStack();stack.Add(fields);
        List<DetailView> children=null;
        void Refresh()
        {
            string current=path();var node=DetailNode(binding,path);var schema=binding.Document.Context.Schema;
            bool structure=schema.IsStruct(type);
            bool pendingRoot=binding.Pending && depth==0 && current==binding.Path;
            bool present=pendingRoot || (structure ? node is BtlStruct : node is BtlTable);
            DetailName(create,"detail.create",current);create.Enabled=current!=null && DetailWritable(binding);
            DetailText(status,pendingRoot ? "此地块尚无附加记录，修改时才写入。" : current==null ? binding.Section.Browse ? "当前没有可查看的对象，请先添加。" : "请先选择对应对象。" : node==null ? "此配置未写入。" : "数据结构与当前解释不同，保留原内容。");
            stack.SetItemVisible(status,!present || pendingRoot);stack.SetItemVisible(create,current!=null && node==null && !pendingRoot);
            stack.SetItemVisible(fields,present);
            if(!present)return;
            if(children==null)
            {
                children=new();
                foreach(var field in (structure ? schema.StructMembers(type) : schema.FieldsOf(type)).OrderBy(f=>f.Id))
                {
                    if(omitted.Contains(field.Id.ToString()))continue;
                    var label=binding.Section.Labels.Field(type,field);
                    DetailView view;
                    if(structure)
                    {
                        int index=field.Id;
                        var members=binding.Document.Context.TryStructMembers(type,out var declared) ? declared : Array.Empty<StructMember>();
                        string kind=index<members.Length ? members[index].T : "u16";
                        view=DetailValue(binding,label,kind,()=>path()+"."+index,
                            ()=>DetailNode(binding,path) is BtlStruct st && index<st.V.Count ? st.V[index] : null,
                            text=>ResearchEdits.SetMember(binding.Document,path(),type,index,text),null,()=>true);
                    }
                    else
                    {
                        if(!binding.Document.Context.TryGet(type,field.Id,out var hint))continue;
                        Func<string> childPath=()=>path()==null ? null : path()+"/"+field.Id;
                        if(hint.Kind is FieldKind.Scalar or FieldKind.Bool or FieldKind.String)
                            view=DetailValue(binding,label,hint.Kind==FieldKind.String ? "string" : hint.ScalarT,childPath,
                                ()=>(DetailNode(binding,childPath) as BtlScalar)?.V,
                                text=>ResearchEdits.Set(binding.Document,path(),type,field.Id,text),
                                ()=>ResearchEdits.Clear(binding.Document,path(),field.Id),
                                ()=>DetailNode(binding,childPath) is null or BtlScalar);
                        else if(hint.Kind==FieldKind.Vector)
                        {
                            string Title()=>label.Label+(DetailNode(binding,childPath) is BtlVector vec ? "（"+vec.V.Count+"项）" : "（未写）");
                            view=DetailFold(binding,Title,DetailKey(binding,childPath()),true,
                                ()=>DetailsVector(binding,path,type,field,hint,depth+1));
                        }
                        else
                        {
                            string childType=hint.Kind==FieldKind.Table ? hint.ChildTable : hint.StructName;
                            view=DetailFold(binding,()=>label.Label+(DetailNode(binding,childPath)==null ? "（未写）" : ""),DetailKey(binding,childPath()),true,()=>
                            {
                                var body=new DetailsStack();
                                var clear=DetailButton(binding,"清除配置",()=>ResearchEdits.Clear(binding.Document,path(),field.Id));body.Add(clear);
                                var nested=DetailsObject(binding,childPath,childType,new(),depth+1);body.Add(nested.Control);
                                return new DetailView(body,()=>
                                {
                                    DetailName(clear,"detail.clear",childPath());
                                    clear.Enabled=DetailWritable(binding) && DetailNode(binding,childPath)!=null;
                                    nested.Refresh();
                                });
                            });
                        }
                    }
                    children.Add(view);fields.Add(view.Control);
                }
            }
            foreach(var child in children)child.Refresh();
        }
        return new DetailView(stack,Refresh);
    }
    DetailView DetailValue(DetailBinding binding,ResearchFieldLabel label,string kind,Func<string> key,Func<object> read,
        Action<string> apply,Action clear,Func<bool> compatible)
    {
        var row=new BufferedTable{AutoSize=true,Dock=DockStyle.Top,ColumnCount=3,Margin=new Padding(0,2,0,2)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var name=new Label{Text=label.Label+(label.Uncertain ? " *" : ""),AutoSize=true,Anchor=AnchorStyles.Left};row.Controls.Add(name,0,0);
        _fieldTips.SetToolTip(name,label.Tip);
        Control editor;
        if(kind=="bool")
        {
            var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top};combo.Items.AddRange(new object[]{"未写","否","是"});editor=combo;
        }
        else editor=new TextBox{Dock=DockStyle.Top};
        row.Controls.Add(editor,1,0);_fieldTips.SetToolTip(editor,label.Tip);
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,WrapContents=false};
        var commit=DetailButton(binding,"应用",()=>apply(editor is ComboBox c ? c.SelectedIndex==0 ? "" : c.SelectedIndex==2 ? "true" : "false" : editor.Text));
        buttons.Controls.Add(commit);
        var remove=clear==null ? null : DetailButton(binding,"清空",clear);if(remove!=null)buttons.Controls.Add(remove);
        row.Controls.Add(buttons,2,0);
        return new DetailView(row,()=>
        {
            string path=key();object value=read();bool match=compatible();
            DetailName(editor,"detail.value",path);DetailName(commit,"detail.apply",path);
            if(editor is ComboBox combo)
            {
                int index=value==null ? 0 : Convert.ToBoolean(value) ? 2 : 1;if(combo.SelectedIndex!=index)combo.SelectedIndex=index;
            }
            else
            {
                DetailText(editor,ResearchEdits.Text(value));
                ((TextBox)editor).PlaceholderText=!match ? "结构不匹配" : value==null ? "未写" : "";
            }
            commit.Enabled=match && DetailWritable(binding);editor.Enabled=match;
            if(remove!=null) {DetailName(remove,"detail.clear",path);remove.Enabled=value!=null && match && DetailWritable(binding);}
        });
    }
    DetailView DetailsVector(DetailBinding binding,Func<string> parent,string type,FbsField field,FieldHint hint,int depth)
    {
        Func<string> path=()=>parent()==null ? null : parent()+"/"+field.Id;
        var stack=new DetailsStack();var label=binding.Section.Labels.Field(type,field);
        var status=new Label{AutoSize=true,Text="特殊编码或不匹配的数据，请使用专用页面编辑。"};stack.Add(status);
        var content=new DetailsStack();stack.Add(content);
        BtlVector vector=null;
        bool ReadVector()
        {
            var node=DetailNode(binding,path);vector=node as BtlVector;
            bool compatible=(node==null || vector!=null) && (vector==null || string.IsNullOrEmpty(vector.Enc));
            stack.SetItemVisible(status,!compatible);stack.SetItemVisible(content,compatible);return compatible;
        }
        if(hint.VectorElem is not ("table" or "struct"))
        {
            var text=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Height=72,Dock=DockStyle.Top};content.Add(text);
            _fieldTips.SetToolTip(text,label.Tip+"\n留空为未写，[] 为空列表，多个值用逗号分隔。");
            var apply=DetailButton(binding,"应用列表",()=>ResearchEdits.ReplaceArray(binding.Document,parent(),type,field.Id,text.Text));content.Add(apply);
            return new DetailView(stack,()=>
            {
                if(!ReadVector())return;
                DetailName(text,"detail.array",path());DetailName(apply,"detail.apply",path());
                DetailText(text,vector==null ? "" : vector.V.Count==0 ? "[]" : string.Join(", ",vector.V.Select(ResearchEdits.Text)));
                apply.Enabled=DetailWritable(binding);
            });
        }
        var bar=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top};
        var list=new BufferedListView{View=View.Details,FullRowSelect=true,MultiSelect=false,VirtualMode=true,Height=132,Dock=DockStyle.Top};
        list.Columns.Add("条目",65);list.Columns.Add("内容",240);
        list.RetrieveVirtualItem+=(_,e)=>
        {
            if(vector==null || e.ItemIndex>=vector.V.Count) {e.Item=new ListViewItem("");return;}
            object item=vector.V[e.ItemIndex];string summary=item is BtlTable table ? string.Join(" / ",table.F.OrderBy(f=>f.Key).Take(3).Select(f=>f.Value is BtlScalar s ? ResearchEdits.Text(s.V) : "配置")) : "结构数据";
            e.Item=new ListViewItem(new[]{(e.ItemIndex+1).ToString(),summary});
        };
        var body=new DetailsStack();DetailView row=null;int selected=-1;bool changing=false;
        Func<string> rowPath=()=>selected<0 || path()==null ? null : path()+"["+selected+"]";
        void RefreshRow()
        {
            if(row==null && selected>=0)
            {
                row=DetailsObject(binding,rowPath,hint.VectorElemTable,new(),depth);body.Add(row.Control);
            }
            if(row!=null) {body.SetItemVisible(row.Control,selected>=0);if(selected>=0)row.Refresh();}
        }
        var add=DetailButton(binding,"添加条目",()=>_detailRows[path()]=ResearchEdits.Add(binding.Document,parent(),type,field.Id));bar.Controls.Add(add);
        var remove=DetailButton(binding,"删除条目",()=>
        {
            if(selected<0)throw new FrontEditException("请先选择条目。");
            ResearchEdits.Remove(binding.Document,path(),selected);_detailRows[path()]=Math.Max(0,selected-1);
        });bar.Controls.Add(remove);
        var clear=DetailButton(binding,"清除列表",()=>ResearchEdits.Clear(binding.Document,parent(),field.Id));bar.Controls.Add(clear);
        content.Add(bar);content.Add(list);content.Add(body);
        list.SelectedIndexChanged+=(_,_)=>
        {
            if(changing || _refreshingDetails || list.SelectedIndices.Count==0)return;
            using var update=BeginUiUpdate();selected=list.SelectedIndices[0];_detailRows[path()]=selected;RefreshRow();
        };
        return new DetailView(stack,()=>
        {
            if(!ReadVector())return;
            string current=path();DetailName(list,"detail.list",current);DetailName(add,"detail.add",current);
            DetailName(remove,"detail.remove",current);DetailName(clear,"detail.clear",current);
            int count=vector?.V.Count??0;selected=count==0 ? -1 : Math.Clamp(_detailRows.GetValueOrDefault(current),0,count-1);
            changing=true;list.BeginUpdate();
            try
            {
                if(list.VirtualListSize!=count)list.VirtualListSize=count;
                if(selected>=0 && (list.SelectedIndices.Count==0 || list.SelectedIndices[0]!=selected))
                {
                    list.SelectedIndices.Clear();list.Items[selected].Selected=true;
                }
                list.Invalidate();
            }
            finally {list.EndUpdate();changing=false;}
            add.Enabled=DetailWritable(binding);remove.Enabled=DetailWritable(binding) && selected>=0;clear.Enabled=DetailWritable(binding) && vector!=null;
            RefreshRow();
        });
    }
    static string OwnerType(BtlFrontDocument document,string path)
    {
        string type=document.Context.RootType;
        foreach(Match part in Regex.Matches(path,@"/(\d+)(\[\d+\])?"))
        {
            if(!document.Context.TryGet(type,int.Parse(part.Groups[1].Value),out var hint))throw new FrontEditException("配置路径解释不完整。");
            type=hint.Kind==FieldKind.Vector ? hint.VectorElemTable : hint.ChildTable??hint.StructName;
        }
        return type;
    }
}
