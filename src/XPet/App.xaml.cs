using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace XPet;

public partial class App : Application
{
    // 保持引用，进程存活期间一直占着，防止被回收后单实例失效
    static Mutex? mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例：重复启动会同时开两个入库程序，所以第二个直接退出
        mutex = new Mutex(true, @"Local\XPet.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);
        new MainWindow().Show();
    }

    static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Store.Dir);
            File.AppendAllText(Store.PathOf("error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // 日志写不进去就算了
        }
    }
}
