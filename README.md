# Codex Quota Dashboard · Codex 额度看板

一个用 C# / WPF 编写的 Windows 桌面悬浮小窗，把 **Codex 剩余额度、下次重置时间、重置卡到期情况和 Tibo 重置雷达** 放在 Mini 附近，方便随时查看。

界面采用类似 iOS 电量组件的圆环设计，支持置顶、拖动、跟随 Mini，以及通过联动入口与桌面应用同启同退。Tibo 雷达内置独立浏览器采集器，由程序定时读取 X 公开页面并在本地分析；运行时不需要 Agent、API 密钥、Chrome 扩展或单独部署服务器。

> 展示的是当前登录账号的 Codex 额度。可用周期、额度分组和重置卡明细取决于账号接口返回的数据；ChatGPT 各模型的消息次数和聊天上下文余量不在查询范围内。

## 核心功能

| 功能 | 说明 |
| --- | --- |
| 圆环额度显示 | 圆心显示剩余百分比，支持接口返回的 5 小时、周额度及其他周期、额度分组 |
| 低额度颜色提示 | 正常显示绿色；剩余低于 35% 显示黄色，低于 15% 显示红色 |
| 周期重置倒计时 | 常驻显示下次重置日期、时间和剩余时长，统一换算为北京时间 |
| 重置卡查询 | 显示可用张数、最近到期时间；展开后查看卡片状态、到期时间、剩余时长和发放时间 |
| 临期提示 | 重置卡在 24 小时内到期时高亮；缺失或部分明细会明确标注 |
| Tibo 直连雷达 | 内置 WebView2 直接读取 `@thsottiaux` 的公开页面，保存原文、时间与原帖链接 |
| 本地重置研判 | 规则识别预告、完成声明、发卡、否定及弱线索；解析时间窗口并展示依据，未经校准不输出精确概率 |
| 自动与手动刷新 | 账号额度和重置卡每 60 秒刷新；X 页面每 5 分钟读取，也可随时手动查询 |
| Mini 跟随 | 随 Mini 移动和隐藏，拖动小窗可保存相对间距，也可关闭跟随独立摆放 |
| 同启同退 | 联动入口同时启动桌面应用和小窗；宿主主进程退出时，小窗及额度读取子进程一起结束 |
| 缓存与连接状态 | 连接失败时保留上次读数，并标注更新时间、缓存或过期状态 |

**重置卡功能只查询，不消耗卡片。** 个人周期重置时间来自账号读数；Tibo 公开发言与本地推断单独展示，不会覆盖个人周期时间。完成公告也不等于你的个人账号已恢复额度。

## 使用前准备

- Windows x64 桌面环境。
- .NET Framework 4.6.2 或更新版本，以及 Microsoft Edge WebView2 Runtime。程序启动时检查运行时；Windows 11 通常已包含，缺失时可从 [Microsoft 官方页面](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)安装。
- 本机已安装、运行并登录代码所适配的 ChatGPT/Codex Windows 桌面应用，同时存在可用的 Codex 命令行运行时。
- 程序目录可写，用于保存窗口设置、公开消息和 X 浏览器会话；直连雷达需要能访问 X，首次可能需要登录一次。

## 下载安装

