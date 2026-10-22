# Codex Quota Mini · Codex 桌面额度看板

一个用 C# / WPF 编写的 Windows 桌面悬浮小窗，把 **Codex 剩余额度、下次重置时间、重置卡到期情况和 Tibo 重置雷达** 放在 Mini 附近，方便随时查看。

界面采用类似 iOS 电量组件的圆环设计，支持置顶、拖动、跟随 Mini，以及通过联动入口与桌面应用同启同退。

> 展示的是当前登录账号的 Codex 额度。可用周期、额度分组和重置卡明细取决于账号接口返回的数据；ChatGPT 各模型的消息次数和聊天上下文余量不在查询范围内。

## 核心功能

| 功能 | 说明 |
| --- | --- |
| 圆环额度显示 | 圆心显示剩余百分比，支持接口返回的 5 小时、周额度及其他周期、额度分组 |
| 低额度颜色提示 | 正常显示绿色；剩余低于 35% 显示黄色，低于 15% 显示红色 |
| 周期重置倒计时 | 常驻显示下次重置日期、时间和剩余时长，统一换算为北京时间 |
| 重置卡查询 | 显示可用张数、最近到期时间；展开后查看卡片状态、到期时间、剩余时长和发放时间 |
| 临期提示 | 重置卡在 24 小时内到期时高亮；缺失或部分明细会明确标注 |
| Tibo 重置雷达 | 同步公开重置预告，支持点击查看原帖，并显示自动处理夏令时的 PT 时钟 |
| 第三方重置预测 | 展示来源提供的今日、明日预测概率，并检查更新时间和来源有效性 |
| 自动与手动刷新 | 账号额度和重置卡每 60 秒刷新；公开雷达每 5 分钟同步，也可随时手动查询 |
| Mini 跟随 | 随 Mini 移动和隐藏，拖动小窗可保存相对间距，也可关闭跟随独立摆放 |
| 同启同退 | 联动入口同时启动桌面应用和小窗；宿主主进程退出时，小窗及额度读取子进程一起结束 |
| 缓存与连接状态 | 连接失败时保留上次读数，并标注更新时间、缓存或过期状态 |

**重置卡功能只查询，不消耗卡片。** 个人周期重置时间来自账号读数；Tibo 公开预告和第三方预测单独展示，不会覆盖个人周期时间。第三方概率不是 Tibo 本人的预测或 OpenAI 的到账承诺。

## 使用前准备

- Windows x64 桌面环境。
- Windows PowerShell 和包含 WPF 的 .NET Framework 4.6 或更新版本；构建使用系统的 Framework64 编译工具。
- 本机已安装、运行并登录代码所适配的 ChatGPT/Codex Windows 桌面应用，同时存在可用的 Codex 命令行运行时。
- 程序目录可写，用于保存窗口设置和查询缓存；公开雷达需要访问其来源网站。

项目使用系统程序集，无需安装 NuGet 依赖。仓库提供源码和构建脚本，`CodexQuotaMini.exe` 由本机编译生成。

## 快速开始

### 1. 获取源码

```powershell
git clone https://github.com/Zarye/codex-quota-mini.git
cd codex-quota-mini
```

也可以在 GitHub 的 **Code → Download ZIP** 下载并解压，然后在该目录打开 PowerShell。

### 2. 编译程序

```powershell
powershell -NoProfile -File .\build.ps1
```

成功后在当前目录生成 `CodexQuotaMini.exe`。重新构建前，请先关闭已运行的额度小窗，释放 EXE 文件占用。

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
| 点击 `↻` 或右键“立即刷新” | 同时查询账号额度、重置卡与公开雷达 |
| 拖动小窗空白处 | 调整位置；开启跟随时保存相对 Mini 的间距 |
| 点击“重置卡”区域 | 展开只读明细，点击区域外收起 |
| 点击 Tibo 预告 | 打开对应的 Tibo 原帖 |
| 点击预测概率或来源署名 | 打开对应的公开来源网站 |
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

### Tibo 的预告或预测概率能保证我的账号恢复吗？

个人额度是否恢复，以账号查询结果为准。Tibo 动态通过公开网站副本获取；今日、明日概率来自第三方预测网站。过期信号、超出时限的预告、预测待更新和连接失败均有状态提示。没有接入微信小程序或要求 X、微信登录。

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

公开雷达使用以下来源：

| 内容 | 来源 |
| --- | --- |
| 账号查询协议 | [Codex app-server 接口说明](https://learn.chatgpt.com/docs/app-server) |
| Tibo 公开信号副本 | [codex-reset.com](https://codex-reset.com/zh/radar)，请求 `/api/feed` |
| 今日、明日预测 | [Codex Reset News](https://www.codexreset-news.com/?lang=zh-CN)，请求 `/api/v1/overview` |
| 第三方接口说明 | [Codex Reset News 开发者页面](https://www.codexreset-news.com/developers?lang=zh-CN) |

账号查询走本机 app-server；公开雷达请求中不附带账号额度或重置卡数据。缓存文件保存在程序目录：

| 文件 | 内容 |
| --- | --- |
| `data/settings.json` | 跟随开关、窗口位置和相对间距 |
| `data/launch-settings.json` | 用户自行配置的桌面应用启动标识 |
| `data/quota-cache.json` | 账号额度、重置卡展示字段和查询时间 |
| `data/reset-radar-cache.json` | 公开信号、第三方预测、来源链接和时间 |
| `data/window-state.json` | 最近一次窗口位置与显示状态 |
| `小窗预览.png` | 程序生成的当前看板预览，可能包含个人额度读数 |

重置卡展示缓存不保存卡片兑换 ID。`.gitignore` 已排除整个 `data/`、编译程序、图标、快捷方式、预览图和临时文件；分享运行目录时，请排除个人缓存与设置。

## 源码结构

| 文件 | 用途 |
| --- | --- |
| [QuotaMini.cs](QuotaMini.cs) | WPF 主窗口、额度圆环、账号额度读取、位置跟随与进程清理 |
| [ResetCredits.cs](ResetCredits.cs) | 重置卡解析、临期提示和只读明细 |
| [ResetRadar.cs](ResetRadar.cs) | Tibo 公开信号、第三方预测、时区换算与数据时效检查 |
| [Companion.cs](Companion.cs) | 桌面应用识别、联动启动及旧启动项清理 |
| [build.ps1](build.ps1) | 使用本机 .NET Framework 编译程序 |
| [使用说明.txt](使用说明.txt) | 简明中文操作说明 |
