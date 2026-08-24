# Host.Api Native AOT Phase 3 Provider 闭包实施计划

- 日期：2026-08-24
- 分支：`cursor/api-native-aot-phase3-providers`
- 基线：`main` @ Phase 2 + publish 门禁加固
- 关联 ADR：[`ADR-0009`](../../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md)

## 目标

在 Phase 2 `Aot-published` 基础上，为 Host.Api Native 闭包内的 **S3** 与 **Kafka Replay API** 建立真实 Provider 运行证据。

## 任务分解

| 任务 | 内容 | 状态声明 |
|---|---|---|
| 3A | MinIO Testcontainers + Native S3 HTTP E2E（双库） | `Native-provider-verified: s3` |
| 3B spike | NativeAot 引用 Kafka + 真实 `AddFullNetKafkaReplayOperations` + publish 分类 | 决定是否 Blocked |
| 3B E2E | Kafka Testcontainers + 范围重放 Endpoint E2E（双库） | `Native-provider-verified: kafka-replay` |
| 治理 | ADR-0009、test-matrix、CI 分步、warning allowlist | 与证据一致 |

## 验收命令

```bash
pnpm test:aot:analyzers
pnpm test:aot:publish:linux
pnpm test:aot:native:e2e
pnpm test:aot:native:s3:e2e
pnpm test:aot:native:kafka-replay:e2e
pnpm test:aot:native:providers:e2e
```
