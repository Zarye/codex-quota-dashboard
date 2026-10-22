# Codex Quota Mini

Windows 桌面额度看板：在 Mini 附近显示 Codex 剩余额度、周期重置时间、账户重置卡和 Tibo 公开重置信号。

## 功能

- 圆环显示剩余百分比，常驻显示周期重置日期与倒计时。
- 查询账号重置卡张数；点击卡片区域查看到期时间、剩余时长和发放时间，24 小时内到期高亮。
- 每 60 秒刷新账号额度与重置卡，鼠标移入后可点击 `↻` 手动刷新，也支持右键立即刷新。
- 每 5 分钟同步 Tibo 公开预告与第三方重置预测，显示 PT 当地时间，处理夏令时与预告时区歧义。
- 支持跟随 Mini、拖动调整间距及隐藏联动。
- 从联动入口启动时，与 ChatGPT/Codex 桌面应用一起启动；桌面应用主进程退出后，结束小窗及额度读取子进程。

账号余额、重置卡和公开预测分别展示。重置卡功能仅查询，不使用卡片；第三方概率不代表 Tibo 或 OpenAI 的承诺。

## 构建

适用于 Windows x64，需要系统自带的 Windows PowerShell 和 .NET Framework 4.x WPF 编译工具。无需安装 NuGet 依赖。

在本目录执行：

```powershell
powershell -NoProfile -File .\build.ps1
```

生成 `CodexQuotaMini.exe`。重新构建前请关闭已运行的额度小窗，释放 EXE 文件占用。

## 启动

电脑上需要已安装并登录的 ChatGPT/Codex 桌面应用。程序会查找本机 Codex 命令行运行时，复用其已有登录管理。

联动启动桌面应用与看板：

```powershell
.\CodexQuotaMini.exe --launch-chatgpt
```

桌面应用已经运行时，可直接双击 `CodexQuotaMini.exe` 打开看板；桌面应用未运行时，直接双击会退出。

可创建一个指向 `CodexQuotaMini.exe`、参数为 `--launch-chatgpt` 的快捷方式，作为日常启动入口。仓库不包含本机快捷方式。

默认启动标识为 `OpenAI.Codex_2p2nqsd0c76g0!App`。如果安装的桌面应用标识不同，可用 `Get-StartApps` 查找实际 AppID，并在本地 `data/launch-settings.json` 中配置：

```json
{
  "appUserModelId": "填写 Get-StartApps 返回的实际 AppID"
}
```

Mini 跟随依赖桌面应用保存的 `electron-avatar-overlay-bounds` 和 `electron-avatar-overlay-open` 位置记录；若应用更新后记录格式变化，可右键关闭跟随并手动放置小窗。

## 操作

- 拖动小窗：调整相对 Mini 的位置。
- `↻`：立即查询额度、重置卡并刷新雷达。
- 点击“重置卡”：展开只读明细，点击区域外收起。
- 点击 Tibo 预告、概率或来源署名：打开对应原帖或来源网站。
- 右键：立即刷新、切换跟随、关闭小窗。

到期时间和重置倒计时使用北京时间；PT 时钟使用美国太平洋时区并自动处理夏令时。接口未提供、连接失败及过期缓存均有单独提示。

## 文件

| 文件 | 用途 |
| --- | --- |
| `QuotaMini.cs` | WPF 主窗口、额度圆环、账号额度读取、位置跟随与进程清理 |
| `ResetCredits.cs` | 重置卡查询结果解析、临期提示和只读明细 |
| `ResetRadar.cs` | Tibo 公开信号、第三方预测、时区换算与数据时效检查 |
| `Companion.cs` | 桌面应用识别、联动启动及旧启动项清理 |
| `build.ps1` | 使用本机 .NET Framework 编译程序 |
| `使用说明.txt` | 本机使用说明 |

## 数据与来源

账号数据通过本机 app-server 的 `account/rateLimits/read` 读取，使用 `rateLimitsByLimitId`、`resetsAt` 和 `rateLimitResetCredits` 等返回字段。不复制登录密钥，不创建聊天或调用推理模型。

- [Codex app-server 接口说明](https://learn.chatgpt.com/docs/app-server)
- [Tibo 公开信号副本：codex-reset.com](https://codex-reset.com/zh/radar)
- [第三方预测：Codex Reset News](https://www.codexreset-news.com/?lang=zh-CN)
- [公开预测接口说明](https://www.codexreset-news.com/developers?lang=zh-CN)

运行时在 `data/` 保存窗口位置、账号额度及公开雷达缓存。`.gitignore` 已排除整个目录，以及本机编译程序、图标、快捷方式、预览图和临时文件。重置卡展示缓存不保存卡片兑换 ID。
