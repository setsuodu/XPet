using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace XPet;

/// <summary>执行 tasks.json 里的任务。type: chrome / open / cmd</summary>
public sealed class TaskRunner
{
    readonly Action<string, bool> notify; // (文本, 是否警告)，可能在非 UI 线程调用

    public TaskRunner(Action<string, bool> notify) { this.notify = notify; }

    public void Run(TaskItem t, AppConfig c, Vars v)
    {
        try
        {
            switch ((t.Type ?? "").Trim().ToLowerInvariant())
            {
                case "chrome": OpenChrome(t, c, v); break;
                case "open": OpenShell(t, v); break;
                case "cmd": RunCmd(t, v); break;
                default:
                    notify($"「{t.Label}」的类型 {t.Type} 不认识（可用 chrome / open / cmd）", true);
                    break;
            }
        }
        catch (UserFacingException e)
        {
            notify(e.Message, true);
        }
        catch (Exception e)
        {
            notify($"「{t.Label}」失败：{e.Message}", true);
        }
    }

    static void OpenChrome(TaskItem t, AppConfig c, Vars v)
    {
        var url = v.Expand(t.Url).Trim();
        if (url.Length == 0) throw new UserFacingException($"「{t.Label}」没有配置 url");
        if (url.Contains('"')) throw new UserFacingException($"「{t.Label}」的 url 里有引号，不能用");

        var args = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(c.ChromeProfile))
            args.Append("--profile-directory=\"").Append(c.ChromeProfile.Replace("\"", "")).Append("\" ");
        args.Append('"').Append(url).Append('"');

        try
        {
            // UseShellExecute=true 时，"chrome" 会通过注册表里的 App Paths 找到安装位置
            Process.Start(new ProcessStartInfo
            {
                FileName = c.Chrome,
                Arguments = args.ToString(),
                UseShellExecute = true,
            });
        }
        catch (Win32Exception e)
        {
            throw new UserFacingException($"打不开 Chrome：{e.Message}（可在 config.json 的 chrome 里填完整路径）");
        }
    }

    static void OpenShell(TaskItem t, Vars v)
    {
        var target = v.Expand(t.Target).Trim();
        if (target.Length == 0) throw new UserFacingException($"「{t.Label}」没有配置 target");
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    void RunCmd(TaskItem t, Vars v)
    {
        if (t.Args.Count == 0) throw new UserFacingException($"「{t.Label}」没有配置 args");

        // 每个参数单独展开、单独传递，不经过 shell，所以内容里的空格、引号、& 都不会被解释
        var args = t.Args.Select(a => v.Expand(a)).ToList();

        var psi = new ProcessStartInfo
        {
            FileName = args[0],
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        for (var i = 1; i < args.Count; i++) psi.ArgumentList.Add(args[i]);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        var last = "";
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data; };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data; };
        p.Exited += (_, _) =>
        {
            p.WaitForExit(); // 等输出读完，last 才是真正的最后一行
            var code = p.ExitCode;
            if (code == 0) notify($"「{t.Label}」完成", false);
            else notify($"「{t.Label}」失败（退出码 {code}）：{Short(last)}", true);
            p.Dispose();
        };

        try
        {
            p.Start();
        }
        catch (Win32Exception e)
        {
            throw new UserFacingException($"启动不了 {args[0]}：{e.Message}");
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        notify($"「{t.Label}」开始运行…", false);
    }

    static string Short(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
