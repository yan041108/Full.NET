# Full.NET 性能地图

## 入口与证据

| 领域 | 主要路径 | 首选证据 |
| --- | --- | --- |
| API 请求管道 | `src/Hosts/Full.NET.Host.Api/Program.cs` | ASP.NET Core P50/P95/P99、错误率、并发 |
| Dapper | `src/BuildingBlocks/Full.NET.Data.Dapper/` | `StatementName` 耗时、调用量、失败、执行计划 |
| 认证 | `src/Modules/Full.NET.Modules.Identity/Security/` | 会话/API Key 往返、撤销时效、连接池 |
| 租户缓存 | `src/Modules/Full.NET.Modules.Tenancy/Persistence/` | FusionCache L1/L2 命中、源加载、失效时延 |
| Audit | `src/Modules/Full.NET.Modules.Auditing/` | 请求尾延迟、写入失败、表增长、清理耗时 |
| Outbox | `src/Hosts/Full.NET.Host.Worker/`、`src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/` | 吞吐、重复、失败、最老消息年龄 |
| Jobs | `src/Modules/Full.NET.Modules.Jobs/Execution/` | 领取/执行耗时、租约续期、积压 |
| Vue/Layui | `ui/admin/`、`ui/admin-layui/` | minified、gzip、Brotli、首屏请求与交互时间 |

## 基线记录模板

每次性能 Verification 至少记录：

```text
Commit/Branch:
Date/Environment:
CPU/Memory/OS/.NET/Node:
Database Provider/Version:
Dataset:
Scenario/Concurrency/Duration/Warmup:
Throughput/Error rate:
P50/P95/P99:
DB CPU/IO/locks/pool wait:
Allocation/GC:
Artifact paths:
```

## 建议场景

### API

1. 匿名只读请求；
2. JWT 认证只读请求；
3. API Key 认证请求；
4. 认证写请求；
5. Host 工作台；
6. 审计第一页与深分页；
7. 依赖超时和客户端取消。

跨请求链的双库混合负载入口为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- mixed-load
```

默认矩阵对 SQL Server/MySQL 分别运行 `1/4/16/32` 并发，每档预热 30 秒、稳态
600 秒，并覆盖 JWT/API Key、读写、预期验证失败、Audit 查询和 Outbox 入队。每个
Provider/并发单元使用独立数据库与 API Host；工件包含固定 manifest、汇总和逐请求
NDJSON。该入口用于冻结本机回归预算，不得把 TestServer QPS 当作生产 SLA，也不得用
API 侧 Outbox pending 增量替代 Worker 消费容量证据。

审计列表的可重复双库入口为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query
```

默认矩阵使用 10 万行、并发 1、预热 5 次、采样 30 次，输出 SQL Server
`STATISTICS XML` 实际计划和 MySQL `EXPLAIN FORMAT=JSON`。工件位于
`BenchmarkDotNet.Artifacts/auditing-query/` 且不提交；Verification 必须摘录
环境、P50/P95/P99、实际读行/扫描/排序事实和未验证项。

SQL Server 可选谓词出现参数敏感或首次请求污染计划时，运行：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver
```

该模式在隔离容器中按策略/请求顺序清空计划缓存，比较 `current_optional`、
`branch_specific` 与 `recompile`。必须同时判断 P95/P99、逻辑读、实际读行、缓存命中
和编译 CPU；缓存策略的计划指标是一次编译成本，`recompile` 是单次执行编译成本，
禁止把两者当作同一采样窗口的总编译 CPU。

MySQL 深 OFFSET 出现全表访问和 filesort 时，先运行隔离索引 Hint A/B：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode mysql-index-ab --providers mysql
```

该模式在同一 MySQL 8.0 容器中成对比较优化器选择与固定时间索引 Hint，并逐轮反转
执行顺序。只有深页 P50/P95/P99、首屏、contains 和执行计划共同支持时才可形成生产
Task；禁止仅因计划从 filesort 变为 index 就落地 Hint。

时间索引 Hint 未通过门禁后，可运行不带 Hint 的延迟物化 A/B：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode mysql-late-materialization-ab --providers mysql
```

该模式要求内层只完成稳定键分页、外层按当前页主键回表，并对两策略的总数、行数和
有序 ID 做严格等价检查。深页改善但筛选场景尾延迟不稳定时，禁止据此猜测 Offset 或
筛选分支阈值；应停止继续修补 OFFSET，转入显式 cursor API 规格。

显式 cursor API 落地后，运行双库生产等价 A/B：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode cursor-ab
```

`cursor-ab` 必须在相同深页边界成对交替采样旧端点的 COUNT＋OFFSET 与新端点的单次
keyset，校验返回行数和有序 ID 完全一致，并保留双库执行计划与原始样本。两类响应语义
不同：cursor 没有精确总数；只允许据此评价显式游标端点，禁止静默替换仍消费总数的
旧客户端。排序键、筛选规范化、UUID 物理语义或索引变化后必须重跑。

### Outbox 与 Jobs

