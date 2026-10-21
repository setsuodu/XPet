using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XPet;

/// <summary>config.json</summary>
public sealed class AppConfig
{
    public string Vault { get; set; } = @"D:\GitLab\obsidian-x";
    /// <summary>浏览器下载文件夹；留空 = 当前用户的 Downloads</summary>
    public string Inbox { get; set; } = "";
    public string Python { get; set; } = "python";
    public string IngestScript { get; set; } = @"{vault}\_tools\x_ingest.py";
    /// <summary>chrome 的可执行文件名或完整路径</summary>
    public string Chrome { get; set; } = "chrome";
    /// <summary>多个 Chrome 账号时填，例如 "Profile 1"；留空 = 默认账号</summary>
    public string ChromeProfile { get; set; } = "";
    public bool AutoStartIngest { get; set; } = true;
}

/// <summary>tasks.json 里的一项。type: chrome / open / cmd</summary>
public sealed class TaskItem
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string Target { get; set; } = "";
    public List<string> Args { get; set; } = new();
}

/// <summary>state.json：窗口位置</summary>
public sealed class PetState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
}

/// <summary>可以直接显示给用户看的错误</summary>
public sealed class UserFacingException : Exception
{
    public UserFacingException(string message) : base(message) { }
}

public static class Defaults
{
    public static List<TaskItem> Tasks() => new()
    {
        new TaskItem { Label = "打开 Chrome 下载页", Type = "chrome", Url = "chrome://downloads/" },
        new TaskItem { Label = "打开 X 点赞页", Type = "chrome", Url = "https://x.com/i/history/likes" },
        new TaskItem { Label = "打开笔记仓库", Type = "open", Target = "{vault}" },
        new TaskItem
        {
            Label = "补全入库（--rebuild）",
            Type = "cmd",
            Args = new() { "{python}", "{ingestScript}", "--rebuild", "--vault", "{vault}" },
        },
        new TaskItem
        {
            Label = "yt-dlp 下载剪贴板里的链接",
            Type = "cmd",
            Args = new() { "yt-dlp", "-o", @"{vault}\_dl\%(id)s.%(ext)s", "{clipboard}" },
        },
    };
}

/// <summary>%APPDATA%\XPet 下的读写</summary>
public static class Store
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XPet");

    static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip, // 允许在 json 里写 // 注释
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义成 \uXXXX
    };

    public static string PathOf(string name) => Path.Combine(Dir, name);

    /// <summary>
    /// 文件不存在就写入默认值；解析失败时返回默认值并给出 error，不会覆盖用户的文件。
    /// </summary>
    public static T Load<T>(string name, Func<T> makeDefault, out string? error) where T : class
    {
        error = null;
        var path = PathOf(name);
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(path))
            {
                var d = makeDefault();
                Save(name, d);
                return d;
            }
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opt) ?? makeDefault();
        }
        catch (Exception e)
        {
            error = $"{name} 读取失败：{e.Message}";
            return makeDefault();
        }
    }

    public static void Save<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathOf(name), JsonSerializer.Serialize(value, Opt));
        }
        catch
        {
            // 写不进去不影响使用
        }
    }
}

/// <summary>把 {vault} {inbox} {ingestScript} {python} {appdata} {clipboard} 替换成实际值</summary>
public sealed class Vars
{
    static readonly Regex Token = new(@"\{(\w+)\}", RegexOptions.Compiled);
    readonly AppConfig c;

    public Vars(AppConfig c) { this.c = c; }

    public string Vault => c.Vault;

    public string Inbox => string.IsNullOrWhiteSpace(c.Inbox)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        : c.Inbox.Replace("{vault}", Vault);

    public string IngestScript => c.IngestScript.Replace("{vault}", Vault);

    public string Expand(string s) => Token.Replace(s, m => m.Groups[1].Value switch
    {
        "vault" => Vault,
        "inbox" => Inbox,
        "ingestScript" => IngestScript,
        "python" => c.Python,
        "appdata" => Store.Dir,
        "clipboard" => ClipboardUrl(),
        _ => m.Value,
    });

    /// <summary>
    /// 剪贴板内容会作为命令行参数传给外部程序（比如 yt-dlp）。
    /// 只允许 http(s) 链接，避免剪贴板里是 "--exec ..." 这类选项被当成参数执行。
    /// </summary>
    static string ClipboardUrl()
    {
        string? text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { throw new UserFacingException("读取剪贴板失败，稍后再试"); }

        if (Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            return u.AbsoluteUri;
        }
        throw new UserFacingException("剪贴板里不是 http(s) 链接");
    }
}
