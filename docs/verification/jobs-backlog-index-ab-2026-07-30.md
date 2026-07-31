# Jobs backlog 候选索引双库 A/B（2026-07-30）

## 状态与结论

- 状态：`Build-verified`
- 源版本：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 分支：`codex/performance-hardening`
- 任务快照：`jobs-backlog-index-ab-20260730`
- 正式工件：`.tmp/jobs-backlog-index-ab-20260730/formal-v4`
- 结论：候选索引 `IX_fn_jobs_execution_BacklogStatusTenant` **不允许进入独立迁移切片**。

本轮只在隔离 Testcontainers 数据库中创建和删除候选索引，没有修改 037、生产 SQL、Worker
配置、默认并发或积压采样周期，也没有创建 038。SQL Server 的候选索引减少了实际读取行和逻辑读，
但 `trigger_insert` 与 `terminal_success` 写路径未通过门禁；MySQL 优化器没有选择候选索引，
查询 P95/P99 也未同时改善。任一 Provider 阻断即保持生产结构不变。

## 实验设计

每个 Provider 只启动一个容器，执行正式 DbUp 迁移并写入相同的 100000 行固定分布数据，然后按
`baseline -> candidate -> candidate -> baseline` 四个镜像块运行。每个 Variant 合计 5 次预热、
30 次查询采样和 10 次写路径采样；查询始终直接消费生产 backlog Statement。

写路径探针直接消费生产 `trigger_insert`、`claim` 和 `terminal_success` Statement，在独立事务中
校验影响行数后回滚，因此各块的数据集不会随采样漂移。门禁要求：

- pending、due、最老可领取时间和最老到期时间全部正确；
- baseline/candidate 实际计划均完整；
- candidate 查询 P95 与 P99 都严格改善；
- 三条写路径的 candidate P95 相对 baseline 均不得回退超过 20%。

候选索引形状：

| Provider | 键与覆盖 |
| --- | --- |
| SQL Server | `(Status, TenantId) INCLUDE (NextAttemptAtUtc, CreatedAtUtc)` |
| MySQL | `(Status, TenantId, NextAttemptAtUtc, CreatedAtUtc)` |

## 正式结果

### 查询与索引成本

| Provider | Variant | P50 ms | P95 ms | P99 ms | 创建耗时 | 索引体积 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| SQL Server | baseline | 72.426 | 394.141 | 397.838 | — | — |
| SQL Server | candidate | 54.082 | 81.300 | 84.416 | 202.952 ms | 8,626,176 bytes |
| MySQL | baseline | 165.502 | 404.613 | 1,102.225 | — | — |
| MySQL | candidate | 207.026 | 415.556 | 496.476 | 3,368.195 ms | 5,783,552 bytes |

这些结果是本机容器内单并发受控实验，不代表生产 SLA；SQL Server 与 MySQL 的绝对耗时不得
横向排名。门禁只比较同一 Provider、同一容器和同一数据集中的两个 Variant。

### 写路径 P95

| Provider | 路径 | baseline ms | candidate ms | 相对变化 | 门禁 |
| --- | --- | ---: | ---: | ---: | --- |
| SQL Server | `trigger_insert` | 176.400 | 1,199.986 | +580.26% | BLOCK |
| SQL Server | `claim` | 446.648 | 236.983 | -46.94% | PASS |
| SQL Server | `terminal_success` | 173.081 | 258.971 | +49.62% | BLOCK |
| MySQL | `trigger_insert` | 50.893 | 463.567 | +810.87% | BLOCK |
| MySQL | `claim` | 237.820 | 439.451 | +84.78% | BLOCK |
| MySQL | `terminal_success` | 38.564 | 56.247 | +45.85% | BLOCK |

SQL Server 查询收益门禁通过，但两条写路径阻断；MySQL 被候选索引未实际采用、查询收益和三条
写路径共同阻断。

## 执行计划事实

### SQL Server

工件：

- `sqlserver/baseline/actual.showplan.xml`
- `sqlserver/candidate/actual.showplan.xml`