Outbox 独立消费容量入口为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- outbox-capacity
```

默认使用 SQL Server/MySQL、并发 `1/2/4/8`、Handler 延迟 `0/10/100/1000ms` 和
副本 `1/2` 的核心矩阵；Batch/Payload 只在参考档组合，避免无界笛卡尔积。开发期必须
显式缩小列表、`--repetitions 1` 和采样时长；正式报告必须保留重复投递、续租、连接池、
锁等待、日志写入、GC、容器资源与期末 backlog，不能只看平均吞吐。默认额外执行一次
遗弃租约恢复场景；短开发验证可显式 `--recovery false`，正式证据不得关闭。长矩阵默认
开启 `--resume true`：每个完成键原子写入同一输出目录，后续任务窗口必须复用完全相同的
参数与构建版本；参数或源版本漂移时禁止续跑，避免把不可比较的样本合并。需要按任务窗口
主动分段时使用 `--max-new-samples <n>`；它只限制本次新增并已持久化的样本数，不计入
checkpoint skip，也不改变矩阵语义。
正式采样前必须先停止并排空预热消费者，再按预热实测速率、采样时长和安全余量补足
待处理消息；补量不得与正式采样争用数据库，也不得计入正式 Dapper、连接池或吞吐证据。
若期末积压归零，该样本只能作为夹具饥饿的否定证据，禁止据此评价容量或继续拼接旧
checkpoint。
容量报告还必须按稳定 `StatementName` 和低基数失败原因汇总非取消 Dapper 失败；
`deadlock`、`command_timeout`、`lock_wait_timeout`、`database_error` 与
`application_error` 用于定位瓶颈，不得写入 SQL、异常文本、租户或消息 ID。

分别在 SQL Server/MySQL 验证：

- 空队列、半批、满批和持续积压；
- 单 Worker 与多 Worker；
- 快/慢 Handler、瞬时失败、永久失败；
- 进程终止、租约到期、恢复和重复消费；
- Batch/Poll/Lease 参数矩阵。

Jobs backlog 聚合查询的可重复双库证据入口为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query
```

默认使用 10 万行固定分布数据、单并发、5 次预热和 30 次采样，并保存 SQL Server 实际
`STATISTICS XML`、MySQL `EXPLAIN FORMAT=JSON` 与 `EXPLAIN ANALYZE`。短开发验证可显式使用
`--rows 2000 --warmup 1 --iterations 5 --providers <provider>`；正式证据必须双库执行且保留默认
代表性规模。该入口直接消费 Jobs 生产 Statement，结果门禁覆盖 Host pending、到期重试、最老可领取
与最老到期时间；不同 Provider 的绝对耗时不得横向排名，也不能据此承诺生产 SLA。

Jobs backlog 候选索引的同库 A/B 入口为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --mode index-ab
```

该模式在每个 Provider 的单一容器和固定数据集内按
`baseline -> candidate -> candidate -> baseline` 镜像块采样，候选索引固定为
`IX_fn_jobs_execution_BacklogStatusTenant`。除 backlog 查询 P50/P95/P99 与实际计划外，
还必须记录索引创建耗时、索引体积以及生产 `trigger_insert`、`claim`、
`terminal_success` Statement 的事务回滚探针。短开发验证可使用：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-backlog-query --mode index-ab --rows 2000 --warmup 1 --iterations 5 --mutation-iterations 3 --providers <provider>
```

只有 SQL Server/MySQL 的查询 P95/P99 都严格改善、正确性和计划完整，且三类写路径
P95 回归均不超过 20%，才允许进入独立迁移切片；A/B 命令本身禁止修改正式迁移、
生产 SQL、Worker 默认并发或积压采样周期。

Jobs 并发容量的可重复入口为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- jobs-capacity
```

默认矩阵覆盖 SQL Server/MySQL、并发 `1/2/4/8`、Handler 延迟 `0/1000ms`，
并额外包含慢 Handler 的双副本 `c2` 形状；每档三轮。Runner 使用生产
`JobExecutionRunner`、固定低基数 JobKey、预热实测速率补量、原子 checkpoint 与
构建指纹隔离，报告终态吞吐、Handler/队列 P50/P95/P99、预期失败、重复尝试、续租、
Dapper 失败原因、连接池、数据库锁/日志、进程和容器资源。完整矩阵只允许通过
`.github/workflows/jobs-capacity.yml` 手工触发；本地只运行每个 Provider 一次短 smoke。
只有两库全部 c2 正确，吞吐中位数至少提升 20%、队列 P95 不回退、慢 Handler 有续租、
双副本正确且数据库失败为零，报告才可给出 `EligibleForCanaryAtTwo`。该结论只允许进入
独立 canary 决策，不得自动修改生产配置；证据不完整时默认 `MaxConcurrency = 1`。

### 前端

```powershell
pnpm --filter @fullnet/admin build
pnpm --filter @fullnet/admin-layui build
pnpm test:performance-governance
pnpm test:bundle-budgets
```

记录 Vite 输出的 JS/CSS minified 与 gzip；首屏静态依赖图和大体积延迟 chunk 分别设预算。发布环境额外记录 Brotli 和浏览器首屏瀑布。

## 仓库验证

```powershell
pnpm test:skills
pnpm test:governance
pnpm test:naming
dotnet build Full.NET.slnx -c Release
pnpm test:dotnet:unit -- --no-build
pnpm test:dotnet:compatibility -- --no-build
pnpm test:dotnet:architecture -- --no-build
```

测试数量、超时与分片只维护在
[`eng/testing/test-matrix.json`](../../../../eng/testing/test-matrix.json)，Skill
不得复制易变数字。本地数据库、认证、共享宿主、Outbox、缓存或 Dapper 基础设施变更按照 `rules/development-quality.md` 第 11.1 节运行 `test:integration:affected` 选择出的影响集；完整集合只由 `main` CI 并行分片执行。
