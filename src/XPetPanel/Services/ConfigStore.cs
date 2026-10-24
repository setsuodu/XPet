using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using XPetPanel.Models;

namespace XPetPanel.Services;

public static class ConfigStore
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XPetPanel");

    static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string PathOf(string name) => Path.Combine(Dir, name);

    public static List<ToolItem> LoadTools()
    {
        Directory.CreateDirectory(Dir);
        var path = PathOf("tools.json");

        // 优先读 AppData，没有就尝试读程序旁边的示例
        if (!File.Exists(path))
        {
            var local = Path.Combine(AppContext.BaseDirectory, "tools.json");
            if (File.Exists(local))
            {
                File.Copy(local, path, overwrite: false);
            }
            else
            {
                var defaults = DefaultTools();
                SaveTools(defaults);
                return defaults;
            }
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ToolItem>>(json, Opt) ?? DefaultTools();
        }
        catch
        {
            return DefaultTools();
        }
    }

    public static void SaveTools(List<ToolItem> tools)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathOf("tools.json"), JsonSerializer.Serialize(tools, Opt));
    }

    public static List<ToolItem> DefaultTools() => new()
    {
        new ToolItem
        {
            Id = "open-downloads",
            Label = "打开下载文件夹",
            Description = "用资源管理器打开 Downloads",
            Category = "文件工具",
            Icon = "📁",
            Type = "open",
            Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        },
        new ToolItem
        {
            Id = "open-appdata",
            Label = "打开配置目录",
            Description = "打开 XPetPanel 的配置文件夹",
            Category = "系统维护",
            Icon = "⚙️",
            Type = "open",
            Target = Dir,
        },
        new ToolItem
        {
            Id = "notepad",
            Label = "记事本",
            Description = "启动系统记事本",
            Category = "系统维护",
            Icon = "📝",
            Type = "cmd",
            Args = new() { "notepad.exe" },
        },
        new ToolItem
        {
            Id = "calc",
            Label = "计算器",
            Description = "启动系统计算器",
            Category = "系统维护",
            Icon = "🔢",
            Type = "cmd",
            Args = new() { "calc.exe" },
        },
        new ToolItem
        {
            Id = "artcraft-updater",
            Label = "ArtCraft 更新器",
            Description = "启动 craft_updater.py（需自行改路径）",
            Category = "创意工具",
            Icon = "🎨",
            Type = "cmd",
            Args = new() { "python", @"D:\GitHub\[Workspace]\my-scripts\artcraft\craft_updater.py" },
        },
        new ToolItem
        {
            Id = "clear-run",
            Label = "清除运行历史",
            Description = "清除 Win+R 运行对话框历史（示例，需自行改路径）",
            Category = "系统维护",
            Icon = "🧹",
            Type = "cmd",
            Args = new() { "cmd.exe", "/c", @"D:\GitHub\[Workspace]\my-scripts\windows\clear-run-history.bat" },
        },
    };
}
