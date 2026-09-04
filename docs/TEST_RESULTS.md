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
Tests: 12/12 passed
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
| direct 模式强制最低 20 分钟 | PASS |
| 实时镜像解析原创与回复 | PASS |

## Release 包验证

```text
Release ZIP structure: PASS
Isolated Install.cmd execution: PASS
Required files missing: 0
Forbidden UserData/bin/obj files: 0
Installed application process affected during test: No
Local v1.0.0 SHA256: d1ce7e82f42a518418b59475a93c99faecf1ec4abb51dee698a60d9c35075c28
```

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
