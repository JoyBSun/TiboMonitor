# 本机版架构说明

## 默认链路

```text
┌──────────────────────── Windows 本机 ────────────────────────┐
│ TiboMonitor.exe                                              │
│   -> 启动时立即检查                                          │
│   -> PeriodicTimer：每 1200 秒检查                           │
│   -> RemoteFeedClient（FeedMode=direct）                     │
│   -> 公开实时镜像（禁用系统代理）                            │
│   -> FlashFillingParser                                      │
│   -> RemoteFeed                                              │
│   -> DeduplicationService                                    │
│   -> UserData\Data\state.json（seen IDs + read 状态）       │
│   -> MainWindow（Topmost，无自动关闭 Timer）                 │
│   -> NotifyIcon / HKCU AutoStart                             │
└──────────────────────────────────────────────────────────────┘

```

默认链路不需要 GitHub、云服务器、X API credits、Token、浏览器 Cookie 或 X 登录状态。

## 数据源模式

### direct（默认）

```text
https://flash-filling.com/user/thsottiaux
```

客户端直接读取公开 HTML 中的 `tweet-card`，提取 Post ID、目标账号、时间和正文，转换为统一 `XPost`：

```text
Id, Text, CreatedAt, Url, Type
```

解析逻辑位于 Core，因此最终自包含 exe 可以独立工作。

### remote（可选备用）

将 `FeedMode` 改为 `remote` 并填写 `FeedUrl` 后，客户端读取标准 JSON Feed。该模式便于高级用户接入自己的可靠数据源。

## 20 分钟周期

配置：

```json
"FeedMode": "direct",
"LocalPollingIntervalSeconds": 1200
```

程序启动后先立即检查，然后通过 `PeriodicTimer` 等待 1200 秒。为避免免费入口被频繁请求，direct 模式在配置加载时强制最低 1200 秒，即使旧配置误写成 60 秒也会按 20 分钟运行。

托盘“立即检查”允许人工请求，但不应连续点击；HTTP 错误时程序记录日志并等待下一周期。

## 首次运行状态机

1. 加载 `程序目录\UserData\Data\state.json`。
2. 如果存在未读，立即重新显示窗口。
3. 读取当前实时镜像。
4. 如果本地状态尚未初始化，将当前内容保存为已读 baseline。
5. 以后只有不在 state 中的新 Post ID 才加入未读。

默认不会把历史内容全部弹出来。`NotifyRecentOnFirstRun=true` 仅供显式测试。

## 类型识别

- 正文以 `@` 开头：Reply。
- 卡片包含 `quoted-tweet`：Quote。
- 其他：Original。

公开镜像未稳定暴露纯 Repost 类型，因此默认不承诺 Repost 识别。

## 本地持久化

```text
程序目录\UserData\Data\state.json
```

每条记录包含：

```text
Id, Text, CreatedAt, FirstDetectedAt, Url, Type, Read
```

保存采用同目录临时文件后原子替换。JSON 损坏时先隔离原文件，不直接覆盖证据。已读历史可按 `MaxStatePosts` 清理；未读不会因为数量上限而删除。

## 窗口生命周期

- 新消息：`Show + Activate`，窗口保持 `Topmost=true`。
- 右上角 X：取消 Closing 并 Hide，不修改 Read。
- 我已读：按当前 Post ID 设置 `Read=true`，保存后切换下一条。
- 无未读：窗口隐藏，托盘和轮询继续运行。
- Windows 重启：先加载 state，有未读立即显示，再检查网络。

## 故障隔离

| 故障 | 行为 |
|---|---|
| 断网、DNS、超时 | 日志记录；程序继续常驻；下一周期重试 |
| 镜像 HTTP 错误 | 不修改状态；等待下一周期 |
| 镜像结构变化 | 拒绝本批数据，不产生错误提醒 |
| 账号不匹配 | 拒绝整批数据 |
| state.json 损坏 | 隔离损坏文件并进入安全空状态 |
| 电脑关机/休眠 | 本机停止检查；下次启动时立即检查近期内容 |

## 安全边界

- 不存储 X Token、账号密码或 Cookie。
- 不操作 X 网页、DOM、OCR 或鼠标。
- 不读取用户浏览器数据。
- Feed 内容只在程序目录的 `UserData` 中保存。
- “打开原文”才调用系统默认浏览器访问 x.com。

## 免费入口的边界

当前实时镜像是第三方免费来源，不是正式稳定 API。它可能限流、改版或只返回有限的近期内容。解析器使用明确字段验证和账号验证；无法可信解析时宁可失败并保留旧状态，也不生成可能错误的提醒。
