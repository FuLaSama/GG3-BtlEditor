using System.Text.Json;
using BtlCore.Fb;

namespace BtlCore.Scripting;

/// <summary>界面名称和研究说明；物理类型仍取当前文档 schema。</summary>
public sealed class ResearchLabels
{
    public int Version { get; set; }
    public string Revision { get; set; }
    public Dictionary<string, ResearchTypeLabel> Types { get; set; } = new();
    public static ResearchLabels Load(string path)
    {
        var result=JsonSerializer.Deserialize<ResearchLabels>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(result?.Version!=1) throw new InvalidDataException(path+": 研究名称目录版本必须为 1");
        foreach(var type in result.Types.Values)
            foreach(var field in type.Fields.Values)
                if(string.IsNullOrWhiteSpace(field.Label) || field.Label.Length>18) throw new InvalidDataException(path+": 字段短名应为 1–18 字");
        return result;
    }
    public ResearchFieldLabel Field(string owner,FbsField field) => Types.TryGetValue(owner,out var type) && type.Fields.TryGetValue(field.Name,out var label)
        ? label : new ResearchFieldLabel{Label="字段 "+field.Id,Tip="当前解释："+field.Name};
}
public sealed class ResearchTypeLabel
{
    public string Label { get; set; }
    public Dictionary<string,ResearchFieldLabel> Fields { get; set; } = new();
}
public sealed class ResearchFieldLabel
{
    public string Label { get; set; }
    public string Tip { get; set; }
    public bool Uncertain { get; set; }
}
