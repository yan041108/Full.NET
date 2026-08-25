# Host.Api Native AOT Notifications 验证记录

- 日期：2026-08-25
- 验证时间：2026-08-25 12:48–13:00 UTC（20:48–21:00 Asia/Shanghai）
- 分支：`main`
- 代码提交：`c3732a6ca47a58cd7f1f9f1b67cf7b15a9bbac81`
- 关联计划：[`2026-08-25-notifications-native-aot-closure.md`](../superpowers/plans/2026-08-25-notifications-native-aot-closure.md)
- CI 证据：[`api-native-aot-linux` run 32849677783](https://github.com/yan041108/Full.NET/actions/runs/32849677783)
- 结论：`success`

## 范围

本记录只证明已发布的 linux-x64 Native AOT `Host.Api` 可执行文件，在 SQL Server 与 MySQL 上完成 Notifications 公告、站内信、源生成 HTTP JSON 与 SignalR JSON Hub 外部进程链路。不覆盖 Settings、Jobs、Worker/Migrator Native AOT、多节点 SignalR Backplane 或生产容量。

## 运行结果

| 检查项 | 结果 | 证据 |
|---|---|---|
| 工作流结论 | 通过 | run `32849677783` conclusion `success`，head SHA `c3732a6ca47a58cd7f1f9f1b67cf7b15a9bbac81` |
| `Verify Native AOT analyzers` | 通过 | 同 run step success |
| `Publish linux-x64 Native AOT artifact` | 通过 | 同 run step success |
| `Run Native AOT Notifications E2E` | 通过 | step success；`Test run summary: Passed! total: 2 failed: 0 succeeded: 2 skipped: 0` |
| SQL Server | 通过 | `SqlServer_native_artifact_supports_notifications_http_json_signalr` 已执行 |
| MySQL | 通过 | `MySql_native_artifact_supports_notifications_http_json_signalr` 已执行 |
| TRX | 已生成 | `artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-notifications.trx` |
| Publish manifest | 已上传 | artifact `api-native-aot-linux-manifest`，同一 run / 同一 SHA |

成功运行按工作流配置跳过失败日志 artifact 上传；原生进程日志仅在失败路径保留。本记录以步骤日志中的实际执行摘要（2/2 executed and passed）作为运行验证，不以 analyzer 或 publish 成功外推。

## 未验证边界

- Settings、Jobs 模块的 Native AOT 数据/HTTP 闭包
- Worker / Migrator Native AOT
- 多实例 SignalR Backplane 跨节点投递
- 生产容量与 1 万并发 SLO（`Capacity-not-verified`）
