# Worker Native AOT Phase 0 验证记录

## 范围

本记录验证 `Full.NET.Host.Worker` 的 AOT/Trim 静态分析闭包，包括 Worker 自身 JSON、SQL 参数、Dapper 行物化器和模块后台注册。它不声明 Linux 原生产物、外部进程运行、Provider 或生产容量已经验证。

## 基线

- 基线提交：`7f3e06c9f8d3bd7dfa63a12af57f2f04de7998b9`
- 任务快照：`worker-native-aot-phase0-20260829`
- 决策：[`ADR-0010`](../architecture/adr/ADR-0010-worker-native-aot-analysis-boundary.md)

## 验证结果

| 检查 | 结果 | 说明 |
| --- | --- | --- |
| Files AOT 参数门禁（RED） | 失败，命中 3 个文件 | 门禁原先只识别同行 `new {`，无法识别换行匿名对象；修正扫描后先证明能够捕获真实违规。 |
| Files AOT 参数门禁（GREEN） | 1/1 通过 | 三个后台 Runner 改为稳定参数字典，未改变 SQL 与事务语义。 |
| Worker SQL/JSON 门禁（RED） | 失败 | 首次运行捕获 Shadow 查询匿名参数，证明新门禁有效。 |
| Worker 后台模块物化器门禁（RED） | 失败，命中 11 个模块 | Worker 的最小装配图此前没有同步执行模块物化器 Contributor。 |
| Worker analyzer 入口门禁（RED） | 失败 | 首次运行捕获缺失的独立 Worker analyzer 脚本。 |
| `pnpm test:aot:worker:analyzers` | 通过 | Release AOT 分析构建 0 warning、0 error；随后 restore 并以 `-t:Rebuild` 强制恢复默认 JIT 产物。Architecture 回归门禁先失败、修复后 1/1 通过，避免只 restore 遗留条件编译 DLL。 |
| `pnpm test:aot:analyzers` | 通过 | Host.Api 回归分析构建 0 warning、0 error。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | 70/70 通过 | 覆盖 Worker 与既有 Host.Api 静态闭包规则。 |
| `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~OutboxVersionRetirement\|FullyQualifiedName~DeletedHostFileBlobCleanupTests\|FullyQualifiedName~PendingHostFileReconciliationTests\|FullyQualifiedName~ShadowEventComparisonTests" --minimum-expected-tests 1` | 22/22 通过 | 参数读取测试同时覆盖稳定字典。 |
| `pnpm test:integration:partitions` | 通过 | 首次运行发现基线清单为 633、实际为 635；核对只有 infrastructure 从 141 增至 143 后更新唯一矩阵，复跑得到五分片合计 635，无遗漏或重复。 |
| `pnpm test:governance` | 52/52 通过 | 文档、规则和仓库治理检查通过。 |
| `pnpm test:inner -- --snapshot worker-native-aot-phase0-20260829` | **未通过** | 集成工具 53/53 与 Release 构建通过；选择出的 34 个 SQL Server/MySQL 测试均在 Testcontainers 初始化阶段因本机 Docker daemon 不可用而失败，未进入产品代码。不得外推为双库运行验证。 |
| `pnpm test:dotnet:architecture` | **未通过，166/176** | 额外全量检查暴露 10 项任务范围外失败，其中包含缺失 `node_modules/.pnpm/node_modules/@parcel/watcher` 的环境异常，以及既有依赖、错误码和 SerialNumbers SQL catalog 违规；本记录不把 AOT 选择器通过外推为全量 Architecture 通过。 |

## 结论

Phase 0 达到 `Worker Aot-analysis-clean`：Worker 完整引用图的 AOT 分析以及静态 SQL、JSON、物化器门禁均已关闭。该结论仅代表构建与架构静态闭包；由于本机 Docker 不可用，双数据库 Worker 运行行为仍未验证，且没有生成 Linux Native AOT 产物。

## 未验证边界

- Worker linux-x64 Native AOT publish、启动和停止；
- SQL Server/MySQL 原生 Worker 外部进程；
- Kafka Producer/Consumer、CDC Relay、DLQ 与 Lag Observer 原生路径；
- Migrator Native AOT；
- 生产等价吞吐、P99、内存和恢复能力。
