# Jobs 重试指标与 Worker 异步 Scope 验证（2026-07-30）

## 状态与范围

- 状态：`Build-verified`
- 任务基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`jobs-retry-telemetry-20260730`
- 范围：Jobs 已持久化状态转换、重试排期与重试耗尽的低基数 OpenTelemetry 指标，以及 Worker 根 Scope 的异步释放。
- 不包含：待处理深度与年龄快照、生产告警阈值、容量或吞吐收益、Cron、通用延迟调度、退避抖动和人工重放。

## 行为结论

Jobs 模块注册 `Full.NET.Jobs` Meter，并暴露以下单调 Counter：

| 指标 | 标签 | 记录边界 |
| --- | --- | --- |
| `fullnet.jobs.execution.transitions` | `outcome=succeeded|failed|retry_scheduled` | 对应数据库状态更新成功且影响行数大于 0 |
| `fullnet.jobs.retry.scheduled` | 无 | 可重试失败成功写回 `pending` |
| `fullnet.jobs.retry.exhausted` | 无 | 可重试失败达到总尝试次数并成功写入 `failed` |

指标不携带 JobKey、ExecutionId、TenantId、异常类型、异常消息、SQL 或 URL。租约所有权丢失导致状态更新影响
0 行时不记录，避免把未提交状态误报为执行结果。Meter 监听器或导出器异常被隔离在观测旁路，不能反转已经
提交的任务状态或终止 Worker 轮询。

独立 Worker 接入 Realtime handler 后，启动路由校验首次解析到包含仅支持 `IAsyncDisposable` 的 `DbSession`
依赖图。原同步 `CreateScope()` 在退出时稳定触发释放异常；Worker 的启动校验和一次性版本退役扫描现均使用
`await using ... CreateAsyncScope()`，与 Worker 处理器、Migrator 和集成夹具的生命周期模式一致。

## TDD 与根因证据

1. 指标 RED：`JobExecutionRunnerTests` 聚焦 9 项中 4 项按预期失败，实际计数均为 0。
2. 观测旁路 RED：临时移除监听器异常隔离后，成功路径 1/1 被模拟监听器异常击穿。
3. 租约丢失 RED：让终态命令返回 0 行后，失败路径 1/1 观察到错误增加的指标。
4. 指标 GREEN：成功、普通失败、重试排期、重试耗尽、监听器异常和 0 行更新边界聚焦 9/9 通过。
5. Worker Scope RED：源级生命周期检查发现 `Program.cs` 存在 2 处同步根 Scope；真实 SQL Server Worker
   启动在释放 `DbSession` 时抛出 `InvalidOperationException`。
6. Worker Scope GREEN：两处均改为异步 Scope 后，源级检查为 2/2 async、0 sync；Notifications 的独立
   Worker 真实栈在 SQL Server 与 MySQL 均完成启动、Outbox 消费和正常 teardown。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| Jobs Unit 聚焦 | 13/13，失败 0，跳过 0 |
| Jobs SQL Server | 1/1，失败 0，跳过 0 |
| Jobs MySQL | 1/1，失败 0，跳过 0 |
| Jobs 模块 Release 构建 | 0 警告、0 错误 |
| Worker Release 构建 | 0 警告、0 错误 |
| Architecture | 49/49 |
| Naming | 23/23 |
| Governance | 16/16 |
| 测试工具契约 | 31/31 |
| Worker 双库真实栈旁证 | SQL Server Vue/Layui 2/2；MySQL Vue/Layui 2/2 |
| Docker 串行协调 | Jobs 双库结束后 `docker ps` 为空并已释放给其它窗口 |

本切片没有新增测试方法或迁移。收尾时共享矩阵的新鲜门槛为 Unit 662、Infrastructure 80、Full 226，
且 migration selection 保留 037；本窗口未覆盖这些由其它切片共同维护的数值。

## 未验证项与声明边界

- 没有运行完整 226 项 Integration；完整集合仍由 `main` CI 的互斥并行分片门禁运行。
- 任务快照之后存在其它窗口写入，affected inner 计划因此扩展到共享变更；本窗口只执行 Jobs 聚焦影响集，
  没有重复运行其它窗口的 CodeGeneration 或 Notifications 集合。
- 本切片只补可观测性与生命周期正确性，不声明延迟、吞吐、数据库往返或资源占用得到改善。
- Counter 不能反推当前积压；数据库快照型待处理深度、最老待处理年龄、最老到期重试年龄及生产告警阈值仍待补。
- 规则演进检查未命中重复失败、高风险新类别或规则冲突；不新增规则候选。
- Skill 演进检查未发现项目 Skill 缺口；不修改 Skill。
