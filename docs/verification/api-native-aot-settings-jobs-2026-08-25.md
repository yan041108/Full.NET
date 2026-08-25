# Host.Api Native AOT Settings / Jobs 验证记录

- 日期：2026-08-25
- 验证时间：2026-08-25 16:34–16:46 UTC（2026-08-26 00:34–00:46 Asia/Shanghai）
- 分支：`main`
- 代码提交：`7162c3297c17580544a61b7cc0cab0f5694847c4`
- 关联计划：[`2026-08-25-settings-jobs-native-aot-closure.md`](../superpowers/plans/2026-08-25-settings-jobs-native-aot-closure.md)
- CI 证据：[`api-native-aot-linux` run 32872774812](https://github.com/yan041108/Full.NET/actions/runs/32872774812)
- 结论：`success`

## 范围

本记录只证明已发布的 linux-x64 Native AOT `Host.Api` 可执行文件，在 SQL Server 与 MySQL 上完成 Settings（字典/配置/诊断/网格偏好）与 Jobs（定义/触发 ping/执行列表/计划/健康）HTTP JSON 外部进程链路。不覆盖 Jobs Worker 托管轮询、Worker/Migrator Native AOT、多节点 SignalR Backplane 或生产容量。

前置代码提交 `bc7727d6` 完成模块静态参数、物化器与 E2E/CI 接线；同日 `7162c329` 补齐 AOT 路径对 `IN @Ids` 集合参数的展开（对齐 Dapper 反射语义）。本记录以实际执行 Settings/Jobs 原生测试且全绿的提交 `7162c329` 为证据 SHA。

## 运行结果

| 检查项 | 结果 | 证据 |
|---|---|---|
| 工作流结论 | 通过 | run `32872774812` conclusion `success`，head SHA `7162c3297c17580544a61b7cc0cab0f5694847c4` |
| `Verify Native AOT analyzers` | 通过 | 同 run step success |
| `Publish linux-x64 Native AOT artifact` | 通过 | 同 run step success |
| `Run Native AOT Settings Jobs E2E` | 通过 | step success；`Test run summary: Passed! total: 4 failed: 0 succeeded: 4 skipped: 0` |
| Settings SQL Server | 通过 | `SqlServer_native_artifact_supports_settings_http_json` 已执行 |
| Settings MySQL | 通过 | `MySql_native_artifact_supports_settings_http_json` 已执行 |
| Jobs SQL Server | 通过 | `SqlServer_native_artifact_supports_jobs_http_json` 已执行 |
| Jobs MySQL | 通过 | `MySql_native_artifact_supports_jobs_http_json` 已执行 |
| TRX | 已生成 | `artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-settings-jobs.trx` |
| Publish manifest | 已上传 | artifact `api-native-aot-linux-manifest`，同一 run / 同一 SHA |

成功运行按工作流配置跳过失败日志 artifact 上传；原生进程日志仅在失败路径保留。本记录以步骤日志中的实际执行摘要（4/4 executed and passed）作为运行验证，不以 analyzer、Windows discovery 或 publish 成功外推。

## 未验证边界

- Jobs Worker 托管领取/轮询循环的独立 Native AOT（Host.Api 手动 trigger 已覆盖同进程 `JobExecutionRunner`）
- Worker / Migrator Native AOT
- 多实例 SignalR Backplane 跨节点投递
- 生产容量与 1 万并发 SLO（`Capacity-not-verified`）
