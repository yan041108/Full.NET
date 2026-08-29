# Worker Native AOT Phase 1 验证记录

## 范围

本阶段建立 Worker 独立 linux-x64 publish 契约、GitHub Actions 门禁，以及 SQL Server/MySQL 一次性 Outbox 版本退役扫描外部进程测试。它验证的目标链路是启动、Dapper AOT backlog 读取、源生成 JSON 和正常退出，不覆盖常驻消费与容量。

## 本地证据

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Phase 1 Architecture RED | 失败 | 首次运行因 Worker publish/E2E 命令与矩阵缺失而失败。 |
| `WorkerNativeAot_HasIsolatedPublishAndDualDatabaseE2EGates` | 1/1 通过 | 产物目录、命令、矩阵、双库测试文件与独立 CI 均已登记。 |
| `pnpm test:aot:worker:native:e2e` | 发现 2 项，2 项 Inconclusive | Windows 只验证发现门禁；在判断平台前启动容器的首版测试已失败并修正。未执行原生进程。 |
| `pnpm test:integration:partitions`（首次） | 失败 | 新增两项后实际发现 637，与旧 canonical 635 不一致；矩阵随后按唯一事实源更新。 |
| `pnpm test:integration:partitions`（复跑） | 通过 | 五个分片合计 637，无遗漏或重复。 |
| `pnpm test:aot:worker:analyzers` | 通过 | AOT 分析与默认 JIT 重建均为 0 warning、0 error。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 通过 | 同时覆盖 Phase 0 静态闭包和 Phase 1 publish/E2E 契约。 |
| `pnpm test:inner -- --snapshot worker-native-aot-phase1-20260829` | 通过 | Integration tooling 53/53、Release build、637 项分片和 Governance 52/52 均通过；inner 按规则不执行 native-aot 外部进程。 |

## 尚未完成的发布证据

- 本机 Docker daemon 不可用，未执行 `pnpm test:aot:worker:publish:linux`；
- 尚无 Linux Worker executable、manifest 或 publish warning 输出；
- SQL Server/MySQL 原生 Worker 测试必须由 `worker-native-aot-linux.yml` 在 Linux CI 实际通过；
- CI 成功前能力状态继续保持 `Build-verified / Analysis-only`，不得使用 `Worker Aot-published`。

## 后续边界

完成本阶段 CI 后，下一独立切片才验证常驻 Legacy Outbox 的领取、处理、优雅停止与租约恢复；Kafka/CDC、Jobs、Files 和容量各自保留独立门禁。
