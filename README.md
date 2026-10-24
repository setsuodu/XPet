# XPet

一个桌面宠物：常驻右下角，帮你启动 `x_ingest.py`、打开固定页面、执行自定义命令，并用气泡告诉你入库结果。

已合并 **XPet 工具中心（原 XPetPanel）**：右键菜单 →「打开工具中心」即可弹出工具面板。

## 构建和运行

需要 .NET 10 SDK（Windows）。

```bash
cd src/XPet
dotnet run
```

发布成单个 exe：

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

发布后的 exe 在 `bin\Release\net10.0-windows\win-x64\publish\`，目标电脑需要装 .NET 10 桌面运行时。

## 使用

* **左键拖动** 移动位置，位置会记住。**单击** 显示入库状态。
* **右键** 打开菜单：任务列表、启动/停止入库、**打开工具中心**、打开配置文件夹、重新载入配置、隐藏到托盘、退出。
* 头上的灯：绿色 = 入库程序运行中，灰色 = 已停止。
* 右上角红色数字 = 下载文件夹里等待处理的 x_*.json 数量。
* 脸的变化：开心 = 入库成功；叉叉眼 = 有错误或警告。
* 隐藏到托盘后，消息会改用托盘气泡提示。

## 配置

### 宠物配置（%APPDATA%\XPet\）

第一次运行会自动生成，右键菜单「打开配置文件夹」可以直达。

#### config.json

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| vault | D:\GitLab\obsidian-x | 笔记仓库 |
| inbox | 空 | 浏览器下载文件夹，空 = 用户目录下的 Downloads |
| python | python | python 可执行文件，不在 PATH 里就填完整路径，或者填 py |
| ingestScript | {vault}\_tools\x_ingest.py | 入库脚本位置 |
| chrome | chrome | Chrome 可执行文件，打不开就填完整路径 |
| chromeProfile | 空 | 有多个 Chrome 账号时填，例如 Profile 1 |
| autoStartIngest | true | 宠物启动时是否自动启动入库程序 |

另有 `tasks.json`（右键任务列表）、`state.json`（窗口位置）。

### 工具中心配置（%APPDATA%\XPetPanel\tools.json）

首次打开工具中心时会把示例 `Panel/tools.json` 复制过去。字段说明：

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

改完后点工具中心界面上的「重新载入」即可。

## 目录结构（合并后）

```
src/
├── XPet.slnx
└── XPet/
    ├── XPet.csproj          # 单一项目，已包含工具中心
    ├── MainWindow.*         # 桌面宠物
    ├── Panel/
    │   ├── PanelWindow.*    # 工具中心窗口
    │   ├── Models/ToolItem.cs
    │   ├── Services/
    │   └── tools.json       # 默认工具列表（会复制到输出目录）
    └── Resources/           # 精灵图、语音、图标
```
