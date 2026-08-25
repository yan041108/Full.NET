# Host.Api Native AOT Linux Publish 验证记录

- 日期：2026-08-25（Linux CI fresh 复验）
- 分支：`main`
- 基线提交：`f3ea5f51c76275968f0525b4b5c57c0a865eed6b`
- 任务快照：`api-native-aot-phase2-ci-20260824`
- 关联计划：[`2026-08-23-api-native-aot-phase2.md`](../superpowers/plans/2026-08-23-api-native-aot-phase2.md)
- CI 证据：[`api-native-aot-linux` run 32821397581](https://github.com/yan041108/Full.NET/actions/runs/32821397581)

## 状态摘要

> 2026-08-25 的 fresh `ubuntu-latest` 运行已完成 publish、架构门禁、双库 HTTP、双库 SignalR JSON、S3 与 Kafka Replay 原生进程验证；本记录据此关闭 Phase 2 `Aot-published` 门槛。

| 检查项 | 结果 | 证据 |
|---|---|---|
| `pnpm test:aot:analyzers` | 通过 | CI `Verify Native AOT analyzers` success；本地 AOT analysis build 0 warning / 0 error |
| `pnpm test:aot:publish:linux` | 通过 | CI `Publish linux-x64 Native AOT artifact` success；manifest 上传成功；本地同提交产物 71,926,064 bytes |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 通过 | CI architecture gate success；提交前聚焦复验 15/15 |
| Linux 原生可执行文件 | 通过 | `artifacts/native-aot/linux-x64/publish/Full.NET.Host.Api` |
| `pnpm test:aot:native:e2e` | 通过 | CI `Run Native AOT external-process E2E` success；本地 Linux fresh 5/5 |
| Native Smoke（live/ready/SIGTERM） | 通过 | `NativeApiSmokeTests` |
| SQL Server 关键 HTTP 链路 | 通过 | `NativeApiSqlServerE2ETests` |
| MySQL 关键 HTTP 链路 | 通过 | `NativeApiMySqlE2ETests` |
| SignalR JSON + Redis 配置 | 通过（SQL Server + MySQL） | `NativeApiSignalRJsonE2ETests` |
| Notifications HTTP/JSON/SignalR | 通过（SQL Server + MySQL） | 见 [`api-native-aot-notifications-2026-08-25.md`](api-native-aot-notifications-2026-08-25.md)；CI [`run 32849677783`](https://github.com/yan041108/Full.NET/actions/runs/32849677783) 实际执行 2/2 |
| Settings / Jobs HTTP/JSON | 通过（SQL Server + MySQL） | 见 [`api-native-aot-settings-jobs-2026-08-25.md`](api-native-aot-settings-jobs-2026-08-25.md)；CI [`run 32872774812`](https://github.com/yan041108/Full.NET/actions/runs/32872774812) 实际执行 4/4 |
| **ADR-0008 状态** | **`Aot-published`** | Linux publish、启动及关键双库/SignalR 原生 E2E 已闭合 |

## Publish 命令（权威）

```bash
pnpm test:aot:publish:linux
```

等效核心参数：

- `Configuration=Release`
- `RuntimeIdentifier=linux-x64`
- `SelfContained=true`
- `FullNetPublishMode=NativeAot`

## 产物与阈值

- 产物路径：`artifacts/native-aot/linux-x64/publish/Full.NET.Host.Api`
- 最小体积门槛：见 `eng/testing/test-matrix.json#nativeAotPublish.minimumExecutableBytes`（当前 8_000_000）
- Manifest：`artifacts/native-aot/linux-x64/publish-manifest.json`（记录 SDK/RID/镜像/字节数/单次耗时；**不得**将单次耗时当作容量结论）

## Native E2E 命令

```bash
pnpm test:aot:native:e2e
```

## 剩余边界

- Worker/Migrator Native AOT
- Notifications HTTP/JSON/SignalR Native 切片见 [`api-native-aot-notifications-2026-08-25.md`](api-native-aot-notifications-2026-08-25.md)
- Settings / Jobs HTTP/JSON Native 切片见 [`api-native-aot-settings-jobs-2026-08-25.md`](api-native-aot-settings-jobs-2026-08-25.md)
- S3 与 Kafka Replay 的精确 Provider 状态见 [`api-native-aot-phase3-providers-2026-08-24.md`](api-native-aot-phase3-providers-2026-08-24.md)
- 多实例 SignalR Backplane 跨节点投递（JIT 集成测试已覆盖；Native 仅验证 Redis 配置下 JSON Hub 收发）
- 生产容量与 1 万并发 SLO（`Capacity-not-verified`）

## Suppression 清单

Phase 2 **未新增** `NoWarn=IL*`、通配 linker descriptor、通配程序集 Root 或 `UnconditionalSuppressMessage`；仅按 ADR-0008 §3.1 对 `MemoryPack.Core` 使用单程序集 `TrimmerRootAssembly`。publish 日志对已登记第三方程序集及 IL 告警码执行精确 allowlist，未知告警失败关闭。
