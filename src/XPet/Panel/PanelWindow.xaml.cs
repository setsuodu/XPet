using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XPet.Panel.Models;
using XPet.Panel.Services;

namespace XPet.Panel;

public partial class PanelWindow : Window
{
    List<ToolItem> _tools = new();
    string _currentCategory = "全部";
    string _search = "";
    readonly ToolRunner _runner = new();
    readonly DispatcherTimer _toastTimer = new();

    public PanelWindow()
    {
        InitializeComponent();

        _runner.Notify += (text, warn) =>
            Dispatcher.Invoke(() => ShowToast(text, warn));

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastText.Visibility = Visibility.Collapsed;
        };

        Loaded += (_, _) =>
        {
            ReloadTools();
        };
    }

    void ReloadTools()
    {
        _tools = ConfigStore.LoadTools();
        RebuildCategories();
        RebuildTools();
        StatusText.Text = $"共 {_tools.Count} 个工具";
    }

    void RebuildCategories()
    {
        CategoryList.Children.Clear();

        var cats = new List<string> { "全部" };
        cats.AddRange(_tools.Select(t => t.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c));

        foreach (var cat in cats)
        {
            var btn = new Button
            {
                Content = cat,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 4),
                Background = cat == _currentCategory ? new SolidColorBrush(Color.FromRgb(0xEB, 0xF8, 0xFF)) : Brushes.Transparent,
                Foreground = cat == _currentCategory ? (Brush)FindResource("PanelAccentBrush") : (Brush)FindResource("PanelTextPrimary"),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 13,
                FontWeight = cat == _currentCategory ? FontWeights.SemiBold : FontWeights.Normal,
            };
            btn.Click += (_, _) =>
            {
                _currentCategory = cat;
                RebuildCategories();
                RebuildTools();
            };
            CategoryList.Children.Add(btn);
        }
    }

    void RebuildTools()
    {
        ToolHost.Children.Clear();

        IEnumerable<ToolItem> q = _tools;
        if (_currentCategory != "全部")
            q = q.Where(t => t.Category == _currentCategory);
        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            q = q.Where(t =>
                (t.Label?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Category?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var groups = q.GroupBy(t => t.Category).OrderBy(g => g.Key);

        if (!groups.Any())
        {
            ToolHost.Children.Add(new TextBlock
            {
                Text = "没有匹配的工具",
                Foreground = (Brush)FindResource("PanelTextSecondary"),
                Margin = new Thickness(8),
                FontSize = 14,
            });
            return;
        }

        foreach (var g in groups)
        {
            ToolHost.Children.Add(new TextBlock
            {
                Text = g.Key,
                Style = (Style)FindResource("CategoryHeader"),
            });

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var tool in g)
            {
                wrap.Children.Add(CreateCard(tool));
            }
            ToolHost.Children.Add(wrap);
        }
    }

    Border CreateCard(ToolItem tool)
    {
        var card = new Border { Style = (Style)FindResource("ToolCard") };

        var sp = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = tool.DisplayIcon,
            FontSize = 20,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = tool.Label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("PanelTextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        sp.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(tool.Description))
        {
            sp.Children.Add(new TextBlock
            {
                Text = tool.Description,
                FontSize = 12,
                Foreground = (Brush)FindResource("PanelTextSecondary"),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        card.Child = sp;
        card.MouseLeftButtonUp += (_, _) => _runner.Run(tool);
        card.ToolTip = $"类型: {tool.Type}\n单击执行";
        return card;
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text ?? "";
        RebuildTools();
    }

    void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(ConfigStore.Dir) { UseShellExecute = true });
    }

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        ReloadTools();
        ShowToast("已重新载入 tools.json", false);
    }

    void ShowToast(string text, bool warn)
    {
        ToastText.Text = text;
        ToastText.Foreground = warn
            ? (Brush)FindResource("PanelDangerBrush")
            : (Brush)FindResource("PanelAccentBrush");
        ToastText.Visibility = Visibility.Visible;
        StatusText.Text = text;

        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(4);
        _toastTimer.Start();
    }
}