baseline 继续对 `PK_fn_jobs_execution` 执行 `Clustered Index Scan`，实际读取 100000 行、输出
50000 行，逻辑读 2786。candidate 计划选择
`IX_fn_jobs_execution_BacklogStatusTenant` 的 `Index Seek`，实际读取和输出均为 50000 行，
逻辑读降为 572；单次 Showplan 查询的 CPU/Elapsed 从 46/46 ms 降为 38/38 ms。

计划级改善也转化为本轮四块镜像墙钟 P95/P99 改善，但 8.23 MiB 的额外索引使
`trigger_insert` 与 `terminal_success` P95 明显超过 20% 门槛。因此不能依据查询和逻辑读收益
绕过写路径门禁。

### MySQL

工件：

- `mysql/baseline/estimated.explain.json`
- `mysql/baseline/actual.explain-analyze.txt`
- `mysql/candidate/estimated.explain.json`
- `mysql/candidate/actual.explain-analyze.txt`

baseline 与 candidate 都选择既有 `IX_fn_jobs_execution_PendingNextAttemptLease`，只使用
`Status` key part，实际读取 70000 条 `pending` 后再按 `TenantId IS NULL` 过滤为 50000 条。
候选索引虽出现在 `possible_keys`，但没有被选中；candidate 的 `EXPLAIN ANALYZE` 聚合实际时间
约 151 ms，高于 baseline 的约 125 ms。5.52 MiB 的额外索引没有带来查询路径收益。

## 索引体积采集修复

MySQL 首轮 smoke 证明应用账号不能读取 `mysql.innodb_index_stats`。改用应用可访问的
`INFORMATION_SCHEMA.TABLES.INDEX_LENGTH` 后，又暴露创建索引后的统计缓存仍返回旧值。最终采集
在同一连接中设置 `information_schema_stats_expiry = 0`、执行
`ANALYZE TABLE fn_jobs_execution`，再读取 candidate/baseline 总索引体积差值。该刷新发生在
A/B 采样之外，不计入查询或写路径耗时；2000 行 smoke 记录到 131072 bytes，正式 100000 行记录到
5783552 bytes。

## 可重复入口

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --mode index-ab --output .tmp/jobs-backlog-index-ab-20260730/formal-v4
```

短开发验证可显式限制单个 Provider：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --mode index-ab --rows 2000 --warmup 1 --iterations 5 --mutation-iterations 3 --providers mysql --output .tmp/jobs-backlog-index-ab-20260730/smoke-mysql
```

## 工程验证

- Jobs Unit 聚焦：34/34 通过，其中本切片的 backlog benchmark 契约为 15/15。
- 全量 Unit：744/744 通过，`eng/testing/test-matrix.json` 的 Unit minimum 已新鲜更新为 744。
- Integration 新鲜 discovery：full 230、API SQL Server 39、API MySQL 39、migrations 70、
  infrastructure 82；037 migration selection 保持不变。
- Architecture：49/49；Naming：23/23；Governance：16/16；
  Performance governance：3/3；Test matrix contract：4/4。
- 项目 Skills 验证：module-delivery 52 项、performance-hardening 33 项，全部通过。
- `Full.NET.Benchmarks` Release 构建为 0 warning / 0 error；CLI `jobs-backlog-query --help`
  退出码为 0。
- 正式双库 A/B 均完成正确性、实际计划、查询尾延迟、三条写路径与索引体积采样；两库结论均为
  `BLOCK`。Jobs 容器、数据库和 Ryuk 在释放 Docker 时均已退出。
- `affected:plan --phase slice` 命中 CodeGeneration、Files、Realtime、integration-tooling 与
  smoke；共享窗口已按同一工作区状态完成合并影响集 28/28、tooling 39/39、smoke 8/8，
  因 Jobs benchmark 本身未新增 Integration 测试，本窗口没有重复占用 Docker。

## 后续门禁

- 不为该候选创建生产迁移，也不修改 037。
- 若继续探索，应改变候选形状或查询策略，并重新执行完整双库镜像 A/B；不得只依据单次计划或
  2000 行 smoke 推断正式收益。
- SQL Server 后续候选必须同时解释写放大和端到端尾延迟；MySQL 后续候选必须先证明优化器实际
  选择目标索引。
- 默认 `Jobs:Worker:BacklogSampleSeconds = 30` 与 `MaxConcurrency = 1` 保持不变。
