# Host.Api Native AOT Phase 2 实施计划

- 日期：2026-08-23
- 分支：`cursor/api-native-aot-phase2`
- 基线：`37c82e09d8b1f4c5f061cf2ea47197e7562c7ebc`（`main`）
- 前置：`Aot-analysis-clean`（Phase 1，见 [`api-native-aot-readiness-2026-08-23.md`](../../verification/api-native-aot-readiness-2026-08-23.md)）
- 关联 ADR：[`ADR-0008`](../../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)

## 目标

在不缩小 Host.Api 模块闭包的前提下，完成 **linux-x64 Native AOT** 的真实 publish、链接、原生进程启动与关键运行链路验证；全部门禁通过后状态升为 **`Aot-published`**。

## 任务分解

| 任务 | 内容 | 交付物 |
|---|---|---|
| Task 5 | Linux publish 门禁 | `pnpm test:aot:publish:linux`、`eng/testing/test-matrix.json#nativeAotPublish`、治理/架构测试 |
| Task 6 | 原生产物 Smoke | `NativeApiProcessHost`、liveness/readiness/SIGTERM 外部进程测试 |
| Task 7 | 关键链路 E2E | JIT Migrator + Native API：双库 HTTP、SignalR JSON（Guid/string/long）、模块闭包只读链路 |
| Task 8 | CI 与文档 | `.github/workflows/api-native-aot-linux.yml`、验证记录、`capability-status` |

## 明确禁止

- Worker/Migrator/AppHost/CLI 设置 `PublishAot=true`
- `NoWarn=IL*`、通配 linker descriptor、无依据 suppression
- 关闭 Identity/Tenancy/Organization/Files/CodeGeneration/SignalR 模块
- Windows analyzer build 冒充 Linux publish
- Kafka/S3 Native 运行路径

## 验收命令

```bash
pnpm test:aot:analyzers
pnpm test:aot:publish:linux
pnpm test:dotnet:architecture -- --selection api-native-aot
pnpm test:aot:native:e2e
pnpm test:integration:affected -- --snapshot api-native-aot-phase2 --phase merge
```

## 完成定义

仅当 **Linux publish、原生启动、双库关键 HTTP、SignalR JSON（含 Redis 配置路径）** 全部通过时声明 `Aot-published`；否则保持 `Aot-analysis-clean` 并列出未完成项。
