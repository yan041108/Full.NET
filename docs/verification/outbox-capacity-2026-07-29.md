# Outbox 消费容量入口验证（2026-07-29）

## 1. 范围与结论

- 范围：Task 24 Step 2 的独立容量入口、矩阵约束、真实 Worker 装配、报告与双库单档冒烟。
- 基线：`b1472503ca59b4c601c8899602d229b30d269a8c` 加本记录对应工作区差异。
- 结论：SQL Server 与 MySQL 的慢 Handler 单档均通过正确性门禁；默认
  `OutboxWorker:MaxConcurrency = 1` 未调整。
- 未闭环：正式 35 档三轮采样、索引 A/B 和默认并发决策。

## 2. 实现边界

新增命令：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- outbox-capacity
```

默认核心矩阵遍历：

- Provider：SQL Server、MySQL；
- 单副本并发：`1/2/4/8`；
- Handler 延迟：`0/10/100/1000ms`；
- Worker 副本：`1/2`；
- 参考形状：Batch `20/100`、Payload `256/4096`。

核心容量矩阵与参考形状去重后为 35 档，而不是 256 档全笛卡尔积。每档默认三轮，
开发期可显式缩小所有列表并设 `--repetitions 1`。报告输出 JSON 与 Markdown，包含
吞吐、Handler P95/P99、重复投递、续租 SQL、Dapper、连接池、锁等待、日志写入、
GC/分配、容器 CPU/内存和期末 backlog。

## 3. 定向测试

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
  --filter FullyQualifiedName~OutboxCapacityContractTests --no-restore
```

结果：**9/9 通过**。其中宿主装配回归测试先复现缺少 `IIdGenerator` 导致
`ValidateOnBuild` 失败，再补齐与真实 Dapper Outbox Store 相同的依赖闭包。

Unit discovery 为 **520**，canonical 更新为 **520/7/49/199**。本地没有运行完整
199 项 Integration；它继续只由 `main` CI 四个互斥分片执行。

受影响选择器以 `b1472503ca59b4c601c8899602d229b30d269a8c` 为基线，只选择
共享 Worker Host Smoke，结果 **8/8 通过**，墙钟约 **1 分 23 秒**。

本次恢复增补以 `58442119b0d1a6fbbea3b53fa0963adc88b9cd17` 为任务基线。
`test:integration:affected:plan` 与 `test:integration:affected` 均判定
`local mode: none`、`affected targets: none`，因此没有启动 Integration 容器；
完整 199 项仍只由 `main` CI 四分片执行。

## 4. 双库真实冒烟

共同参数：并发 1、Handler 1000ms、单副本、Batch 20、Payload 256、预置 20 条、
无预热、采样 2 秒、租约 6 秒、续租 2 秒、重复 1 次。

| Provider | 版本 | 完成 | msg/s | P99 ms | 重复 | 续租 | 期末积压 | Dapper 失败 | 正确性 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| SQL Server | 16.0.4135.4 | 2 | 0.994 | 1010.107 | 0 | 1 | 18 | 0 | PASS |
| MySQL | 8.0.46 | 1 | 0.498 | 1013.542 | 0 | 1 | 19 | 0 | PASS |

两库连接池和数据库容器资源证据均完整，期末 backlog 均非零。后续复跑暴露测量结束时
取消续租 SQL 会被底层 `failures{outcome=canceled}` 统计为失败；基准聚合已将受控取消与
真实失败分栏，并增加 1 项 Unit 回归。该结果只证明入口与正确性链路，不构成吞吐比较或
默认并发调整证据。

原始工件位于本机临时目录，不提交仓库：

- `%TEMP%/fullnet-outbox-capacity-verified-2c8a8f7b66f446cabf2e472c7a2d72c9`

## 5. 下一步

1. 以正式时长运行 35 档三轮双库矩阵，并保留每档原始 JSON。
2. 只有执行计划和正式矩阵都提供证据时，才进入 pending 索引 A/B。
3. 默认并发继续保持 1，正式双库所有正确性与资源门禁通过前不得提高。

## 6. 遗弃租约恢复增补

恢复场景使用真实 Store 领取并遗弃单条租约，模拟 Handler 已产生副作用但进程在终态
确认前退出，再由真实 `OutboxProcessor` 轮询接管。共同参数为租约 5 秒、恢复余量
3 秒、Payload 256 字节、重复 1 次。

| Provider | 恢复 ms | MessageId | Attempts | 重复窗口 | Acquire SQL | Dapper 失败/取消 | 恢复后 pending | 门禁 |
|---|---:|---|---:|---:|---:|---:|---:|---|
| SQL Server | 5031.670 | 稳定复用 | 2 | 1 | 47 | 0/0 | 0 | PASS |
| MySQL | 5080.650 | 稳定复用 | 2 | 1 | 47 | 0/0 | 0 | PASS |

