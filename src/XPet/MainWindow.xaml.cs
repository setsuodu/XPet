using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using XPet.Panel;

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
    readonly VoicePlayer voice = new();

    // ===================== 精灵图相关 =====================
    BitmapImage? _imgNormal, _imgHappy, _imgWarn;
    BitmapImage[] _blinkFrames = Array.Empty<BitmapImage>();
    BitmapImage[] _toHappy = Array.Empty<BitmapImage>();
    BitmapImage[] _happyToNormal = Array.Empty<BitmapImage>();
    BitmapImage[] _toWarn = Array.Empty<BitmapImage>();
    BitmapImage[] _warnToNormal = Array.Empty<BitmapImage>();

    DispatcherTimer? _animTimer;
    int _animIndex;
    Action? _onAnimComplete;
    Face _currentFace = Face.Normal;

    public MainWindow()
    {
        InitializeComponent();

        runner = new TaskRunner((text, warn) => UI(() => Notify(text, warn)));

        ingest.Line += (text, isErr) => UI(() => HandleIngestLine(text, isErr));
        ingest.Exited += code => UI(() =>
        {
            UpdateIndicators();
            Notify($"入库程序退出了（退出码 {code}）", true);
            voice.Play("ingest_exit");
        });

        bubbleTimer.Tick += (_, _) => { bubbleTimer.Stop(); Bubble.Visibility = Visibility.Collapsed; };
        faceTimer.Tick += (_, _) =>
        {
            faceTimer.Stop();
            ReturnToNormal();
        };
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
        LoadSprites();          // 加载精灵图
        LoadAll();
        RestorePosition();
        CleanOldLogs();
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
        {
            ShowBubble("你好，右键打开菜单", false, 3000);
            voice.Play("hello");
        }
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

        // 恢复音量
        voice.Volume = s.VoiceVolume;
    }

    void SaveState()
    {
        Store.Save("state.json", new PetState
        {
            Left = Left,
            Top = Top,
            VoiceVolume = voice.Volume,
        });
    }

    void SavePosition() => SaveState();

    // ---------- 托盘 ----------

    void InitTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏宠物", null, (_, _) => ToggleVisible());
        menu.Items.Add("退出", null, (_, _) => Quit());

        // 单文件发布时旁边往往没有 Resources/xpet.ico，优先从 exe 自身 PE 图标提取
        System.Drawing.Icon? icon = null;
        try
        {
            var exe = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "XPet.exe");
            if (File.Exists(exe))
                icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);

            if (icon == null)
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "xpet.ico");
                if (File.Exists(icoPath))
                    icon = new System.Drawing.Icon(icoPath);
            }
        }
        catch { /* 失败就用系统默认 */ }

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon ?? System.Drawing.SystemIcons.Application,
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
            voice.Play("click");
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

        var openPanel = new MenuItem { Header = "打开工具中心" };
        openPanel.Click += (_, _) => OpenPanel();
        m.Items.Add(openPanel);

        var openCfg = new MenuItem { Header = "打开配置文件夹" };
        openCfg.Click += (_, _) => Process.Start(new ProcessStartInfo(Store.Dir) { UseShellExecute = true });
        m.Items.Add(openCfg);

        var openLog = new MenuItem { Header = "打开错误日志" };
        openLog.Click += (_, _) =>
        {
            var dir = Path.Combine(Store.Dir, "logs");
            var today = Path.Combine(dir, $"error-{DateTime.Now:yyyy-MM-dd}.log");
            if (File.Exists(today))
            {
                Process.Start(new ProcessStartInfo(today) { UseShellExecute = true });
                return;
            }
            if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "error-*.log").Any())
            {
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                return;
            }
            ShowBubble("还没有错误日志", false, 2500);
        };
        m.Items.Add(openLog);

        var reload = new MenuItem { Header = "重新载入配置" };
        reload.Click += (_, _) =>
        {
            LoadAll();
            ShowBubble("配置已重新载入（入库程序需要手动重启才生效）", false, 4000);
            voice.Play("config_reload");
        };
        m.Items.Add(reload);

        m.Items.Add(new Separator());

        // ---------- 设置 ----------
        var settings = new MenuItem { Header = "设置" };

        // 音量
        var volMenu = new MenuItem { Header = $"音量  {(int)(voice.Volume * 100)}%" };
        foreach (var pct in new[] { 0, 25, 50, 75, 100 })
        {
            var p = pct;
            var item = new MenuItem
            {
                Header = $"{p}%",
                IsChecked = (int)(voice.Volume * 100 + 0.5) == p,
            };
            item.Click += (_, _) =>
            {
                voice.Volume = p / 100.0;
                SaveState();
                ShowBubble($"音量已设为 {p}%", false, 2000);
                voice.Play("click");
            };
            volMenu.Items.Add(item);
        }
        settings.Items.Add(volMenu);

        // 版本
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        var verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "dev";
        settings.Items.Add(new MenuItem
        {
            Header = $"版本  v{verStr}",
            IsEnabled = false,
        });

        m.Items.Add(settings);

        m.Items.Add(new Separator());

        // ---------- 状态机调试菜单 ----------
        var debug = new MenuItem { Header = "状态机调试" };
        debug.Items.Add(MakeDebugItem("Normal（静止）", () => { faceTimer.Stop(); SetFace(Face.Normal); }));
        debug.Items.Add(MakeDebugItem("Happy（静止）", () => { faceTimer.Stop(); SetFace(Face.Happy); }));
        debug.Items.Add(MakeDebugItem("Warn（静止）", () => { faceTimer.Stop(); SetFace(Face.Warn); }));
        debug.Items.Add(new Separator());
        debug.Items.Add(MakeDebugItem("→ Happy 过渡", () => FaceFor(Face.Happy, 4000)));
        debug.Items.Add(MakeDebugItem("→ Warn 过渡", () => FaceFor(Face.Warn, 4000)));
        debug.Items.Add(MakeDebugItem("Happy → Normal", () => { _currentFace = Face.Happy; ReturnToNormal(); }));
        debug.Items.Add(MakeDebugItem("Warn → Normal", () => { _currentFace = Face.Warn; ReturnToNormal(); }));
        debug.Items.Add(new Separator());
        debug.Items.Add(MakeDebugItem("眨眼 Blink", () => Blink()));
        debug.Items.Add(MakeDebugItem("跳跃 Jump", () => Jump()));
        debug.Items.Add(new Separator());
        debug.Items.Add(MakeDebugItem("重新加载精灵图", () => { LoadSprites(); ShowBubble("精灵图已重新加载", false, 2000); }));
        debug.Items.Add(new Separator());
        var voiceMenu = new MenuItem { Header = "测试语音" };
        foreach (var name in new[] { "hello", "ingest_start", "ingest_ok", "ingest_skip", "ingest_warn", "ingest_error", "ingest_stop", "ingest_exit", "click", "config_reload" })
        {
            var n = name;
            voiceMenu.Items.Add(MakeDebugItem(n, () => voice.Play(n)));
        }
        debug.Items.Add(voiceMenu);
        m.Items.Add(debug);

        m.Items.Add(new Separator());

        var hide = new MenuItem { Header = "隐藏到托盘" };
        hide.Click += (_, _) => Hide();
        m.Items.Add(hide);

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Quit();
        m.Items.Add(exit);

        m.IsOpen = true;
    }


    PanelWindow? panelWindow;

    void OpenPanel()
    {
        if (panelWindow == null || !panelWindow.IsLoaded)
        {
            panelWindow = new PanelWindow();
            panelWindow.Closed += (_, _) => panelWindow = null;
            panelWindow.Show();
        }
        else
        {
            if (panelWindow.WindowState == WindowState.Minimized)
                panelWindow.WindowState = WindowState.Normal;
            panelWindow.Activate();
            panelWindow.Focus();
        }
    }

    MenuItem MakeDebugItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
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
        voice.Play("ingest_stop");
    }

    void HandleIngestLine(string text, bool isErr)
    {
        if (isErr)
        {
            LogError(text);
            Notify($"入库出错：{text}", true);   // 不再截断，完整显示
            voice.Play("ingest_error");
            return;
        }

        if (text.StartsWith("监视"))
        {
            ShowBubble("入库已启动，监视下载文件夹", false, 3000);
            voice.Play("ingest_start");
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
                voice.Play("ingest_warn");
            }
            else
            {
                var msg = rest.Contains("跳过")
                    ? $"{who} 之前已入库（md 未覆盖）"
                    : $"已入库 {who}  图{m.Groups[3].Value} 视频{m.Groups[4].Value}";
                ShowBubble(msg, false, 4500);
                FaceFor(Face.Happy, 3000);
                Jump();
                voice.Play(rest.Contains("跳过") ? "ingest_skip" : "ingest_ok");
            }
            RefreshPending();
            return;
        }

        // 问题说明行（"    - xxx"）或明确的失败信息
        if (text.TrimStart().StartsWith("- ") || text.Contains("出错") || text.Contains("下载失败"))
        {
            LogError(text);
            Notify(text.Trim(), true);          // 不再截断
            voice.Play("ingest_error");
        }
    }

    /// <summary>按天写入 %APPDATA%\XPet\logs\error-yyyy-MM-dd.log</summary>
    static void LogError(string text)
    {
        try
        {
            var dir = Path.Combine(Store.Dir, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"error-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}";
            File.AppendAllText(path, line, Encoding.UTF8);
        }
        catch
        {
            // 写日志失败就忽略，别把宠物搞崩
        }
    }

    /// <summary>启动时清理 7 天前的日志文件</summary>
    static void CleanOldLogs()
    {
        try
        {
            var dir = Path.Combine(Store.Dir, "logs");
            if (!Directory.Exists(dir)) return;
            var cutoff = DateTime.Now.Date.AddDays(-7);
            foreach (var f in Directory.EnumerateFiles(dir, "error-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(f); // error-2026-10-25
                if (name.Length < 16) continue;
                if (DateTime.TryParse(name.AsSpan(6), out var d) && d < cutoff)
                    File.Delete(f);
            }
        }
        catch { }
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

    // ===================== 精灵图核心逻辑 =====================

    void LoadSprites()
    {
        _imgNormal = LoadImg("normal.png");
        _imgHappy  = LoadImg("happy.png");
        _imgWarn   = LoadImg("warn.png");

        _blinkFrames   = LoadSeq("blink", 3);
        _toHappy       = LoadSeq("to_happy", 4);
        _happyToNormal = LoadSeq("happy_to_normal", 4);
        _toWarn        = LoadSeq("to_warn", 4);
        _warnToNormal  = LoadSeq("warn_to_normal", 4);

        PetImage.Source = _imgNormal;
        _currentFace = Face.Normal;
    }

    BitmapImage? LoadImg(string name)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Pet/{name}", UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            // 图片不存在时返回 null，运行时不会崩
            return null;
        }
    }

    BitmapImage[] LoadSeq(string prefix, int count)
    {
        var list = new List<BitmapImage>();
        for (int i = 1; i <= count; i++)
        {
            var img = LoadImg($"{prefix}_{i:D2}.png");
            if (img != null) list.Add(img);
        }
        return list.ToArray();
    }

    void PlayAnim(BitmapImage[] frames, int intervalMs = 80, Action? onComplete = null)
    {
        if (frames.Length == 0)
        {
            onComplete?.Invoke();
            return;
        }

        _animTimer?.Stop();
        _animIndex = 0;
        _onAnimComplete = onComplete;

        PetImage.Source = frames[0];

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _animTimer.Tick += (_, _) =>
        {
            _animIndex++;
            if (_animIndex >= frames.Length)
            {
                _animTimer.Stop();
                _onAnimComplete?.Invoke();
                return;
            }
            PetImage.Source = frames[_animIndex];
        };
        _animTimer.Start();
    }

    void SetFace(Face f)
    {
        _currentFace = f;
        PetImage.Source = f switch
        {
            Face.Happy => _imgHappy ?? _imgNormal,
            Face.Warn  => _imgWarn  ?? _imgNormal,
            _          => _imgNormal
        };
    }

    void FaceFor(Face f, int ms)
    {
        if (f == Face.Happy && _toHappy.Length > 0)
        {
            PlayAnim(_toHappy, 70, () =>
            {
                _currentFace = Face.Happy;
                PetImage.Source = _imgHappy ?? _imgNormal;
                faceTimer.Stop();
                faceTimer.Interval = TimeSpan.FromMilliseconds(ms);
                faceTimer.Start();
            });
        }
        else if (f == Face.Warn && _toWarn.Length > 0)
        {
            PlayAnim(_toWarn, 70, () =>
            {
                _currentFace = Face.Warn;
                PetImage.Source = _imgWarn ?? _imgNormal;
                faceTimer.Stop();
                faceTimer.Interval = TimeSpan.FromMilliseconds(ms);
                faceTimer.Start();
            });
        }
        else
        {
            SetFace(f);
            faceTimer.Stop();
            faceTimer.Interval = TimeSpan.FromMilliseconds(ms);
            faceTimer.Start();
        }
    }

    void ReturnToNormal()
    {
        if (_currentFace == Face.Happy && _happyToNormal.Length > 0)
        {
            PlayAnim(_happyToNormal, 70, () =>
            {
                _currentFace = Face.Normal;
                PetImage.Source = _imgNormal;
            });
        }
        else if (_currentFace == Face.Warn && _warnToNormal.Length > 0)
        {
            PlayAnim(_warnToNormal, 70, () =>
            {
                _currentFace = Face.Normal;
                PetImage.Source = _imgNormal;
            });
        }
        else
        {
            _currentFace = Face.Normal;
            PetImage.Source = _imgNormal;
        }
    }

    void Blink()
    {
        // 只在 Normal 状态下眨眼，避免和表情冲突
        if (_currentFace != Face.Normal || _blinkFrames.Length == 0) return;

        var backup = PetImage.Source;
        PlayAnim(_blinkFrames, 60, () =>
        {
            if (_currentFace == Face.Normal)
                PetImage.Source = backup;
        });
    }

    void Jump()
    {
        JumpShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -24, TimeSpan.FromMilliseconds(170))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
    }

    // ---------- 工具 ----------

    void UI(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.Invoke(a);
    }

    static string Short(string s) => s.Length <= 160 ? s : s[..157] + "...";
}
