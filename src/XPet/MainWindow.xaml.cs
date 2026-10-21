using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace XPet;

public partial class MainWindow : Window
{
    enum Face { Normal, Happy, Warn }

    // x_ingest.py 的输出行：[贴文ID] 路径  图N 视频N  写入 md / md 已存在，跳过  [有问题 N 处]
    static readonly Regex OkLine = new(@"^\[(\d+)\]\s+(\S+)\s+图(\d+)\s+视频(\d+)\s*(.*)$");
    static readonly Regex NameRe = new(@"^\d{4}-\d{2}-\d{2}_(.+)_\d+$");

    static readonly Brush OkBrush = Frozen(Color.FromArgb(245, 255, 255, 255));
    static readonly Brush WarnBrush = Frozen(Color.FromRgb(255, 227, 227));
    static readonly Brush LedOn = Frozen(Color.FromRgb(0x2E, 0xCC, 0x71));
    static readonly Brush LedOff = Frozen(Color.FromRgb(0x9A, 0xA5, 0xA0));

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    AppConfig cfg = new();
    List<TaskItem> tasks = new();
    Vars vars = new(new AppConfig());
    readonly IngestRunner ingest = new();
    readonly TaskRunner runner;

    readonly DispatcherTimer bubbleTimer = new();
    readonly DispatcherTimer faceTimer = new();
    readonly DispatcherTimer blinkTimer = new();
    readonly DispatcherTimer pendingTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly Random rng = new();

    System.Windows.Forms.NotifyIcon? tray;
    int pending;

