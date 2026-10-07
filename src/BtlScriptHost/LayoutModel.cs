namespace BtlCore.Scripting
{
    public sealed class LayoutBundle
    {
        public List<LayoutTab> Tabs { get; } = new List<LayoutTab>();
        public List<LayoutScript> Scripts { get; } = new List<LayoutScript>();
    }

    public sealed class LayoutScript
    {
        public string Name;
        public string Path;
        public string Text;
    }

    public sealed class LayoutTab
    {
        public string Id;
        public string Title;
        public string PagePath;
        public bool Builtin;
        public string Placement;
        public LayoutPage Page;
    }

    public sealed class LayoutPage
    {
        public string Id;
        public List<LayoutSection> Sections { get; } = new List<LayoutSection>();
    }

    public sealed class LayoutSection
    {
        public string Title;
        public string Kind;
        public string Bind;
        public string Resolve;
        public string Id;
        public string Text;
        public string Filter;
        public List<LayoutPalette> Palettes { get; } = new List<LayoutPalette>();
        public List<LayoutColumn> Columns { get; } = new List<LayoutColumn>();
        public List<LayoutField> Fields { get; } = new List<LayoutField>();
        public List<LayoutGroup> Groups { get; } = new List<LayoutGroup>();
        public List<LayoutRow> Rows { get; } = new List<LayoutRow>();
        public List<LayoutCommand> Commands { get; } = new List<LayoutCommand>();
    }

    public sealed class LayoutPalette
    {
        public string Title;
        public string Target;
    }

    public sealed class LayoutGroup
    {
        public string Title;
        public List<LayoutRow> Rows { get; } = new List<LayoutRow>();
    }

    public sealed class LayoutRow
    {
        public string Label;
        public List<LayoutField> Fields { get; } = new List<LayoutField>();
    }

    public sealed class LayoutColumn
    {
        public string Header;
        public string Bind;
    }

    public sealed class LayoutField
    {
        public string Id;
        public string Label;
        public string Widget;
        public string Type;
        public string Action;
        /// <summary>相对栏目向量的后缀，如 /0.6；含 {index} 或 {cell} 时是完整路径。</summary>
        public string Bind;
        public int? Shift;
        public int? Width;
        /// <summary>编辑器里显示的名称表：unit、general、ex、building、fort。</summary>
        public string Lookup;
        /// <summary>逗号分隔的勾选框 id。这些框都勾上时，本框才可编辑。</summary>
        public string When;
        public string Scope;
        public string Default;
        public decimal? Minimum;
        public decimal? Maximum;
        public bool ReadOnly;
        public bool Enabled = true;
        public string DisableReason;
    }

    public sealed class LayoutCommand
    {
        public string Id;
        public string Label;
        public string Script;
        public string Confirm;
        public string Handler;
        public bool Enabled = true;
        public string DisableReason;
    }

    public sealed class LayoutLoadResult
    {
        public bool Ok;
        public LayoutBundle Bundle;
        public List<string> Errors { get; } = new List<string>();
    }
}
