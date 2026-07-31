# Jobs 并发容量入口验证

## 范围与结论

- 基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`jobs-concurrency-capacity-20260730`
- 入口：`jobs-capacity`
- 本地范围：SQL Server/MySQL 各一次 `c1/c2` 短 smoke
- 正式范围：仅 `.github/workflows/jobs-capacity.yml` 手工 CI

独立容量入口已能直接运行生产 `JobExecutionRunner`，并记录持续积压、终态吞吐、
Handler/队列 P50/P95/P99、预期失败、重复尝试、续租、Dapper、连接池、数据库、
进程和容器资源。短 smoke 只证明入口与正确性门禁可执行，不证明容量收益。

**`Jobs:Worker:MaxConcurrency` 默认值继续保持 `1`；容量收益尚未通过完整双库矩阵验证。**

## 证据合同

默认手工矩阵为：

- Provider：SQL Server、MySQL；
- 并发：`1/2/4/8`；
- Handler 延迟：`0/1000ms`；
- 基础形状：单副本；
- 额外形状：慢 Handler、并发 `2`、双副本；
- 每档三轮，预热 10 秒、采样 30 秒；
- 固定 8 个低基数 JobKey，其中 1 个为预期失败键；
- Batch 16、租约 30 秒、续租 5 秒。

Runner 在预热后停止并排空消费者，再按实测速率和 1.5 倍安全余量重建正式积压。
采样结束后停止领取新批次并等待在途批次排空。只有下列条件同时成立，单档正确性才
通过：

- 数据库终态数等于 Handler 调用数，失败终态等于预期失败数；
- Handler/队列延迟证据均存在，样本数与对应终态一致，且分位值有限、非负、单调；
- 无 `AttemptCount > 1`、Running 残留或终态租约残留；
- 期末仍有 Pending，证明采样窗口未耗尽积压；
- Dapper 失败/取消和 Runner 意外错误均为 0；
- 连接池、进程、数据库和容器资源证据完整且数值有效。

报告建议还要求配置中的 Provider、场景和重复序号形成精确、唯一、完整的样本键集合。
报告即使总样本数符合预期，只要存在重复键、缺失键或越界键，也必须标记为 `PARTIAL`
并保持 `KeepConcurrencyOne`，不得用其它样本的漂亮结果提前给出 canary 建议。

本地缩短 smoke 即使键集合完整、正确性和 c2 阈值全部通过，也没有 canary 决策资格。
评估器只接受至少覆盖默认两库、`1/2/4/8`、`0/1000ms`、单/双副本、三轮、
10 秒预热、30 秒采样和既定 Batch/Handler/租约形状的手工矩阵；缩短时长、重复次数或
场景目录一律保持 `KeepConcurrencyOne`。增加时长、样本数或额外场景不会降低该门禁。

`EligibleForCanaryAtTwo` 还要求两库全部 c2 样本正确、吞吐中位数相对 c1 至少提高
20%、队列 P95 不回退、慢 c1/c2 都有续租、慢双副本 c2 正确。报告只给出证据建议，
不会写配置，也不会推荐高于并发 `2`。

## TDD 与构建

| 验证 | 结果 |
| --- | --- |
| Options/场景目录 RED | 缺少 `JobsCapacityOptions`、`JobsCapacityScenarioCatalog` |
| 积压/统计 RED | 缺少 `JobsCapacityBacklogPlanner`、`JobsCapacityStatistics` |
| Handler/Probe RED | 缺少容量 Handler 与预期失败类型 |
| 评估门禁 RED | 缺少 `JobsCapacityRunResult`、`JobsCapacityAssessment` |
| DI RED | ValidateOnBuild 暴露缺失 `IIntegrationEventSerializer`，补齐 MessagePack 注册 |
| Report/checkpoint RED | 缺少原子报告和构建指纹隔离 |
| 完整性 RED | 评估器缺少 Options/Scenarios 输入，无法判断配置矩阵是否完整 |
| 完整性 GREEN | 同为 54 个样本但含 1 个重复键、缺 1 个键时保持并发 1 |
| 决策矩阵 RED | 两库缩短 smoke 完整且正确时错误返回 `EligibleForCanaryAtTwo` |
| 决策矩阵 GREEN | 缩短 smoke 保持 `KeepConcurrencyOne`，完整默认矩阵仍具备评估资格 |
| 运行标量 RED | 零采样时长仍被 `CorrectnessGatePassed` 接受 |
| 运行标量 GREEN | 零/非有限时长、非有限或非正吞吐、负 drain 全部失败关闭 |
| 延迟证据 RED | 缺失 Handler 延迟仍被 `CorrectnessGatePassed` 接受 |
| 延迟证据 GREEN | 缺失、非有限、样本数错配或分位顺序损坏全部失败关闭 |
| 资源证据 RED | 缺失进程快照仍被 `CorrectnessGatePassed` 接受 |
| 资源证据 GREEN | 缺失/损坏进程快照、数据库采集错误或快照顺序异常全部失败关闭 |
| 运行时资源 RED | 连接池 active 为 `NaN` 时，伪造的完整标志仍被门禁接受 |
| 运行时资源 GREEN | 损坏连接池数值/错误标志、零容器样本、非有限或倒序 CPU 统计全部失败关闭 |
| Jobs capacity Unit | **15/15**，失败 0、跳过 0 |
| Benchmark Release build | **0 warning / 0 error** |
| `jobs-capacity --help` | 退出码 0，正确输出容量帮助 |

## 双库短 smoke

两库串行使用：

```text
concurrency=1,2
handler-delay-ms=50
replicas=1
repetitions=1
warmup=1s
duration=2s
seed-jobs=64
batch-size=8
handler-keys=4
failing-handler-keys=1
```

| Provider | 场景 | 终态 | 期末 Pending | 失败/预期失败 | Running | 正确性 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| SQL Server | c1-d50-r1 | 32 | 32 | 8/8 | 0 | PASS |
| SQL Server | c2-d50-r1 | 56 | 56 | 14/14 | 0 | PASS |
| MySQL | c1-d50-r1 | 32 | 32 | 8/8 | 0 | PASS |
| MySQL | c2-d50-r1 | 56 | 56 | 14/14 | 0 | PASS |

工件位于：

- `.tmp/jobs-concurrency-capacity-20260730/smoke-sqlserver`
- `.tmp/jobs-concurrency-capacity-20260730/smoke-mysql`

每个命令均退出 0，逐档 `CorrectnessGatePassed=true`；两库容器与 Ryuk 最终
`RUNNING_COUNT=0`。短 smoke 没有 1000ms 慢 Handler、续租或双副本形状，且两库分开
成报告，因此 assessment 按设计保持 `KeepConcurrencyOne`。

## 未完成项

- 尚未从同一冻结构建运行手工 CI 的完整双库 `1/2/4/8` 三轮矩阵；
- 未取得两库可重复的 c2 吞吐和队列尾延迟收益；
- 未经独立 canary 决策，不得提高任何生产环境默认并发。
