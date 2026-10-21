# XPet

一个桌面宠物：常驻右下角，帮你启动 `x_ingest.py`、打开固定页面、执行自定义命令，并用气泡告诉你入库结果。

## 构建和运行

需要 .NET 10 SDK（Windows）。

```
dotnet run
```

发布成单个 exe：

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

发布后的 exe 在 `bin\Release\net10.0-windows\win-x64\publish\`，目标电脑需要装 .NET 10 桌面运行时。

## 使用

- **左键拖动**移动位置，位置会记住。**单击**显示入库状态。
- **右键**打开菜单：任务列表、启动/停止入库、打开配置文件夹、重新载入配置、隐藏到托盘、退出。
- 头上的灯：绿色 = 入库程序运行中，灰色 = 已停止。
- 右上角红色数字 = 下载文件夹里等待处理的 `x_*.json` 数量。
- 脸的变化：开心 = 入库成功；叉叉眼 = 有错误或警告。
- 隐藏到托盘后，消息会改用托盘气泡提示。

## 配置

所有配置都在 `%APPDATA%\XPet\`，第一次运行会自动生成，右键菜单「打开配置文件夹」可以直达。

### config.json

| 字段 | 默认值 | 说明 |
|---|---|---|
| `vault` | `D:\GitLab\obsidian-x` | 笔记仓库 |
| `inbox` | 空 | 浏览器下载文件夹，空 = 用户目录下的 Downloads |
| `python` | `python` | python 可执行文件，不在 PATH 里就填完整路径，或者填 `py` |
| `ingestScript` | `{vault}\_tools\x_ingest.py` | 入库脚本位置 |
| `chrome` | `chrome` | Chrome 可执行文件，打不开就填完整路径 |
| `chromeProfile` | 空 | 有多个 Chrome 账号时填，例如 `Profile 1` |
| `autoStartIngest` | `true` | 宠物启动时是否自动启动入库程序 |

JSON 里的反斜杠要写成 `\\`，或者直接用 `/`。

### tasks.json

每个任务一项，菜单按这个顺序生成。

```json
[
  { "label": "打开 Chrome 下载页", "type": "chrome", "url": "chrome://downloads/" },
  { "label": "打开笔记仓库",       "type": "open",   "target": "{vault}" },
  { "label": "下载剪贴板里的链接",  "type": "cmd",
    "args": ["yt-dlp", "-o", "{vault}\\_dl\\%(id)s.%(ext)s", "{clipboard}"] }
]
```

| type | 字段 | 说明 |
|---|---|---|
| `chrome` | `url` | 用 Chrome 打开网址（含 `chrome://`） |
| `open` | `target` | 用系统默认方式打开文件夹、文件或网址 |
| `cmd` | `args` | 后台运行命令，不弹黑窗口。`args[0]` 是程序，其余是参数。结束后气泡显示成功或失败 |

可用变量：`{vault}`、`{inbox}`、`{ingestScript}`、`{python}`、`{appdata}`、`{clipboard}`。

`cmd` 不经过 shell，每个参数单独传递，所以内容里有空格、引号、`&` 都不会被当成命令执行。`{clipboard}` 只接受 http(s) 链接，不是链接会直接报错，防止剪贴板里的 `--exec ...` 之类内容被当成选项。

## 已知限制

- 强行结束宠物进程（任务管理器杀掉）时，后台的 python 可能留下来，需要手动结束。正常用右键菜单「退出」不会有这个问题。
- 同时只能运行一个宠物，第二次启动会直接退出。
- 出错日志在 `%APPDATA%\XPet\error.log`。
- 「打开 X 点赞页」默认的网址 `https://x.com/i/history/likes` 我没核实过，打不开就在 `tasks.json` 里改成 `https://x.com/你的用户名/likes`。
