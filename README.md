# XPetPanel — 可扩展工具中心 MVP

轻量 WPF 主窗体，用来聚合 my-scripts / XPet 的各种小工具。  
后续可以和桌面宠物合并，或作为宠物双击后弹出的面板。

## 目录结构

```
XPetPanel/
├── README.md
└── src/
    └── XPetPanel/
        ├── App.xaml / App.xaml.cs
        ├── MainWindow.xaml / MainWindow.xaml.cs
        ├── Models/
        │   └── ToolItem.cs
        ├── Services/
        │   ├── ConfigStore.cs
        │   └── ToolRunner.cs
        ├── tools.json
        └── XPetPanel.csproj
```

与原版 XPet（`src/XPet/`）保持一致。

## 功能

- 左侧分类导航 + 右侧卡片列表
- 顶部搜索（名称 / 描述 / 分类）
- 配置驱动：`%APPDATA%\XPetPanel\tools.json`
- 支持三种执行类型：
  - `open`：用系统默认方式打开文件/文件夹/网址
  - `cmd`：后台运行命令（无黑窗），结束后提示成功/失败
  - `chrome`：用 Chrome 打开网址
- 单击卡片即可执行

## 运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（Windows）。

```bash
cd src/XPetPanel
dotnet run
```

发布单文件：

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

产物在 `bin\Release\net8.0-windows\win-x64\publish\`。

## 配置

首次运行会把示例 `tools.json` 复制到：

```
%APPDATA%\XPetPanel\tools.json
```

直接编辑这个文件即可增删工具。字段说明：

| 字段 | 说明 |
|------|------|
| id | 唯一标识 |
| label | 显示名称 |
| description | 卡片副标题 |
| category | 分类（左侧导航用） |
| icon | emoji 或短文字 |
| type | `cmd` / `open` / `chrome` |
| target | open / chrome 用的路径或网址 |
| args | cmd 用的参数列表，args[0] 是程序 |
| needsAdmin | 预留 |
| isFavorite | 预留 |

改完后点界面上的「重新载入」即可，无需重启。
