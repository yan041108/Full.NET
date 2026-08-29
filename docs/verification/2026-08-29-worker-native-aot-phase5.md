# Worker Native AOT Phase 5 验证记录

## 范围

本阶段在 SQL Server/MySQL 隔离数据库中预置两条陈旧 Host 级 Pending 文件记录，再由正常常驻 linux-x64 Native Worker 执行 Files Upload Reconciliation：

- 两条记录均使用稳定 `ProviderKey=local`，测试只为其中一条在唯一临时 Files root 创建 Blob；
- 定义和记录由 JIT 测试夹具在同一事务写入，夹具不调用生产对账 Runner 或终态 SQL；
- 原生 Worker 显式使用同一 Files root，并把最小年龄设为验证器允许的 30 秒；
- 数据库终态要求存在 Blob 的记录变为 `ready` 且对象仍存在，缺失 Blob 的 Pending 记录被删除；
- 测试最后发送 SIGTERM，要求 Worker 退出码为 0，并只清理测试拥有的精确临时目录。

## TDD 与本地证据

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Release RED | 2 个 `CS0117` | SQL Server/MySQL 测试因 `VerifyFilesUploadReconciliationAsync` 尚不存在而精确失败，0 warning。 |
| Release GREEN | 0 warning、0 error | Files 探针、受控环境覆盖与常驻断言编译通过。 |
| `pnpm test:aot:worker:native:e2e` | 10 项发现、0 失败、10 项 Inconclusive | Windows 只验证 Release 构建与精确发现；不冒充 Linux 原生执行。 |
| `pnpm test:aot:worker:analyzers` | AOT/JIT 均为 0 warning、0 error | Worker 静态分析闭包保持干净。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 | Phase 5 探针、双库方法名和最低发现数已进入架构门禁。 |
| `pnpm test:integration:partitions` | 645 项，无遗漏或重复 | `infrastructure=153`，总数与矩阵一致。 |
| `pnpm test:inner -- --snapshot worker-native-aot-phase5-20260829` | 通过 | 工具链 53/53、Release 构建 0 warning/0 error、分区 645、Governance 52/52。 |
| 独立只读代码审查 | 无 Critical / Important | 核对双库类型映射、目录删除边界、场景配置隔离、SIGTERM、日志断言和防假绿门禁；结论为可提交，但必须等待 Linux CI 原生证据。 |

## 未验证边界

- 本机不是 Linux 且 Docker daemon 不可用，双库原生 Files 终态必须等待 `worker-native-aot-linux.yml`；
- 未覆盖 `publishing` 慢写入保留、软删除 Blob Cleanup 与 Reference Claim 对账；
- 未覆盖 S3、真实对象存储故障、Kafka/CDC 或连接准入故障；
- 未执行容量或生产等价负载，因此保持 `Capacity-not-verified`。

状态继续为 `Build-verified / Analysis-only`；CI 成功前不得声明 `Worker Aot-published`。
