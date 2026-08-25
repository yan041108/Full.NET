# Host.Api Native AOT Phase 3 Provider 验证记录

- 日期：2026-08-25（Linux CI fresh 复验）
- 分支：`main`
- 基线提交：`f3ea5f51c76275968f0525b4b5c57c0a865eed6b`
- 任务快照：`api-native-aot-phase3-providers-20260824`
- 关联 ADR：[`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md)
- CI 证据：[`api-native-aot-linux` run 32821397581](https://github.com/yan041108/Full.NET/actions/runs/32821397581)

## Phase 3A — S3

| 检查项 | 结果 | 证据 |
|---|---|---|
| `pnpm test:aot:native:s3:e2e` | 通过 | CI `Run Native AOT S3 Provider E2E` success；本地 Linux fresh SQL Server/MySQL 2/2 |
| 运行边界 | 通过 | Native Host.Api 外部进程 + 双库文件元数据 + 真实 MinIO S3 HTTP 上传/下载/删除 |
| **状态** | **完成** | **`Native-provider-verified: s3`** |

## Phase 3B — Kafka Replay

| 检查项 | 结果 | 证据 |
|---|---|---|
| `pnpm test:aot:analyzers`（Kafka 闭包） | 通过 | CI analyzer step success；本地 fresh 0 warning / 0 error |
| `pnpm test:aot:publish:linux` | 通过 | CI publish success；同提交本地产物 71,926,064 bytes，包含 `librdkafka.so` |
| `pnpm test:aot:native:kafka-replay:e2e` | 通过 | CI `Run Native AOT Kafka Replay Provider E2E` success；本地 Linux fresh SQL Server/MySQL 2/2 |
| 运行边界 | 通过 | Native Host.Api 外部进程 + 真实 Kafka 范围重放；Confluent native binding 由精确 RD.XML root 保留 |
| **状态** | **完成** | **`Native-provider-verified: kafka-replay`** |

## CI 结论

- run 32821397581 于 2026-08-25 15:24–15:34（UTC+8）完成，结论 `success`。
- analyzer、publish、architecture、integration build、核心 E2E、S3、Kafka Replay 与 manifest 上传均成功。

## 未验证边界

- AWS Workload Identity / 实例角色 / Web Identity
- Worker Kafka Producer/Consumer Native 路径
- CDC Relay、DLQ、Lag Observer 的 Native AOT 路径
