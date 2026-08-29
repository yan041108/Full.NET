# Worker Native AOT Phase 3 验证记录

## 范围

本阶段在 SQL Server/MySQL 隔离数据库中预置两条 Host 级 Legacy Outbox 消息，再由正常常驻 linux-x64 Native Worker 处理：

- 合法消息使用生产 `MemoryPackIntegrationEventSerializer` 序列化 `AnnouncementPublishedIntegrationEvent`；
- 损坏消息由同一合法载荷截断一个字节构造，夹具先证明生产反序列化器会抛出 `InvalidDataException`；
- 两条消息使用相同稳定路由、SchemaVersion 1 和 `application/x-memorypack`；
- Realtime 明确关闭，Handler 使用 `NullRealtimePublisher`，本阶段不依赖 Redis 或 SignalR。

终态门禁要求合法消息 `Attempts=1`、`ProcessedAtUtc` 非空、非死信且租约释放；损坏消息 `Attempts=1`、未处理、死信原因为 `outbox.invalid_payload` 且租约释放。两条消息还必须同时清空 `LockId`、`LockedUntilUtc` 和 `NextAttemptAtUtc`，避免把部分释放误判为稳定终态。测试最后发送 SIGTERM，要求 Worker 退出码为 0。

## TDD 与本地证据

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Release RED | 2 个 `CS0117` | SQL Server/MySQL 测试因 `VerifyLegacyOutboxDeliveryAsync` 尚不存在而精确失败，0 warning。 |
| Release GREEN | 0 warning、0 error | 探针、终态读取和常驻断言编译通过。 |
| `pnpm test:aot:worker:native:e2e` | 发现 6 项，6 项 Inconclusive | Windows 只验证发现门禁；未执行容器或原生进程。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 通过 | Worker 门禁包含探针文件和最低发现数 6。 |
| `pnpm test:integration:partitions` | 641 项一致 | infrastructure 149，无遗漏或重复。 |
| `pnpm test:aot:worker:analyzers` | 0 warning、0 error | AOT 分析与默认 JIT 重建均通过。 |
| `pnpm test:inner -- --snapshot worker-native-aot-phase3-20260829` | 通过 | 工具链 53/53、Release build 0 warning/0 error、分片 641 项一致、Governance 52/52。 |
| 独立只读审查 | 无 Critical/Important | 根据审查补齐 `LockedUntilUtc` 与 `NextAttemptAtUtc` 清理断言后复核通过。 |

## 未验证边界

- 本机不是 Linux 且 Docker daemon 不可用，双库原生消息终态必须等待 `worker-native-aot-linux.yml`；
- 未覆盖慢 Handler 租约续期、瞬时失败重试、进程崩溃后租约恢复和多 Worker 竞争；
- 未覆盖 Realtime 网络投递、Kafka/CDC、Files 启用态或 Jobs 执行终态；
- 未执行容量或生产等价负载，因此保持 `Capacity-not-verified`。

状态继续为 `Build-verified / Analysis-only`；CI 成功前不得声明 `Worker Aot-published`。
