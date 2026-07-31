# Jobs 积压查询双库成本证据（2026-07-30）

## 状态与范围

- 状态：`Build-verified`
- 源版本：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 分支：`codex/performance-hardening`
- 任务快照：`jobs-backlog-query-evidence-20260730`
- 工件：`.tmp/jobs-backlog-query-evidence-20260730/formal`
- 范围：生产 Jobs backlog 聚合 SQL 的代表性规模双库正确性、P50/P95/P99 与实际执行计划。
- 不包含：生产等价 SLA、跨 Provider 排名、索引变更/A/B、并发 Worker 容量、生产指标导出或告警阈值。

## 可重复入口

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --output .tmp/jobs-backlog-query-evidence-20260730/formal
```

入口直接消费 `JobSql.ReadBacklogSqlServer.Text` 与 `JobSql.ReadBacklogMySql.Text`，Unit 契约会在
benchmark SQL 与生产 Statement 漂移时失败。每个 Provider 使用独立 Testcontainers 数据库并执行正式
DbUp 迁移；Provider 串行运行，结束后删除数据库容器并等待 Ryuk 退出。

## 环境与数据集

| 项目 | 值 |
| --- | --- |
| 操作系统 | Microsoft Windows 10.0.19045 |
| .NET | 10.0.9 |
| 逻辑处理器 | 20 |
| SQL Server | 2022 CU14 / 16.0.4135.4，镜像 `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` |
| MySQL | 8.0.46，镜像 `mysql:8.0` |
| 总行数 | 100000 |
| 观测时间 | `2026-07-30T00:00:00Z` |
| 并发 | 1 |
| 预热/采样 | 5 / 30 |

固定分布包含 50000 条 Host `pending`、15000 条已到期重试、10000 条未来重试、40000 条当前可领取
记录，以及 20000 条租户 `pending` 噪声；其余为 Host `running`/`succeeded`/`failed`。两个 Provider
的 35 次查询均通过 pending、due、最老可领取和最老到期时间正确性门禁。

## 墙钟结果

| Provider | 数据准备 | P50 ms | P95 ms | P99 ms | Min ms | Max ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 4.047 s | 44.637 | 72.793 | 78.322 | 37.896 | 78.322 |
| MySQL | 2.910 s | 128.530 | 206.528 | 216.856 | 98.978 | 216.856 |

这些结果包含每次采样新建连接的端到端查询墙钟时间。不同数据库镜像和实现不可用绝对值横向排名；
本机容器结果也不是生产 SLA。

## 执行计划事实

### SQL Server

工件：`sqlserver/actual.showplan.xml`

- 实际物理操作为 `Clustered Index Scan`，访问 `PK_fn_jobs_execution`，没有选择 037 pending 索引作为
  主访问路径。
- `ActualRowsRead = 100000`，聚合前输出 50000 行。
- 实际逻辑读为 2782；Showplan 的查询 CPU/Elapsed 均为 31 ms。
- 当前数据中总 `pending` 占 70%，但 Host `pending` 为 50%；`TenantId IS NULL` 过滤无法由现有
  filtered index 完整收敛。

### MySQL

工件：`mysql/estimated.explain.json` 与 `mysql/actual.explain-analyze.txt`

- `access_type = ref`，使用 `IX_fn_jobs_execution_PendingNextAttemptLease`，实际只使用首个
  `Status` key part。
- 索引查找实际读取 70000 条 `pending`，`TenantId IS NULL` 再过滤为 50000 条 Host 记录。
- `EXPLAIN ANALYZE` 记录索引查找约 113 ms、Host 过滤约 120 ms、最终聚合约 159 ms。
- JSON 估算 `rows_examined_per_scan = 50000`，低估了实际 70000 条 status 命中；应以 ANALYZE 实际值
  作为本轮事实。

## 结论与后续门禁

本轮补齐了此前缺失的代表性规模双库计划/成本证据，也证明单次采样查询在固定 10 万行数据上结果正确。
计划同时否定了“037 索引已充分覆盖 backlog 聚合”的假设：SQL Server 扫描全表，MySQL 仍需读取所有
status=pending 后再过滤租户。后续如果优化索引，应建立独立双库 A/B：

- SQL Server 评估 Host pending filtered index 或等价覆盖形状；
- MySQL 评估把 `TenantId` 纳入 status 后的有效前缀；
- 同时记录写放大、索引大小、领取/续租/终态路径回归、逻辑读/实际读行与 P95/P99。

没有 A/B 前不修改 037，不声明当前查询已获得性能收益，也不调整 `BacklogSampleSeconds = 30` 或
`MaxConcurrency = 1`。生产指标导出和告警阈值仍待真实部署基线。

后续已完成候选 `IX_fn_jobs_execution_BacklogStatusTenant` 的同容器双库镜像 A/B，详见
[Jobs backlog 候选索引双库 A/B](jobs-backlog-index-ab-2026-07-30.md)。正式 10 万行结果中，
SQL Server 的计划级读取改善没有转化为墙钟尾延迟收益，且三条写路径均超过 20% 回退门槛；
MySQL 未选择候选索引且查询 P95/P99 回退。因此该候选被明确否决，不创建生产迁移。

## 工程验证边界

- `JobsBacklogQueryBenchmarkTests`：7/7；
- Jobs Unit 聚焦：26/26；
- benchmark Release build：0 警告、0 错误；
- Architecture：49/49；
- Naming：23/23；
- Governance：16/16；
- 测试矩阵契约：4/4；
- SQL Server/MySQL 正式入口：各 30/30 有效样本，正确性门禁均通过；
- 本轮新增 7 个 Unit 测试方法、0 个 Integration 测试方法；
- 共享工作区新鲜 Release discovery 为 Unit 708、Integration Full 228、Infrastructure 82、
  API SQL Server/MySQL 各 38、Migrations 70，并保留 migration selection 037；
- affected inner 计划因任务快照后的协作窗口写入同时命中 CodeGeneration、Files、
  integration-matrix、Jobs 与 Realtime；本窗口不重复运行其它窗口已完成的组合集合；
- 没有运行完整 Integration；完整集合仍由 `main` CI 互斥分片执行；
- 任务结束后 `docker ps` 为空，并已按共享队列释放给 Files 窗口。

规则演进检查未命中规则冲突或高风险新类别；Skill 已补充可重复命令，属于既有性能地图的入口完善，
不新增 Skill 候选。