两库都没有早于租约边界重新投递，并在 3 秒余量内完成终态。该场景刻意保留一次重复窗口，
证明至少一次语义下 Handler 仍必须使用稳定 `MessageId` 去重或具备天然幂等性；它不声称
Exactly-Once。原始工件：
`%TEMP%/fullnet-outbox-recovery-gate-c298c713f4d84a3fa0ad56c8786efd24`。

## 7. 正式矩阵断点续跑增补

- 任务基线：`97bd04cb5b24ff906061952fa798b6c447b9a446`。
- 默认启用 `--resume true`，每完成一个普通场景或恢复轮次即原子替换报告。
- 续跑严格校验程序集源版本和全部矩阵语义参数；版本、时长、预热、种子、租约、Provider
  或场景漂移都会拒绝合并。
- `summary.md` 明确输出普通场景与恢复轮次的完成数，并区分 `PARTIAL` 与 `COMPLETE`。

真实 SQL Server 一档短跑使用同一输出目录连续执行两次。第一次启动容器并得到
`场景 1/1、恢复 0/0、COMPLETE`；第二次在容器启动前命中完成 checkpoint，输出
`checkpoint 已完成，跳过容器启动`，Docker 连接标记为 false。工件：
`%TEMP%/fullnet-outbox-checkpoint-smoke-082397a37bdc43d784144b19ec353251`。

该能力只减少长矩阵因中断造成的重复采样，不缩短单档预热/稳态时间，也不允许跨版本拼接
性能结论。正式 35 档三轮报告仍须达到 `COMPLETE` 后才能进入索引或默认并发决策。

## 8. 单次新增样本预算增补

- 任务基线：`20647377dc9de65e2bd39bdff490244c72ac0357`。
- `--max-new-samples 0` 保持无限制；正数必须与 `--resume true` 配合。
- 预算只统计本次新执行且已经原子写入 checkpoint 的普通场景或恢复样本；旧完成键的
  `checkpoint skip` 不计数。

真实 SQL Server 一档、两轮重复、每次最多新增 1 个样本的连续三次运行结果：

1. 第一次新增 1 个样本后正常退出，进度 `1/2`、状态 `PARTIAL`；
2. 第二次跳过第一轮、补齐第二轮，进度 `2/2`、状态 `COMPLETE`；
3. 第三次在连接 Docker 前跳过完整 Provider，容器启动标记为 false。

工件：
`%TEMP%/fullnet-outbox-budget-smoke-3d2cdd7c60ea4d12b5aee4d4527f2155`。
该预算只控制单次命令的工作量和正常停止点，不参与 checkpoint 的矩阵兼容签名，因此后续
窗口可调整批次大小，但不得改变任何性能采样参数。

## 9. 正式矩阵首批否定证据与失败归因

- 固定版本：`fe068967a549cd94aa19e36fb45eb2785cb297a9`。
- 默认 SQL Server 矩阵先完成普通样本 `5/210`；单副本三轮均通过，双副本前两轮均因
  非取消 Dapper failure 未通过正确性门禁，因此该 checkpoint 只保留为否定证据。
- 双副本同参数独立复现多轮后，失败稳定归属于 `outbox.mark_processed`；四副本放大场景
  一轮出现 3 次，低基数原因均为 `database_error`。没有重复投递、积压排空、续租失败、
  命令超时或已确认的 deadlock 证据。
- 容量工件现在同时保存 `failureStatements` 与 `failureReasons`，并收集 Worker
  Warning/Error 的单行异常摘要；指标不写入 SQL、参数、租户、消息 ID 或异常文本。
- TDD 先分别以缺少 Processor 日志收集器、失败语句聚合和失败原因聚合得到 RED，再完成
  GREEN；Outbox 容量与 Dapper 聚焦 Unit **12/12** 通过，Unit discovery **520**。
- 以 `fe06896` 为基线的受影响选择器只命中共享 BuildingBlock smoke，结果 **8/8**；
  本地没有运行完整 199 项 Integration。

代表性诊断工件：

- `%TEMP%/fullnet-outbox-capacity-formal-fe06896`
- `%TEMP%/fullnet-outbox-diagnose-statements2-fe06896`
- `%TEMP%/fullnet-outbox-failure-reason-r4-fe06896`

结论：正式矩阵暂停扩跑，默认并发继续保持 `1`。下一步必须捕获 SQL Server
`outbox.mark_processed` 的具体数据库错误边界并建立回归测试；修复后从新固定提交和新
输出目录重新采样，禁止继续合并 `fe06896` checkpoint。
