# Audit 同步写入尾延迟归因

## 1. 状态与范围

- 计划任务：Task 22 Step 1–2，以及 Step 3 的最小候选与短时 A/B；正式容量矩阵仍待执行。
- 执行时间：2026-07-29（Asia/Shanghai）。
- 代码基线：`15ada1b408aed12dd4fa687437f8bf510c78e83f` 加本记录对应工作区差异。
- 范围：真实 Testing API Host、JWT、读请求、事务 Outbox 写请求、异常探针、
  Access/Operation/Exception 三类 Audit INSERT、SQL Server/MySQL。
- 第 1–6 节保留初始“只测不改”证据；第 7 节记录随后批准的可靠性分类、生产候选和验证结果。

本轮为混合负载基准增加 `audit-write` 归因 workload。Benchmark Host 通过测试专用
`ICommandExecutor` 装饰器读取逐请求 profile，分别执行
`none/access/operation/exception/all`。生产 Host 不注册该装饰器，也不读取基准 Header，
因此不存在生产关闭 Audit 的配置入口。

五个 profile 在同一 Host、同一数据库和同一并发档中按 Worker 确定性轮转。每个请求原始
样本记录 profile；报告同时校验：

1. 场景推导的预期 Audit INSERT 次数；
2. 装饰器观测的实际尝试、失败与写库 P95；
3. 三张 Audit 表在采样窗口前后的行数增量；
4. 端到端 P50/P95/P99、Dapper 总命令数、连接池、容器资源和锁等待。

任一 profile 缺样本、预期与观测不一致、写入失败或数据库行数不一致，证据门禁均失败。

## 2. RED/GREEN 与故障记录

契约测试分三轮先失败后实现：

- profile、workload、三类 INSERT 白名单缺失时，Unit 项目以 27 个缺失符号失败；
- profile 轮转与写入耗时归因缺失时，以 2 个缺失类型失败；
- profile 端到端归因缺失时，以构造函数和归因器缺失失败。

首次双库冒烟在预检阶段稳定返回
`audit-access-operation-exception 期望 500，实际 405`。根因是旧请求构造器把全部
`Write` 场景固定生成为 PUT，而 Testing 异常探针只接受 POST。回归契约先要求异常场景
显式声明 `POST`，随后为所有场景增加稳定 `RequestMethod`，请求构造器不再从读写分类
猜测 HTTP Method。修复后相同冒烟命令通过。

## 3. 命令与环境

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore

dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll `
  --filter "FullyQualifiedName~Full.NET.UnitTests.Performance.MixedLoadContractTests" `
  --minimum-expected-tests 19

dotnet benchmarks/Full.NET.Benchmarks/bin/Release/net10.0/Full.NET.Benchmarks.dll `
  mixed-load `
  --providers sqlserver,mysql `
  --concurrency 4 `
  --warmup-seconds 1 `
  --duration-seconds 10 `
  --workload audit-write `
  --audit-write-profiles none,access,operation,exception,all `
  --output .tmp/audit-write-task22-smoke-v2
