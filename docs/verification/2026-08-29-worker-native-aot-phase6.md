# Worker Native AOT Phase 6 验证记录

## 范围

本阶段在 SQL Server/MySQL 隔离数据库中预置两条 Host 级已删除文件墓碑，再由正常常驻 linux-x64 Native Worker 执行 Files Blob Cleanup：

- 本地 Provider 墓碑指向测试独占 root 中的真实 Blob；
- 未知 Provider 墓碑用于证明解析失败时保留记录，而不是回退到默认 Provider 或阻断后续候选；
- 原生 Worker 显式启用生产默认关闭的 `Files:Cleanup`，使用有界批次与 5 秒轮询；
- 终态要求本地 Blob 与其元数据均删除，未知 Provider 墓碑仍存在；
- 测试最后发送 SIGTERM，要求 Worker 退出码为 0，并只清理测试拥有的随机叶目录。

## TDD 与本地证据

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Release RED | 2 个 `CS0117` | SQL Server/MySQL 测试因 `VerifyFilesDeletedBlobCleanupAsync` 尚不存在而精确失败，0 warning。 |
| Release GREEN | 0 warning、0 error | Cleanup 探针、受控环境覆盖与常驻断言编译通过。 |
| `pnpm test:aot:worker:native:e2e` | 12 项发现、0 失败、12 项 Inconclusive | Windows 只验证 Release 构建与精确发现；不冒充 Linux 原生执行。 |
| `pnpm test:aot:worker:analyzers` | AOT/JIT 均为 0 warning、0 error | Worker 静态分析闭包保持干净。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 | Phase 6 双库方法名和最低发现数已进入架构门禁。 |
| Cleanup Unit 聚焦 | 7/7 | 生产 Cleanup 的配置、分页、Blob 失败隔离与后台处理器回归通过。 |
| `pnpm test:integration:partitions` | 647 项，无遗漏或重复 | `infrastructure=155`，总数与矩阵一致。 |
| `pnpm test:inner -- --base 1bb20bd97362ccde4b0790eec57a31446359beb5` | 通过 | 工具链 53/53、Release 构建 0 warning/0 error、分区 647、Governance 52/52。 |
| 独立只读代码审查与复核 | 无未关闭 Critical / Important | 初审发现并关闭显式开关隔离、失败候选排序两项 Important；复核确认 Phase 5/6 开关互斥且未知 Provider 必先于本地候选处理。 |

## 未验证边界

- 本机不是 Linux；`docker version` 确认 desktop-linux daemon 不可连接，双库原生 Cleanup 终态必须等待 `worker-native-aot-linux.yml`；
- 未覆盖未释放 Reference Claim、并发软删除与清理竞争；
- 未覆盖 S3、真实对象存储故障、Kafka/CDC 或连接准入故障；
- 未执行容量或生产等价负载，因此保持 `Capacity-not-verified`。

状态继续为 `Build-verified / Analysis-only`；CI 成功前不得声明 `Worker Aot-published`。
