# Jobs 有界重试退避与抖动验证（2026-07-30）

## 状态与范围

- 状态：`Build-verified`
- 任务基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 新鲜任务快照：`jobs-retry-backoff-implementation-20260730`
- 范围：Jobs 显式可重试失败的固定/指数退避、有界最大延迟、可选对称抖动，
  以及 SQL Server/MySQL 三次尝试生命周期。
- 不包含：数据库结构或 SQL 变更、Cron/通用延迟任务、人工重放、容量收益、
  多 Worker 热点改善、生产告警阈值和完整 Integration 全量。

## 兼容配置契约

| 配置 | 默认值 | 启动期约束 |
| --- | ---: | --- |
| `MaxAttempts` | `1` | `1..10` |
| `RetryDelaySeconds` | `30` | `1..86400` |
| `RetryBackoffMode` | `fixed` | 精确小写 `fixed` 或 `exponential` |
| `RetryMaxDelaySeconds` | `86400` | `1..86400`，且不得小于基础延迟 |
| `RetryJitterPercent` | `0` | `0..50` |

默认配置仍是首次失败即终止；即使显式提高尝试次数，未配置新字段时也仍使用固定
30 秒且不抖动，因此现有部署不会隐式改变排期。

## 计算与调度语义

1. 指数模式使用数据库领取后的一基 `AttemptCount`：第 1 次失败使用基础延迟，
   第 2 次失败为两倍，之后继续翻倍。
2. 翻倍前先检查最大延迟，计算使用 `long` 并提前封顶，不允许整数溢出。
3. 抖动样本映射到 `[-percent, +percent]`，按
   `MidpointRounding.AwayFromZero` 取整，最终约束到
   `1..RetryMaxDelaySeconds`。
4. `SystemJobsRetryJitterSource` 只使用 `Random.Shared.NextDouble()`，不携带任务 ID、
   租户、异常、SQL 或指标标签。
5. Runner 仍复用既有 `jobs.reschedule_host_execution` Statement；普通异常、缺失
   Handler、宿主取消、租约所有权和尝试耗尽语义保持不变。

## RED / GREEN 证据

| 验证 | 新鲜结果 |
| --- | --- |
| Options RED | 新增默认值断言后出现 3 个预期 `CS1061`，仅缺三个配置属性 |
| Options GREEN | `JobsWorkerOptionsTests` 2/2 |
| 计算器 RED | 10 个预期 `CS0103`，仅缺 `JobsRetryDelayCalculator` |
| 计算器 GREEN | 固定、指数封顶、对称抖动 3/3 |
| Runner 行为 RED | 期望 `+144s`，实际仍为旧固定 `+30s` |
| Runner 行为 GREEN | 指数尝试 3、基础 30、抖动 20% 的固定样本得到 `+144s`，1/1 |
| 延迟指标 RED | 数据库排期已写入 `+144s`，但 `fullnet.jobs.retry.delay` 采样数为 0 |
| 延迟指标 GREEN | 仅在排期写回影响行数大于 0 后记录无标签 `144s`，1/1 |
| 全部 Jobs Unit | 47/47，失败 0，跳过 0 |
| Jobs SQL Server | 1/1；`+30s`、`+60s`、第 3 次终态 |
| Jobs MySQL | 1/1；`+30s`、`+60s`、第 3 次终态 |
| Docker teardown | 两个 Provider 均串行执行；测试进程、业务容器与 Ryuk 已退出，最终 `docker ps` 为空 |
| affected inner 计划 | 最终变更 12 个文件，目标 `Jobs, smoke`；inner 仅执行 smoke |
| affected slice | Integration Release build 0 警告/0 错误；smoke 8/8；Jobs 双 Provider 2/2 |

本切片新增 4 个 Unit 测试方法（计算器 3、Runner 1），没有新增 Integration 测试
方法；双库证据来自扩展既有共享 Jobs 生命周期断言。最终 canonical 数量须由后续矩阵
窗口对共享工作区做新鲜 Release discovery，本窗口不手算、不修改矩阵。

`jobs-retry-delay-telemetry-20260730` 增量快照复用了既有 Runner 测试方法验证实际排期
延迟直方图，因此测试方法数不变；该指标不修改数据库、API、配置或重试语义。

## 非结论

- 本验证只证明排期正确性，不证明退避或抖动已经提高吞吐、降低锁竞争或改善尾延迟。
- `MaxConcurrency` 默认值仍为 `1`，本切片不改变容量基线或此前容量 A/B 结论。
- 本地不运行 239 项完整 Integration；完整集合仍由 `main` CI 的互斥分片执行。
- 规则演进检查未命中用户纠正、重复失败、高风险新类别或规则冲突，不新增规则候选。
- Skill 演进检查未发现项目 Skill 缺口，不修改项目 Skill。