```

- .NET：`10.0.9`
- Windows：`10.0.19045`
- 逻辑处理器：20
- Docker Desktop 内存：31.22 GiB
- SQL Server：`mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04`，
  `16.0.4135.4`
- MySQL：`mysql:8.0`，`8.0.46`

`.tmp/audit-write-task22-smoke-v2` 是本机未纳入版本控制的原始工件目录；本文件保留可审计
摘要。10 秒采样只用于校验测量链路和方向，不是正式容量结论。

## 4. 双库冒烟结果

| Provider | 请求 | QPS | P95 ms | P99 ms | 非预期错误 | Dapper 失败 | Audit A/O/E 行增量 | 锁等待次数/ms | 证据 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- |
| SQL Server | 2,362 | 235.99 | 29.998 | 36.738 | 0 | 0 | 944/720/241 | 769/2,451 | PASS |
| MySQL | 1,904 | 189.67 | 42.165 | 52.001 | 0 | 0 | 762/582/190 | 0/0 | PASS |

| Provider | Profile | 请求 | P95 ms | P99 ms | Access 预期/观测 | Operation 预期/观测 | Exception 预期/观测 |
| --- | --- | ---: | ---: | ---: | --- | --- | --- |
| SQL Server | none | 472 | 20.446 | 38.288 | 0/0 | 0/0 | 0/0 |
| SQL Server | access | 473 | 28.688 | 40.737 | 473/473 | 0/0 | 0/0 |
| SQL Server | operation | 473 | 27.019 | 36.661 | 0/0 | 361/361 | 0/0 |
| SQL Server | exception | 473 | 20.474 | 26.742 | 0/0 | 0/0 | 117/117 |
| SQL Server | all | 471 | 33.402 | 38.974 | 471/471 | 359/359 | 124/124 |
| MySQL | none | 379 | 23.055 | 30.080 | 0/0 | 0/0 | 0/0 |
| MySQL | access | 380 | 36.725 | 41.895 | 380/380 | 0/0 | 0/0 |
| MySQL | operation | 381 | 35.769 | 42.278 | 0/0 | 292/292 | 0/0 |
| MySQL | exception | 382 | 23.827 | 32.299 | 0/0 | 0/0 | 93/93 |
| MySQL | all | 382 | 51.938 | 57.459 | 382/382 | 290/290 | 97/97 |

相对 `none`，`all` 的短样本端到端 P95 增量为 SQL Server `12.956ms`、MySQL
`28.883ms`；P99 增量分别为 `0.686ms`、`27.379ms`。SQL Server P99 增量过小，
且单项 profile 存在负增量，说明 10 秒样本仍受场景分布与系统噪声影响，不能用于冻结
生产预算。

在 `all` profile 中，SQL Server 的一/二/三次串行写路径分别出现
`112/235/124` 次，MySQL 为 `92/193/97` 次。异常场景确实形成
Exception → Operation → Access 三次串行持久化，且两库预期次数、装饰器观测和最终表行数
完全一致。

三类单次 INSERT P95 约为：

| Provider | Access ms | Operation ms | Exception ms |
| --- | ---: | ---: | ---: |
| SQL Server | 8.994 | 9.331 | 9.101 |
| MySQL | 15.838 | 17.625 | 14.547 |

在加入数据库行数一致性门禁后的最终代码状态上，又执行了 `1s` 预热、`3s` 采样、并发 `4` 的双库重放：
SQL Server 完成 `671` 个请求，MySQL 完成 `492` 个请求，均为 `0` 个非预期错误；两者的
`auditWriteAttributionComplete`、总证据门禁和预算门禁均为 `PASS`。该重放只证明最终实现与证据门禁可执行，
不替代上表的方向性样本，也不作为正式容量结论。

## 5. 结论与停止条件

1. 已闭环 Task 22 的测量基础设施和双库正确性冒烟；基准可以在一个共享竞争环境内量化
   单类与组合 Audit 写入，不需要为每个 profile 重启容器。
2. “最多三次串行写”不是静态推测，已由真实异常请求、稳定 Statement 次数和数据库行数
   三重证据确认。
3. 短样本支持“同步 Audit 是 MySQL 尾延迟的重要组成部分”这一后续假设；SQL Server
   同时出现明显锁等待，仍需正式矩阵判断 Audit 与其他写竞争的占比。
4. 当前证据不授权修改可靠性语义。Operation 和安全 Exception 继续按可靠 Audit 候选；
   Access 是否可丢仍须由 Spec 明确。不得据此改为 fire-and-forget 或进程内无界队列。

## 6. 未验证项

- 尚未执行预热 30 秒、稳态 600 秒、并发 `1/4/16/32` 的双库正式矩阵。
- 尚未冻结 Access 的可靠性分类，也未对同事务批处理、同命令批处理或事务 Outbox 候选
  进行单变量 A/B。
- 本轮没有生产代码、SQL 或数据库对象变更，因此没有新的 Auditing Integration 影响集；
  正式候选实现后仍必须运行对应 SQL Server/MySQL 聚焦 Integration。

## 7. 请求内批处理候选（2026-07-29）

可靠性分类已由
[`2026-07-29-auditing-write-reliability-classification.md`](../superpowers/specs/2026-07-29-auditing-write-reliability-classification.md)
冻结：Access 是请求遥测；Operation 与安全相关 Exception 是不可采样的同步数据库审计摘要，
并补充业务领域审计而不替代其事务语义。三类请求级记录都不进入进程内队列，也没有生产关闭
或降采样开关。

实现使用请求作用域固定三槽收集 Access/Operation/Exception，在最外层协调 Middleware 的
`finally` 中使用 `CancellationToken.None` 提交。非空组合映射为七个固定、参数化、全局 Scope
的 Statement；一次显式 `ICommandTransaction` 内只调用一次 `ICommandExecutor`。任一 INSERT
失败时整批回滚、记录 Warning，并保持既有“不得用审计数据库故障替换业务响应”的兼容语义。
空批次不打开事务。该原子性只覆盖同一请求的三张 Audit 表，不宣称与任意业务事务原子提交。

生产代码先由五项失败契约锁定固定容量、空批次、单事务单命令、失败不逃逸和客户端断开后的
最终提交，再实现最小 GREEN；随后补充一类、两类精确组合的两项防漂移覆盖。基准侧增加一项
契约，将七个批量 Statement 展开回既有三类稳定观测名；基准专用捕获策略只注册在 Benchmark
Host，生产 Host 不读取 profile Header。

### 7.1 受影响验证

| 验证 | 结果 |
| --- | --- |
| Release Unit build | 0 warning / 0 error |
| `AuditingWritePathTests` | **7/7**，失败 0、跳过 0 |
| `MixedLoadContractTests` | **20/20**，失败 0、跳过 0 |
| affected Integration plan | `focused: Auditing`，未升级为完整 193 项 |
| SQL Server/MySQL Auditing Integration | 查询/API **6/6**；真实半批回滚 **2/2**；影响集共 **8/8** |

### 7.2 双库短时 A/B

相同机器、并发 4、预热 1 秒、采样 3 秒、同一
`none/access/operation/exception/all` workload 的前后结果如下。短样本只证明方向和证据链
可执行，不用于冻结生产容量预算。

| Provider | 实现 | QPS | 总体 P95 ms | 总体 P99 ms | all P95 ms | all P99 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 三次独立同步写 | 222.06 | 30.301 | 36.473 | 33.543 | 38.730 |
| SQL Server | 请求内单事务单命令 | 233.06 | 28.359 | 33.553 | 29.577 | 37.716 |
| MySQL | 三次独立同步写 | 162.88 | 45.234 | 101.537 | 78.878 | 281.212 |
| MySQL | 请求内单事务单命令 | 185.26 | 41.744 | 50.654 | 42.666 | 48.401 |

候选双库均为 0 个非预期错误，五种 profile 的预期/观测次数完全一致，
`auditWriteAttributionComplete`、总证据门禁和预算门禁均为 PASS。短样本中 SQL Server
总体 P95 改善约 6.41%，MySQL 改善约 7.72%；MySQL `all` P95/P99 明显收敛，支持保留候选。
此外，SQL Server/MySQL 各通过一项真实事务回滚测试：第二条 Operation INSERT 触发非空约束
失败后，第一条已执行的 Access INSERT 行数仍为 0。正式
`30s/600s × c=1/4/16/32` Task 20 矩阵尚未执行，因此 Task 22 Step 3 不标记完成。
