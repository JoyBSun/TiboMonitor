# 测试结果

验证日期：2026-09-04
环境：Windows 10 x64、.NET SDK 8.0.204

## 自动化测试

```powershell
dotnet build TiboMonitor.sln --configuration Release --no-restore
dotnet run --project tests\TiboMonitor.Tests\TiboMonitor.Tests.csproj --configuration Release --no-build
```

结果：

```text
Release build: 0 warnings, 0 errors
Tests: 13/13 passed
```

| 测试 | 结果 |
|---|---|
| 首次运行只建立 baseline | PASS |
| 新 Post 进入未读 | PASS |
| 重复 Post ID 不重复提醒 | PASS |
| 三条消息全部保留 | PASS |
| 关闭再打开后未读仍存在 | PASS |
| 损坏 state.json 被隔离且程序可继续 | PASS |
| 异常 Feed JSON 被拒绝且可重试 | PASS |
| 我已读正确保存 | PASS |
| 网络失败返回可捕获异常 | PASS |
| direct 模式生成标准 Feed | PASS |
| 检查间隔限制为 5～1440 分钟 | PASS |
| 实时镜像解析原创与回复 | PASS |
| 设置保存后可重新加载 | PASS |

## Release 包验证

```text
Release ZIP structure: PASS
Isolated Install.cmd execution: PASS
Required files missing: 0
Forbidden UserData/bin/obj files: 0
Installed application process affected during test: No
Local v1.1.1 SHA256: 3bfe3ca5bb4b85abbed86aa8f7c25d99962b7d7082f8dcc5b96b683de25f9e72
```

## 设置窗口桌面验证

在隔离的临时数据目录中通过 Windows UI Automation 启动并操作真实设置窗口：

```text
Window title: Tibo Monitor 设置
Initial window size: 560 x 680
Minimum window size: 480 x 420
Maximum window size: 720 x 760
Cancel and Save visible at minimum size: Yes
Changed interval: 5 minutes
Saved interval seconds: 300
Settings window closed after Save: Yes
Settings UI test: PASS
```

验证结束后已恢复正式安装目录中的程序，未改动正式 `config.json` 和 `UserData`。

本地 `build-release.ps1` 执行以下步骤：

1. Restore、Release Build 和全部自动测试。
2. 生成 win-x64 自包含应用。
3. 加入 `Install.cmd`、`Uninstall.cmd` 和安装说明。
4. 生成 Release ZIP 和 SHA256 校验文件。
5. 检查 ZIP 中不存在源码构建缓存或用户运行数据。

## 仍需用户环境确认

- Windows SmartScreen 对未签名应用的提示。
- 不同 DPI、多显示器和全屏应用下的 Topmost 行为。
- 目标网络能否直接访问默认公开数据源。
- Windows 重启后的开机启动和未读恢复。
- 第三方数据源临时故障后的下一周期恢复。
