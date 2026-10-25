using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using XPet.Panel.Models;

namespace XPet.Panel.Services;

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

        // visible=true：WorkingDirectory=脚本目录，cmd /k 直接跑（不套 cd）
        if (tool.Visible)
        {
            var py = args.LastOrDefault(a => a.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
            var workDir = py != null ? (Path.GetDirectoryName(py) ?? "") : "";
            if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
                workDir = Environment.CurrentDirectory;

            var runArgs = args.ToList();
            if (py != null)
            {
                var i = runArgs.FindIndex(a => a.Equals(py, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) runArgs[i] = Path.GetFileName(py);
            }
            var cmdline = string.Join(" ", runArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

            var psiVis = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k " + cmdline,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = workDir,
            };
            psiVis.Environment["PYTHONIOENCODING"] = "utf-8";
            psiVis.Environment["PYTHONUTF8"] = "1";
            Process.Start(psiVis);
            Notify?.Invoke($"「{tool.Label}」已启动（窗口已打开）", false);
            return;
        }

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
