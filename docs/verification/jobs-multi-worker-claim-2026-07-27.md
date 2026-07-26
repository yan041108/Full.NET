# Jobs 多 Worker 原子领取验证（2026-07-27）

## 目标

为既有 Host Jobs Worker 增加可重复的双数据库并发门禁，验证多个 Worker
同时轮询一批待执行任务时，不会重复领取、遗漏任务或遗留运行中租约。

本任务只强化已有租约实现的验证，不修改生产运行时、数据库对象或公共 API。

## 并发场景

SQL Server 与 MySQL 各自在既有
`Host_job_definition_and_trigger_follow_contract` 用例内执行同一场景：

1. 复用通过 API 创建且仍处于启用状态的 `jobs.ping` 定义；
2. 写入 32 条 `pending` 执行记录；
3. 创建 4 个独立依赖注入作用域，每个作用域解析自己的
   `JobExecutionRunner`；
4. 通过同一个异步起跑信号同时释放 4 个 Worker，每个 Worker 的批大小为
   8；
5. 断言 4 个 Worker 均领取 8 条，处理总数恰好为 32；
6. 回查全部执行记录，断言状态均为 `succeeded`、`AttemptCount = 1`、
   `LeaseId`/`LeaseExpiresAtUtc` 已清空且 `FinishedAtUtc` 已写入。

并发门禁继续复用既有 SQL Server/MySQL 两项 Jobs Integration，用例总数与
canonical 门槛不变，避免为了同一数据库生命周期重复执行完整迁移。

## 自动化证据

| 门禁 | 结果 |
| --- | --- |
| Integration Release 构建 | 0 warning / 0 error |
| Jobs SQL Server/MySQL 聚焦 | **2/2**，失败 0、跳过 0，**45.880s** |
| Architecture | **49/49**，失败 0、跳过 0 |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189**，无遗漏或重复 |
| Governance | **11/11** |
| Project Skill 契约 | **52** 项 |
| Workspace | 通过 |

首轮聚焦运行在最终状态回查处发现 33 条记录，而并发场景只种入 32 条。根因是
前置 API 流程已在同一任务定义下创建 1 条手动执行记录；4 个 Worker 的处理总数
仍恰好为 32，并非重复领取。测试夹具随后改为保留本场景写入的 32 个执行 ID，
最终状态只对这组 ID 断言；生产代码与领取 SQL 未变。

## 能力边界

该门禁证明当前双库领取 SQL 在确定性并发竞争下保持单次领取语义，不等同于
生产容量或性能基准。大规模多 Worker 压力、长耗时处理器的租约续期策略、
Cron/延迟调度、失败分类与运维重放仍是后续能力。

## 关联

- [Jobs Host 任务定义验证](jobs-host-definitions-2026-07-26.md)
- [能力状态](../roadmap/capability-status.md)