从 [Releases](https://github.com/Zarye/codex-quota-dashboard/releases) 下载 `CodexQuotaDashboard-Setup.exe`，双击安装。安装器把程序放在当前用户的本地应用目录，创建“Codex 额度看板”开始菜单快捷方式，无需管理员权限。也提供便携 ZIP，解压后保留 EXE、配置与全部 DLL 在同一目录。

首次打开后，在内置的 Tibo 采集窗口完成 X 登录。程序使用独立浏览器会话，不读取 Chrome 的密码或 Cookie。关闭采集窗口会将它收起，采集继续由看板内部计时器完成；以后打开看板会复用该本机会话。X 登录失效、页面未就绪、网络失败或缓存过期时会提示检查采集页。

采集窗口显示时不自动刷新，避免打断登录或输入；收起后恢复每 5 分钟读取。X 页面布局、登录或访问限制可能影响采集，程序不会绕过验证码或访问限制。当前只收录页面实际渲染出来的 Tibo 消息，不承诺补全停机期间全部历史。

## 快速开始

### 1. 获取源码

```powershell
git clone https://github.com/Zarye/codex-quota-dashboard.git
cd codex-quota-dashboard
```

也可以在 GitHub 的 **Code → Download ZIP** 下载并解压，然后在该目录打开 PowerShell。

### 2. 编译程序

```powershell
powershell -NoProfile -File .\build.ps1
```

构建脚本首次从官方 NuGet 下载并校验固定版本的 WebView2 SDK，不需要单独安装 NuGet 命令行。成功后生成 `CodexQuotaMini.exe`、配置及三个 WebView2 DLL，运行时须放在一起。重新构建前，请先关闭已运行的小窗。

构建独立安装包和便携包：

```powershell
powershell -NoProfile -File .\build.ps1 -OutputDirectory .\bin\release -Package
```

输出位于 `dist/`，仅打包程序、依赖和说明，不包含本机数据。验证本地规则与真实 WebView2 DOM 采集逻辑：

```powershell
powershell -NoProfile -File .\test.ps1
```

测试使用本地构造页面，不请求 X，也不使用个人登录状态。

### 3. 联动启动

```powershell
.\CodexQuotaMini.exe --launch-chatgpt
```

该入口先打开桌面应用，再启动额度小窗。桌面应用已经运行时，也可以直接双击 EXE 打开小窗；未检测到宿主主进程时，直接双击会退出。

### 设置日常启动入口

创建一个指向 `CodexQuotaMini.exe` 的 Windows 快捷方式，并在目标路径后添加 `--launch-chatgpt` 参数。例如：

```text
"C:\Tools\CodexQuotaMini\CodexQuotaMini.exe" --launch-chatgpt
```

将路径替换为你的实际安装位置，并把快捷方式的“起始位置”设为程序所在目录。以后从该快捷方式打开，即可同时启动桌面应用与看板。

**同启依赖这个联动入口。** 从系统原有的桌面应用入口启动，不会自动带起看板。启动器完成启动后即退出，不会另外启动一个等待应用打开的常驻监控程序；关闭宿主主进程才会触发看板退出，隐藏 Mini 只会联动隐藏小窗。

## 小窗操作

| 操作 | 效果 |
| --- | --- |
| 鼠标移入小窗 | 显示左上角刷新按钮和右上角关闭按钮 |
| 点击 `↻` 或右键“立即刷新” | 查询账号额度、重置卡并刷新 X 采集页 |
| 拖动小窗空白处 | 调整位置；开启跟随时保存相对 Mini 的间距 |
| 点击“重置卡”区域 | 展开只读明细，点击区域外收起 |
| 点击 Tibo 预告 | 打开对应的 Tibo 原帖 |
| 点击“查看采集页” | 打开内置 X 窗口，处理首次登录、查看页面或手动刷新 |
| 右键切换“跟随 Mini” | 在联动跟随和独立摆放之间切换 |
| 点击 `×` 或右键“关闭” | 关闭小窗并结束其额度读取连接 |

周期重置、卡片到期和公开预告截止时间使用 **北京时间（UTC+8）**；PT 时钟使用美国太平洋当地时间并处理夏令时。公开预告里的 `tomorrow` 按发帖时的太平洋当地日期解释；原帖存在 PST/PDT 歧义时会提示两个可能的截止时刻。

## 常见问题

### 为什么双击后没有窗口？

程序需要检测到适配的桌面应用主进程。先运行并登录桌面应用，或使用 `--launch-chatgpt` 联动启动。若 Mini 当前隐藏且开启了“跟随 Mini”，恢复 Mini 后小窗也会恢复显示。

### 为什么提示找不到 Codex，或额度暂时无法读取？

程序会依次查找 `%LOCALAPPDATA%\OpenAI\Codex\bin` 下的运行时，以及 `%APPDATA%\npm\node_modules\@openai` 下的 `codex.exe`。确认运行时存在并已使用 ChatGPT 账号登录，然后手动刷新。连接失败后会保留上次读数并按刷新周期重试。

### 为什么没有周额度、5 小时额度或重置卡到期时间？

界面只展示接口实际返回的数据，不同账号或运行时版本返回的字段可能不同。“未提供”与“0 张”有不同含义；总张数多于已返回的明细时，会标注部分明细，不会推算缺失的到期时间。

### 本地预测是怎么实现的？

程序只读取 X 页面 DOM 中作者为 `@thsottiaux` 的帖子，保留原文、发帖时间和链接。`local-rules-v1` 识别重置预告、完成措辞、备用卡消息、否定表述和弱线索；再将 `within the next 2 hours`、`before 10am PST` 等时间表达换算为北京时间。

只有同页、此前一小时内存在重置预告时，才将独立的时间回复与其关联，并明确标为规则推定。单独的 `Done`、提问、愿望、无关发言不会当成完成公告。原帖 PST 与当地夏令时不一致时保留两个可能截止时刻。

当前版本展示依据与时间窗口，尚未积累完整采样、校准和回测数据，不提供虚构的今日/明日精确百分比，也不调用在线推理模型。个人额度是否恢复，始终以账号查询结果为准。

### Mini 更新后不再跟随怎么办？

跟随功能读取 `%USERPROFILE%\.codex\.codex-global-state.json` 中的 `electron-avatar-overlay-bounds` 和 `electron-avatar-overlay-open`。桌面应用更新可能改变这些记录；此时可以右键关闭跟随，手动放置小窗。

### 联动入口无法打开桌面应用怎么办？

默认启动标识为 `OpenAI.Codex_2p2nqsd0c76g0!App`。先用下面的命令查找实际 AppID：

```powershell
Get-StartApps | Where-Object { $_.Name -match 'ChatGPT|Codex' }
```

在程序目录创建 `data` 文件夹，并新建 UTF-8 编码的 `data/launch-settings.json`：

```json
{
  "appUserModelId": "填写 Get-StartApps 返回的实际 AppID"
}
```

该配置只调整启动目标；宿主进程识别和 Mini 位置读取仍依赖当前代码支持的安装方式。

## 数据来源与本地存储

账号数据通过本机 Codex app-server 的 `account/rateLimits/read` 查询，使用 `rateLimitsByLimitId`、`resetsAt` 和 `rateLimitResetCredits` 等返回字段。程序复用 Codex 自身的登录管理，不复制或保存登录密钥，也不创建聊天或调用推理模型。

Tibo 雷达只从内置浏览器加载的 [Tibo 公开页面](https://x.com/thsottiaux/with_replies) 获取消息，代码不再请求第三方雷达或预测网站。账号查询协议可参见 [Codex app-server 接口说明](https://learn.chatgpt.com/docs/app-server)。本程序不开启本地 HTTP 服务或外部服务器，也没有独立浏览器插件。

账号额度和重置卡数据不会发送给 X；X 自身仍按其网站机制处理浏览器登录与网页访问。缓存文件保存在程序目录：

| 文件 | 内容 |
| --- | --- |
| `data/settings.json` | 跟随开关、窗口位置和相对间距 |
| `data/launch-settings.json` | 用户自行配置的桌面应用启动标识 |
| `data/quota-cache.json` | 账号额度、重置卡展示字段和查询时间 |
| `data/tibo-direct-history.json` | 本机采集到的 Tibo 消息、原始链接和最近成功刷新时间 |
| `data/x-browser-profile/` | WebView2 独立浏览器会话，可能包含 X 登录状态，应视作个人数据保留 |
| `data/window-state.json` | 最近一次窗口位置与显示状态 |
| `小窗预览.png` | 程序生成的当前看板预览，可能包含个人额度读数 |

重置卡展示缓存不保存卡片兑换 ID。升级后不读取旧版第三方雷达缓存。`.gitignore` 已排除整个 `data/`、SDK 下载目录、编译程序、图标、快捷方式、预览图和临时文件；分享运行目录时，请排除个人缓存、X 浏览器会话与设置。小窗退出时会释放采集窗口和它的浏览器子进程。

## 源码结构

| 文件 | 用途 |
| --- | --- |
| [QuotaMini.cs](QuotaMini.cs) | WPF 主窗口、额度圆环、账号额度读取、位置跟随与进程清理 |
| [ResetCredits.cs](ResetCredits.cs) | 重置卡解析、临期提示和只读明细 |
| [ResetRadar.cs](ResetRadar.cs) | Tibo 直连雷达界面、状态与数据时效提示 |
| [LocalRadar.cs](LocalRadar.cs) | 本机消息历史、来源校验、规则分类与时区推断 |
| [XMessageCollector.cs](XMessageCollector.cs) | 内置 WebView2 采集器、登录窗口、定时刷新与进程释放 |
| [TiboCollector.js](TiboCollector.js) | 只读取 Tibo 公开帖子 DOM 的采集脚本 |
| [Companion.cs](Companion.cs) | 桌面应用识别、联动启动及旧启动项清理 |
| [build.ps1](build.ps1) | 使用本机 .NET Framework 编译程序 |
| [Installer.cs](Installer.cs) | 当前用户安装器与开始菜单入口 |
| [test.ps1](test.ps1) | 本地规则、缓存和 WebView2 DOM 测试入口 |
| [使用说明.txt](使用说明.txt) | 简明中文操作说明 |
