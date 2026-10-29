using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        // 空也强制用 Default，避免复用当前激活的 Chrome 实例
        var profile = string.IsNullOrWhiteSpace(c.ChromeProfile) ? "Default" : c.ChromeProfile.Replace("\"", "");
        args.Append("--profile-directory=\"").Append(profile).Append("\" ");
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

        // visible=true：WorkingDirectory=脚本目录，cmd /k 只跑命令（不再套 cd，避免引号炸）
        if (t.Visible)
        {
            try
            {
                var py = args.LastOrDefault(a => a.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
                var workDir = py != null ? (Path.GetDirectoryName(py) ?? "") : "";
                if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
                    workDir = Environment.CurrentDirectory;

                // 在脚本目录下跑：python 用相对文件名，避免 Program Files 空格引号问题
                var runArgs = args.ToList();
                if (py != null)
                {
                    var idx = runArgs.FindIndex(a => a.Equals(py, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) runArgs[idx] = Path.GetFileName(py);
                }
                var cmdline = string.Join(" ", runArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

                var psiVis = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k " + cmdline,  // 用 Arguments 整串，避免 ArgumentList 二次加引号
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workDir,
                };
                psiVis.Environment["PYTHONIOENCODING"] = "utf-8";
                psiVis.Environment["PYTHONUTF8"] = "1";

                try
                {
                    var logDir = Path.Combine(Store.Dir, "logs");
                    Directory.CreateDirectory(logDir);
                    var logPath = Path.Combine(logDir, $"error-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss}] visible 启动「{t.Label}」 cwd={workDir} cmd={cmdline}{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch { }

                Process.Start(psiVis);
                notify($"「{t.Label}」已启动（窗口已打开）", false);
            }
            catch (Win32Exception e)
            {
                throw new UserFacingException($"启动不了：{e.Message}");
            }
            return;
        }

        // 默认：后台静默，等退出后提示成功/失败
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
            if (code == 0)
            {
                notify($"「{t.Label}」完成", false);
            }
            else
            {
                // 写完整错误到按天日志
                try
                {
                    var dir = Path.Combine(Store.Dir, "logs");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, $"error-{DateTime.Now:yyyy-MM-dd}.log");
                    var line = $"[{DateTime.Now:HH:mm:ss}] 「{t.Label}」失败 (exit {code}): {last}{Environment.NewLine}";
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { }

                notify($"「{t.Label}」失败（退出码 {code}）：{Short(last)}", true);
            }
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

    static string Short(string s) => s.Length <= 120 ? s : s[..117] + "…";
}
