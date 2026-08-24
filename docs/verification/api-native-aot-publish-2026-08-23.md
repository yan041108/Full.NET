# Host.Api Native AOT Linux Publish 验证记录

- 日期：2026-08-24（fresh 复验）
- 分支：`main`
- 基线提交：`5d6e8c0e239d5191d06a971fdd5878fde1aaae47`
- 任务快照：`api-native-aot-phase2-ci-20260824`
- 关联计划：[`2026-08-23-api-native-aot-phase2.md`](../superpowers/plans/2026-08-23-api-native-aot-phase2.md)

## 状态摘要

> **填写说明：** 本地 Windows 开发机通过 Docker SDK 容器执行 publish；外部进程 E2E 需在 **Linux**（CI `ubuntu-latest` 或 WSL）上针对已发布产物运行。交付复验时以 fresh CI 输出更新下表。

| 检查项 | 结果 | 证据 |
|---|---|---|
| `pnpm test:aot:analyzers` | 通过（2026-08-24 22:14 UTC+8 fresh，`5d6e8c0e`） | 0 warning / 0 error，构建约 1m29s |
| `pnpm test:aot:publish:linux` | 通过（2026-08-24 22:20 UTC+8，Windows → Docker fresh） | `publish-manifest.json`（`publishMode=docker`，288087 ms）、`publish.log`（8 条已登记 IL 告警） |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 通过（2026-08-24 22:23 UTC+8 fresh） | 29/29 |
| Linux 原生可执行文件 | 通过（70,253,808 bytes） | `artifacts/native-aot/linux-x64/publish/Full.NET.Host.Api` |
| `pnpm test:aot:native:e2e`（Windows 发现门禁） | 通过（发现 5 项、按平台跳过 5 项） | Linux 原生进程执行以 `api-native-aot-linux` CI 为准 |
| Native Smoke（live/ready/SIGTERM） | **CI 待跑** | `NativeApiSmokeTests` |
| SQL Server 关键 HTTP 链路 | **CI 待跑** | `NativeApiSqlServerE2ETests` |
| MySQL 关键 HTTP 链路 | **CI 待跑** | `NativeApiMySqlE2ETests` |
| SignalR JSON + Redis 配置 | **CI 待跑** | `NativeApiSignalRJsonE2ETests` |
| **ADR-0008 状态** | **`Aot-analysis-clean`**（`Aot-published` 待 Linux CI E2E 绿） | — |

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

## 未验证项（Phase 2 范围外）

- Worker/Migrator Native AOT
- Kafka/S3 Provider Native 运行路径
- 多实例 SignalR Backplane 跨节点投递（JIT 集成测试已覆盖；Native 仅验证 Redis 配置下 JSON Hub 收发）
- 生产容量与 1 万并发 SLO（`Capacity-not-verified`）

## Suppression 清单

Phase 2 **未新增** `NoWarn=IL*`、通配 linker descriptor、通配程序集 Root 或 `UnconditionalSuppressMessage`；仅按 ADR-0008 §3.1 对 `MemoryPack.Core` 使用单程序集 `TrimmerRootAssembly`。publish 日志对已登记第三方程序集及 IL 告警码执行精确 allowlist，未知告警失败关闭。
