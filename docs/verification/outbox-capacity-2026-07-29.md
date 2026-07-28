# Outbox 消费容量入口验证（2026-07-29）

## 1. 范围与结论

- 范围：Task 24 Step 2 的独立容量入口、矩阵约束、真实 Worker 装配、报告与双库单档冒烟。
- 基线：`b1472503ca59b4c601c8899602d229b30d269a8c` 加本记录对应工作区差异。
- 结论：SQL Server 与 MySQL 的慢 Handler 单档均通过正确性门禁；默认
  `OutboxWorker:MaxConcurrency = 1` 未调整。
- 未闭环：正式 35 档三轮采样、进程终止后的恢复时间、索引 A/B 和默认并发决策。

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

结果：**5/5 通过**。其中宿主装配回归测试先复现缺少 `IIdGenerator` 导致
`ValidateOnBuild` 失败，再补齐与真实 Dapper Outbox Store 相同的依赖闭包。

Unit discovery 为 **516**，canonical 更新为 **516/7/49/199**。本地没有运行完整
199 项 Integration；它继续只由 `main` CI 四个互斥分片执行。

受影响选择器以 `b1472503ca59b4c601c8899602d229b30d269a8c` 为基线，只选择
共享 Worker Host Smoke，结果 **8/8 通过**，墙钟约 **1 分 23 秒**。

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
2. 增加受控进程终止场景，量化租约到期后的恢复时间和重复投递。
3. 只有执行计划和正式矩阵都提供证据时，才进入 pending 索引 A/B。
4. 默认并发继续保持 1，正式双库所有正确性与资源门禁通过前不得提高。
