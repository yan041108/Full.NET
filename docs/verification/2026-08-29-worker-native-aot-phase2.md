# Worker Native AOT Phase 2 验证记录

## 范围

本阶段在 Phase 1 一次性命令之外增加 Worker 正常常驻路径门禁。SQL Server 与 MySQL 测试均由 JIT Migrator 准备 schema，然后启动 linux-x64 原生 Worker：

1. `/health/live` 返回 200，证明常驻宿主完成启动；
2. `fn_jobs_worker_instance` 出现 Host 心跳，证明 Jobs 后台循环至少完成一次数据库写入；
3. 保持短暂空载运行，使 Legacy Outbox、Jobs 以及默认关闭的 Files 后台服务进入生产循环边界；
4. 发送 SIGTERM，要求进程在 30 秒内以代码 0 退出；
5. 日志不得包含 AOT 致命标记或 Outbox、Jobs、Files 后台迭代故障。

## TDD 证据

| 阶段 | 命令 | 结果 |
| --- | --- | --- |
| RED | `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` | 因 `NativeWorkerE2EAssertions.VerifyPersistentRuntimeAsync` 尚不存在而产生 2 个 `CS0117`，0 warning。 |
| GREEN | 同一 Release build | 0 warning、0 error。 |
| 发现门禁 | `pnpm test:aot:worker:native:e2e` | Windows 发现 4 项；4 项按非 Linux 平台约束 Inconclusive，未把跳过表述为原生通过。 |

## 本地验证

| 检查 | 结果 |
| --- | --- |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 71/71 通过，0 skip。 |
| `pnpm test:integration:partitions` | 639 项无遗漏或重复；infrastructure 147。 |
| `pnpm test:aot:worker:analyzers` | AOT 分析与默认 JIT 重建均为 0 warning、0 error。 |
| `pnpm test:inner -- --snapshot worker-native-aot-phase2-20260829` | Tooling 53/53、Release build 0 warning/0 error、639 项分片一致性与 Governance 52/52 均通过。 |
| 独立代码审查 | 修复后台吞异常日志标记遗漏及 `kill` 超时诊断后复核通过，无剩余 Critical/Important。 |

## 未验证边界

- 本机不是 Linux，且 Docker daemon 不可用，未运行原生常驻 Worker、SQL Server 或 MySQL 容器；
- 4 项测试必须由 `worker-native-aot-linux.yml` 在发布产物后真实执行；
- 本阶段未写入 Outbox 业务消息，未验证处理、重试、续租或死信；
- Jobs 只验证心跳，不验证任务领取、执行、续租或终态；
- Files 后台能力保持默认关闭，未验证启用态 Blob/数据库行为；
- Kafka/CDC、连接池过载恢复和容量仍未覆盖。

因此能力状态继续保持 `Build-verified / Analysis-only`，不得声明 `Worker Aot-published` 或 `Capacity-verified`。
