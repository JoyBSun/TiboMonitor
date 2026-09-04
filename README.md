# TiboMonitor

TiboMonitor 是一个 Windows 常驻提醒工具，每 20 分钟检查一次 X 账号 [`@thsottiaux`](https://x.com/thsottiaux) 的公开动态。发现新内容后显示始终置顶的提醒窗口；只有用户点击“我已读”，该消息才会被标记为已读。

## 功能

- 启动时立即检查，之后每 20 分钟自动检查。
- 持久置顶窗口，不会几秒后自动消失。
- 多条未读、上一条/下一条和未读计数。
- 关闭按钮只隐藏窗口，不会标记已读。
- Post ID 去重，同一条动态不会重复提醒。
- 本地持久化，Windows 重启后恢复未读。
- 系统托盘和当前用户开机启动，无需管理员权限。
- 托盘“设置...”窗口可调整检查间隔、提醒类型、置顶和开机启动。
- 网络错误、超时、无效数据和状态文件损坏时安全恢复。
- 日志轮转，避免日志无限增大。
- .NET 8 自包含发布，普通用户无需安装 .NET。

## 下载和安装

1. 打开仓库右侧的 [Releases](https://github.com/JoyBSun/TiboMonitor/releases/latest)。
2. 下载 `TiboMonitor-win-x64-v1.1.0.zip`。
3. 解压 ZIP。
4. 双击 `Install.cmd`。
5. 安装完成后，程序会自动启动并出现在系统托盘。

默认安装位置：

```text
%LOCALAPPDATA%\Programs\TiboMonitor
```

安装脚本只写入当前用户目录和当前用户开机启动项，不请求管理员权限。升级时再次运行新版 `Install.cmd`，已有 `UserData` 不会被覆盖。

> 当前发布未进行商业代码签名。Windows SmartScreen 可能显示“未知发布者”；请只从本仓库 Releases 下载，并可使用同一 Release 中的 `.sha256` 文件校验完整性。

## 卸载

运行安装目录中的：

```text
Uninstall.cmd
```

卸载脚本会关闭已安装的 TiboMonitor、移除开机启动项并删除安装目录。

## 首次运行

第一次成功读取数据时，程序只建立当前动态的 baseline，默认不会把历史内容全部弹出。之后出现新的 Post ID 才会提醒。

本地状态和日志保存在：

```text
%LOCALAPPDATA%\Programs\TiboMonitor\UserData\
├─ Data\state.json
└─ Logs\tibo-monitor.log
```

## 托盘菜单

```text
打开
立即检查
查看未读
查看日志
设置...
开机启动
退出
```

右键托盘图标并选择“设置...”可以直接调整：

- 检查间隔（20～1440 分钟）；
- Reply、Quote、Repost 提醒开关；
- 提醒窗口是否始终置顶；
- Windows 登录后是否自动启动；
- 打开本地数据目录或日志目录。

保存后立即写入 `config.json` 并应用，检查间隔会从保存时刻重新计时。账号、数据源和 baseline 重置属于高级操作，不在普通设置窗口开放。

排障时也可以使用 `TiboMonitor.exe --settings` 启动并直接打开设置窗口。

## 配置

`config.json` 与 `TiboMonitor.exe` 位于同一目录。

| 字段 | 默认值 | 说明 |
|---|---:|---|
| `Account` | `thsottiaux` | 监控账号，不含 `@` |
| `FeedMode` | `direct` | `direct` 直接读取公开源；`remote` 读取自定义 JSON Feed |
| `FeedUrl` | 空 | 仅 `remote` 模式使用 |
| `MockFeedPath` | 空 | 非空时优先读取本地测试文件 |
| `LocalPollingIntervalSeconds` | `1200` | 检查周期；direct 模式最低 1200 秒 |
| `HttpTimeoutSeconds` | `20` | 单次请求超时 |
| `NotifyReplies` | `false` | 是否提醒回复 |
| `NotifyQuotes` | `true` | 是否提醒引用 |
| `NotifyReposts` | `false` | 是否提醒纯转发 |
| `TopMost` | `true` | 提醒窗口始终置顶 |
| `AutoStart` | `true` | 当前用户开机启动 |
| `NotifyRecentOnFirstRun` | `false` | 首次运行是否提醒近期历史内容 |

## 数据来源与限制

默认 direct 模式读取第三方公开镜像 `flash-filling.com`，不需要 X API Key、账号密码或浏览器 Cookie。HTTP 客户端禁用系统代理，避免残留代理端口导致请求失败。

第三方免费来源可能限流、停机、遗漏内容或修改 HTML 结构。解析器无法可信识别数据时会保留已有状态并等待下一周期，不生成猜测性提醒。本项目不保证严格实时或长期可用性。

## 隐私与安全

- 不保存 X 账号密码、Token 或浏览器 Cookie。
- 不读取浏览器数据。
- 不自动操作 X 页面。
- 状态和日志只保存在本地安装目录。
- `UserData`、日志、构建产物和可执行文件均不会提交到 Git。

## 从源码构建

要求：Windows 10/11、.NET 8 SDK。

```powershell
dotnet restore TiboMonitor.sln
dotnet build TiboMonitor.sln --configuration Release --no-restore
dotnet run --project tests\TiboMonitor.Tests\TiboMonitor.Tests.csproj --configuration Release --no-build
```

生成本地 Release 包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Version 1.1.0
```

输出：

```text
release\v1.1.0\TiboMonitor-win-x64-v1.1.0.zip
release\v1.1.0\TiboMonitor-win-x64-v1.1.0.zip.sha256
```

## 自动发布 GitHub Release

`.github/workflows/release.yml` 在推送 `v*` 标签时自动执行：

1. Release 构建；
2. 运行全部自动测试；
3. 发布 win-x64 自包含程序；
4. 生成一键安装 ZIP 和 SHA256；
5. 创建 GitHub Release 并上传资产。

维护者发布命令：

```powershell
git tag -a v1.1.0 -m "TiboMonitor v1.1.0"
git push origin main
git push origin v1.1.0
```

详细设计见 [架构说明](docs/ARCHITECTURE.md)，验证记录见 [测试结果](docs/TEST_RESULTS.md)。
