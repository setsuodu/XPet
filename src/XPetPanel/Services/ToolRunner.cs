using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using XPetPanel.Models;

namespace XPetPanel.Services;

public sealed class ToolRunner
{
    public event Action<string, bool>? Notify; // text, isWarning

    public void Run(ToolItem tool)
    {
        try
        {
            switch ((tool.Type ?? "").Trim().ToLowerInvariant())
            {
                case "open":
                    OpenShell(tool);
                    Notify?.Invoke($"已打开：{tool.Label}", false);
                    break;
                case "chrome":
                    OpenChrome(tool);
                    Notify?.Invoke($"已用 Chrome 打开：{tool.Label}", false);
                    break;
                case "cmd":
                    RunCmd(tool);
                    break;
                default:
                    Notify?.Invoke($"未知类型：{tool.Type}", true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"{tool.Label} 失败：{ex.Message}", true);
        }
    }

    static void OpenShell(ToolItem tool)
    {
        var target = Environment.ExpandEnvironmentVariables((tool.Target ?? "").Trim());
        if (target.Length == 0) throw new InvalidOperationException("没有配置 target");
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    static void OpenChrome(ToolItem tool)
    {
        var url = (tool.Target ?? "").Trim();
        if (url.Length == 0) throw new InvalidOperationException("没有配置 target/url");

        Process.Start(new ProcessStartInfo
        {
            FileName = "chrome",
            Arguments = $"\"{url}\"",
            UseShellExecute = true,
        });
    }

    void RunCmd(ToolItem tool)
    {
        if (tool.Args == null || tool.Args.Count == 0)
            throw new InvalidOperationException("没有配置 args");

        var args = tool.Args.ToList();
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
        for (var i = 1; i < args.Count; i++)
            psi.ArgumentList.Add(args[i]);

        var last = "";
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data; };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data; };
        p.Exited += (_, _) =>
        {
            p.WaitForExit();
            var code = p.ExitCode;
            if (code == 0)
                Notify?.Invoke($"「{tool.Label}」完成", false);
            else
                Notify?.Invoke($"「{tool.Label}」失败（{code}）：{Short(last)}", true);
            p.Dispose();
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        Notify?.Invoke($"「{tool.Label}」开始运行…", false);
    }

    static string Short(string s) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= 80 ? s : s[..80] + "…");
}
