using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XPet.Panel.Models;

/// <summary>
/// 单个可执行工具。type: cmd / open / chrome
/// </summary>
public sealed class ToolItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "其他";
    public string? Icon { get; set; }          // emoji 或短文字
    public string Type { get; set; } = "cmd";  // cmd | open | chrome
    public string? Target { get; set; }        // open / chrome 用
    public List<string> Args { get; set; } = new(); // cmd 用
    public bool Visible { get; set; } = false; // true=显示控制台
    public bool NeedsAdmin { get; set; }
    public bool IsFavorite { get; set; }

    [JsonIgnore]
    public string DisplayIcon => string.IsNullOrWhiteSpace(Icon) ? "🔧" : Icon;
}
