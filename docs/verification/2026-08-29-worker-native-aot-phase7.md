# Worker Native AOT Phase 7 验证记录

## 范围

本阶段在 SQL Server/MySQL 隔离数据库中预置两个 Ready 文件和两条超龄 Pending 引用 Claim，再由正常常驻 linux-x64 Native Worker 执行 Files Reference Claim Reconciliation：

- 真实引用 Claim 指向存在的 Document Item/Version，且 `(VersionId, FileId)` 精确匹配；
- 孤儿 Claim 指向不存在的 Document Version，并已超过释放宽限期；
- 外部进程无条件关闭 Files Upload Reconciliation 与 Cleanup，仅为本场景启用 Reference Claim Reconciliation；
- 终态要求真实引用变为 `active` 并写入 `ConfirmedAtUtc`，孤儿变为 `released` 并写入 `ReleasedAtUtc`；
- 测试最后发送 SIGTERM，要求 Worker 退出码为 0，且日志不存在 AOT 或后台迭代致命标记。

## TDD 与本地证据

基线：`0f4a2ee10380569626880ab8ab30f7e4a523c0ee`

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Release RED | 2 个 `CS0117` | SQL Server/MySQL 测试因 `VerifyFilesReferenceClaimReconciliationAsync` 尚不存在而精确失败，0 warning。 |
| Release GREEN | 0 warning、0 error | 双库 fixture、受控环境覆盖与常驻断言编译通过。 |
| `pnpm test:aot:worker:native:e2e` | 14 项发现、0 失败、14 项 Inconclusive | Windows 只验证 Release 构建与精确发现；不冒充 Linux 原生执行。 |
| `pnpm test:aot:worker:analyzers` | AOT 分析与强制 JIT 重建均为 0 warning、0 error | Worker 静态分析闭包保持干净，脚本结束后恢复默认 JIT 产物。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 | Phase 7 Probe、双库方法名和最低发现数已进入架构门禁。 |
| `pnpm test:integration:partitions` | 649 项，无遗漏或重复 | `infrastructure=157`，总数与矩阵一致。 |
| `pnpm test:inner -- --base 0f4a2ee10380569626880ab8ab30f7e4a523c0ee` | 通过 | 工具链 53/53、Release 构建 0 warning/0 error、分片 649、Governance 52/52。 |
| `docker version` | Linux daemon 不可连接 | `desktop-linux` 命名管道不存在，因此本机不能执行双库 Linux 原生进程终态。 |

直接使用 `dotnet test ... --filter FullyQualifiedName~NativeWorker` 在当前 Microsoft.Testing.Platform 入口运行 0 项并返回代码 5；仓库权威脚本改为直接执行测试程序集，先 JSON 精确发现 14 项，再以相同 filter 运行，结果如上。该命令层差异没有通过放宽最低发现数处理。

## 未验证边界

- 本机不是 Linux 且 Docker Desktop Linux daemon 不可用，双库原生 Reference Claim 终态必须等待 `worker-native-aot-linux.yml`；
- 未覆盖 Probe 抛异常或未知 ConsumerModule 的失败隔离；
- 未覆盖并发 Claim/Delete、其他消费模块、S3、Kafka/CDC 或连接准入故障；
- 未执行容量或生产等价负载，因此保持 `Capacity-not-verified`。

状态继续为 `Build-verified / Analysis-only`；CI 成功前不得声明 `Worker Aot-published`。
