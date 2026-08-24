# Host.Api Native AOT Phase 3 Provider 验证记录

- 日期：2026-08-24
- 分支：`cursor/api-native-aot-phase3-providers`
- 任务快照：`api-native-aot-phase3-providers-20260824`
- 关联 ADR：[`ADR-0009`](../../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md)

## Phase 3A — S3

| 检查项 | 结果 | 证据 |
|---|---|---|
| `pnpm test:aot:native:s3:e2e`（Windows 发现） | 通过（2 项） | Linux 真跑待 CI |
| **状态** | 开发中 | 目标：`Native-provider-verified: s3` |

## Phase 3B — Kafka Replay

| 检查项 | 结果 | 证据 |
|---|---|---|
| `pnpm test:aot:analyzers`（Kafka 闭包） | 通过（0/0） | 2026-08-24 本地 |
| `pnpm test:aot:publish:linux` | 通过（含 `Confluent.Kafka` IL2104） | 产物 71,397,856 bytes；含 `librdkafka.so`；manifest 已写入 |
| 相对 Phase 2 体积 | +1,144,848 bytes | 70,253,808 → 71,397,856 |
| `pnpm test:aot:native:kafka-replay:e2e`（Windows 发现） | 通过（2 项） | Linux 真跑待 CI |
| **状态** | 开发中 | 目标：`Native-provider-verified: kafka-replay` |

## 推送与 CI

- 本地 `git push` 因 GitHub 443 超时未成功（2026-08-24）；需网络恢复后推送并开 PR 触发 `api-native-aot-linux`。
- Workflow `push` 仅监听 `main`；特性分支需 **PR** 或 `workflow_dispatch`。


- AWS Workload Identity / 实例角色 / Web Identity
- Worker Kafka Producer/Consumer Native 路径
