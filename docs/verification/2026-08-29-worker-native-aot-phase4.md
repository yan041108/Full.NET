# Worker Native AOT Phase 4 验证记录

## 范围

本阶段在 SQL Server/MySQL 隔离数据库中预置一个 Host 级启用的内置 Ping Job 定义和一条 Pending 手动执行记录，再由正常常驻 linux-x64 Native Worker 自动处理：

- 定义使用稳定 `HandlerKind=ping`，`ArgsJson=NULL`，与生产 `PingJobExecutor` 契约一致；
- 定义和执行由 JIT 测试夹具在同一事务写入，夹具不调用任何生产领取或终态方法；
- 测试按执行 UUID 等待数据库终态，不以日志作为业务成功依据；
- 成功终态必须为 `Succeeded`、`AttemptCount=1`、开始/结束时间非空、结束时间不早于开始时间，并同时清空错误、租约和重试字段；
- 时间完整性与先后顺序由数据库端投影为整型标志，避免测试夹具依赖 SQL Server `datetimeoffset` 与 MySQL `datetime(6)` 的隐式 CLR 类型转换；
- 测试最后发送 SIGTERM，要求 Worker 退出码为 0。

## TDD 与本地证据

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Release RED | 2 个 `CS0117` | SQL Server/MySQL 测试因 `VerifyJobsPingExecutionAsync` 尚不存在而精确失败，0 warning。 |
| Release GREEN | 0 warning、0 error | Jobs 探针、终态读取和常驻断言编译通过。 |
| `pnpm test:aot:worker:native:e2e` | 发现 8 项，8 项 Inconclusive | Windows 只验证发现门禁；未执行容器或原生进程。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 通过 | Worker 门禁固定双库 Ping 测试名、探针文件和最低发现数 8。 |
| `pnpm test:aot:worker:analyzers` | 0 warning、0 error | AOT 分析与默认 JIT 重建均通过。 |
| `pnpm test:integration:partitions` | 643 项一致 | infrastructure 151，无遗漏或重复。 |
| `pnpm test:inner -- --snapshot worker-native-aot-phase4-20260829` | 通过 | 工具链 53/53、Release build 0 warning/0 error、分片 643 项一致、Governance 52/52。 |
| Files 相关 Unit 聚焦测试 | 14/14 通过 | 覆盖本里程碑中 Files 后台 runner 的取消与 AOT 参数调整。 |
| 独立只读审查 | 无 Critical/Important | 根据审查移除跨 Provider `DateTimeOffset` 隐式映射，并固定双库测试名后复核通过。 |

提交前执行完整受影响 slice 时，工具链、Release build、分片一致性和 Governance 均通过，但 8 项数据库 Smoke 在夹具启动前统一因本机 Docker daemon 不存在而失败。`docker version` 同步确认 `dockerDesktopLinuxEngine` pipe 不存在；该结果属于环境阻塞，不记录为测试通过，双库运行证据继续由 Linux CI 提供。

## 未验证边界

- 本机不是 Linux 且 Docker daemon 不可用，双库原生 Jobs 终态必须等待 `worker-native-aot-linux.yml`；
- 未覆盖失败重试、长任务租约续期、进程崩溃后租约恢复、多 Worker 竞争和计划调度；
- 未覆盖 HTTP Handler 网络执行、Kafka/CDC、Files 启用态或连接准入故障；
- 未执行容量或生产等价负载，因此保持 `Capacity-not-verified`。

状态继续为 `Build-verified / Analysis-only`；CI 成功前不得声明 `Worker Aot-published`。
