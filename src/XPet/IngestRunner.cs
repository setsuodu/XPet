using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace XPet;

/// <summary>在后台启动 x_ingest.py，并把它的输出一行一行抛出来</summary>
public sealed class IngestRunner
{
    Process? proc;

    /// <summary>(文本, 是否来自 stderr)。在线程池线程上触发，UI 要自己切线程。</summary>
    public event Action<string, bool>? Line;

    /// <summary>入库程序自己退出时触发（主动 Stop 不会触发）</summary>
    public event Action<int>? Exited;

    public bool IsRunning
    {
        get
        {
            try { return proc != null && !proc.HasExited; }
            catch { return false; }
        }
    }

    public void Start(AppConfig c, Vars v)
    {
        if (IsRunning) return;

        var script = v.IngestScript;
        if (!File.Exists(script))
            throw new UserFacingException($"找不到入库脚本：{script}");

        var psi = new ProcessStartInfo
        {
            FileName = c.Python,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(script) ?? Environment.CurrentDirectory,
        };
        // -u：不缓冲，否则 python 在管道里会攒满缓冲区才输出，宠物收不到实时消息
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--vault");
        psi.ArgumentList.Add(v.Vault);
        psi.ArgumentList.Add("--inbox");
        psi.ArgumentList.Add(v.Inbox);
        // 中文 Windows 上管道默认用 GBK 输出，强制 UTF-8
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Line?.Invoke(e.Data, false); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Line?.Invoke(e.Data, true); };
        p.Exited += (_, _) =>
        {
            int code;
            try { code = p.ExitCode; } catch { code = -1; }
            if (ReferenceEquals(proc, p)) Exited?.Invoke(code);
        };

        try
        {
            p.Start();
        }
        catch (Win32Exception e)
        {
            throw new UserFacingException($"启动不了 {c.Python}：{e.Message}");
        }

        proc = p;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
    }

    public void Stop()
    {
        var p = proc;
        proc = null; // 先置空，Exited 里就不会把这次当成意外退出
        try
        {
            if (p != null && !p.HasExited) p.Kill(true);
        }
        catch
        {
            // 已经退出了
        }
    }
}
