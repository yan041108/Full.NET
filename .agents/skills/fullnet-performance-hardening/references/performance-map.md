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

分别在 SQL Server/MySQL 验证：

- 空队列、半批、满批和持续积压；
- 单 Worker 与多 Worker；
- 快/慢 Handler、瞬时失败、永久失败；
- 进程终止、租约到期、恢复和重复消费；
- Batch/Poll/Lease 参数矩阵。

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
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 519
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49
```

测试数量变化时先同步 canonical 门槛，再使用新数字。本地数据库、认证、共享宿主、Outbox、缓存或 Dapper 基础设施变更按照 `rules/development-quality.md` 第 11.1 节运行 `test:integration:affected` 选择出的影响集；完整 199 项只由 `main` CI 并行分片执行。
