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
        public List<LayoutColumn> Columns { get; } = new List<LayoutColumn>();
        public List<LayoutField> Fields { get; } = new List<LayoutField>();
        public List<LayoutCommand> Commands { get; } = new List<LayoutCommand>();
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
        public bool Enabled = true;
        public string DisableReason;
    }

    public sealed class LayoutCommand
    {
        public string Id;
        public string Label;
        public string Script;
        public string Confirm;
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