    public MainWindow()
    {
        InitializeComponent();

        runner = new TaskRunner((text, warn) => UI(() => Notify(text, warn)));

        ingest.Line += (text, isErr) => UI(() => HandleIngestLine(text, isErr));
        ingest.Exited += code => UI(() =>
        {
            UpdateIndicators();
            Notify($"入库程序退出了（退出码 {code}）", true);
        });

        bubbleTimer.Tick += (_, _) => { bubbleTimer.Stop(); Bubble.Visibility = Visibility.Collapsed; };
        faceTimer.Tick += (_, _) => { faceTimer.Stop(); SetFace(Face.Normal); };
        blinkTimer.Tick += (_, _) =>
        {
            Blink();
            blinkTimer.Interval = TimeSpan.FromMilliseconds(rng.Next(2500, 5500));
        };
        pendingTimer.Tick += (_, _) => RefreshPending();

        Pet.MouseLeftButtonDown += Pet_MouseLeftButtonDown;
        Pet.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        Bubble.MouseLeftButtonUp += (_, _) => Bubble.Visibility = Visibility.Collapsed;

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            ingest.Stop();
            tray?.Dispose();
        };
    }

    // ---------- 启动 ----------

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadAll();
        RestorePosition();
        InitTray();

        BobShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -4, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });

        blinkTimer.Interval = TimeSpan.FromMilliseconds(3000);
        blinkTimer.Start();
        pendingTimer.Start();

        if (cfg.AutoStartIngest) StartIngest();
        RefreshPending();

        if (Bubble.Visibility != Visibility.Visible)
            ShowBubble("你好，右键打开菜单", false, 3000);
    }

    void LoadAll()
    {
        cfg = Store.Load("config.json", () => new AppConfig(), out var e1);
        tasks = Store.Load("tasks.json", () => Defaults.Tasks(), out var e2);
        vars = new Vars(cfg);

        var err = e1 ?? e2;
        if (err != null) ShowBubble(err + "（已改用默认值）", true, 7000);
    }

    void RestorePosition()
    {
        var s = Store.Load("state.json", () => new PetState(), out _);
        var left = s.Left ?? SystemParameters.WorkArea.Right - Width - 20;
        var top = s.Top ?? SystemParameters.WorkArea.Bottom - Height - 20;

        // 换过显示器配置时，防止窗口跑到屏幕外面找不到
        var minX = SystemParameters.VirtualScreenLeft;
        var minY = SystemParameters.VirtualScreenTop;
        var maxX = minX + SystemParameters.VirtualScreenWidth - Width;
        var maxY = minY + SystemParameters.VirtualScreenHeight - Height;
        Left = Math.Max(minX, Math.Min(left, maxX));
        Top = Math.Max(minY, Math.Min(top, maxY));
    }

    void SavePosition() => Store.Save("state.json", new PetState { Left = Left, Top = Top });

    // ---------- 托盘 ----------

    void InitTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏宠物", null, (_, _) => ToggleVisible());
        menu.Items.Add("退出", null, (_, _) => Quit());

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "XPet",
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.DoubleClick += (_, _) => ToggleVisible();
    }

    void ToggleVisible()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    void Quit()
    {
        ingest.Stop();
        tray?.Dispose();
        tray = null;
        Application.Current.Shutdown();
    }

    // ---------- 交互 ----------

    void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var l0 = Left;
        var t0 = Top;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }

        if (Math.Abs(Left - l0) < 3 && Math.Abs(Top - t0) < 3)
        {
            // 没怎么动 = 单击
            RefreshPending();
            ShowBubble($"入库：{(ingest.IsRunning ? "运行中" : "已停止")}，待处理 {pending}", false, 3000);
        }
        else
        {
            SavePosition();
        }
    }

    void ShowMenu()
    {
        RefreshPending();
        var m = new ContextMenu { PlacementTarget = Pet, Placement = PlacementMode.MousePoint };

        m.Items.Add(new MenuItem
        {
            Header = $"入库：{(ingest.IsRunning ? "运行中" : "已停止")}　待处理 {pending}",
            IsEnabled = false,
        });
        m.Items.Add(new Separator());

        var any = false;
        foreach (var t in tasks.Where(x => !string.IsNullOrWhiteSpace(x.Label)))
        {
            var task = t;
            var item = new MenuItem { Header = task.Label };
            item.Click += (_, _) => runner.Run(task, cfg, vars);
            m.Items.Add(item);
            any = true;
        }
        if (any) m.Items.Add(new Separator());

        var toggle = new MenuItem { Header = ingest.IsRunning ? "停止入库" : "启动入库" };
        toggle.Click += (_, _) => { if (ingest.IsRunning) StopIngest(); else StartIngest(); };
        m.Items.Add(toggle);

        var openCfg = new MenuItem { Header = "打开配置文件夹" };
        openCfg.Click += (_, _) => Process.Start(new ProcessStartInfo(Store.Dir) { UseShellExecute = true });
        m.Items.Add(openCfg);

        var reload = new MenuItem { Header = "重新载入配置" };
        reload.Click += (_, _) =>
        {
            LoadAll();
            ShowBubble("配置已重新载入（入库程序需要手动重启才生效）", false, 4000);
        };
        m.Items.Add(reload);

        m.Items.Add(new Separator());

        var hide = new MenuItem { Header = "隐藏到托盘" };
        hide.Click += (_, _) => Hide();
        m.Items.Add(hide);

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Quit();
        m.Items.Add(exit);

        m.IsOpen = true;
    }

    // ---------- 入库程序 ----------

    void StartIngest()
    {
        try
        {
            ingest.Start(cfg, vars);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
        UpdateIndicators();
    }

    void StopIngest()
    {
        ingest.Stop();
        UpdateIndicators();
        ShowBubble("入库已停止", false, 2500);
    }

    void HandleIngestLine(string text, bool isErr)
    {
        if (isErr)
        {
            Notify($"入库出错：{Short(text)}", true);
            return;
        }

        if (text.StartsWith("监视"))
        {
            ShowBubble("入库已启动，监视下载文件夹", false, 3000);
            return;
        }

        var m = OkLine.Match(text);
        if (m.Success)
        {
            var name = Path.GetFileNameWithoutExtension(m.Groups[2].Value);
            var nm = NameRe.Match(name);
            var who = nm.Success ? "@" + nm.Groups[1].Value : name;
            var rest = m.Groups[5].Value;

            if (rest.Contains("有问题"))
            {
                Notify($"已入库 {who}，但有媒体问题", true);
            }
            else
            {
                var msg = rest.Contains("跳过")
                    ? $"{who} 之前已入库（md 未覆盖）"
                    : $"已入库 {who}  图{m.Groups[3].Value} 视频{m.Groups[4].Value}";
                ShowBubble(msg, false, 4500);
                FaceFor(Face.Happy, 3000);
                Jump();
            }
            RefreshPending();
            return;
        }

        // 问题说明行（"    - xxx"）或明确的失败信息
        if (text.TrimStart().StartsWith("- ") || text.Contains("出错") || text.Contains("下载失败"))
            Notify(Short(text.Trim()), true);
    }

    // ---------- 显示 ----------

    void Notify(string text, bool warn)
    {
        ShowBubble(text, warn);
        if (warn) FaceFor(Face.Warn, 4000);
    }

    void ShowBubble(string text, bool warn = false, int ms = 4500)
    {
        BubbleText.Text = text;
        Bubble.Background = warn ? WarnBrush : OkBrush;
        Bubble.Visibility = Visibility.Visible;
        bubbleTimer.Stop();
        bubbleTimer.Interval = TimeSpan.FromMilliseconds(ms);
        bubbleTimer.Start();

        // 宠物被隐藏到托盘时，改用托盘气泡提示
        if (!IsVisible && tray != null)
        {
            tray.ShowBalloonTip(3000, "XPet", text,
                warn ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    void RefreshPending()
    {
        pending = CountPending();
        Badge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = pending > 99 ? "99+" : pending.ToString();
        UpdateIndicators();
    }

    int CountPending()
    {
        try
        {
            var dir = vars.Inbox;
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "x_*.json").Count() : 0;
        }
        catch
        {
            return 0;
        }
    }

    void UpdateIndicators()
    {
        Led.Fill = ingest.IsRunning ? LedOn : LedOff;
        if (tray != null)
            tray.Text = $"XPet　入库{(ingest.IsRunning ? "运行中" : "已停止")}　待处理 {pending}";
    }

    void SetFace(Face f)
    {
        EyesNormal.Visibility = f == Face.Normal ? Visibility.Visible : Visibility.Collapsed;
        EyesHappy.Visibility = f == Face.Happy ? Visibility.Visible : Visibility.Collapsed;
        EyesWarn.Visibility = f == Face.Warn ? Visibility.Visible : Visibility.Collapsed;
        MouthSmile.Visibility = f == Face.Normal ? Visibility.Visible : Visibility.Collapsed;
        MouthBig.Visibility = f == Face.Happy ? Visibility.Visible : Visibility.Collapsed;
        MouthWavy.Visibility = f == Face.Warn ? Visibility.Visible : Visibility.Collapsed;
    }

    void FaceFor(Face f, int ms)
    {
        SetFace(f);
        faceTimer.Stop();
        faceTimer.Interval = TimeSpan.FromMilliseconds(ms);
        faceTimer.Start();
    }

    void Blink()
    {
        EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(90)) { AutoReverse = true });
    }

    void Jump()
    {
        JumpShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -24, TimeSpan.FromMilliseconds(170))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
    }

    void UI(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.BeginInvoke(a);
    }

    static string Short(string s) => s.Length <= 60 ? s : s[..60] + "…";

    // ---------- 不出现在 Alt+Tab 里 ----------

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x80;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
    }
}
