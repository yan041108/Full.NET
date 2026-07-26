# 日志退出共享预算验证记录

- 日期：2026-07-26
- 初始开发基线：`5cd2e72`
- 最终同步基线：`cc501fefa2021cf3224b2980c1a34a8321f9b550`
- 分支：`codex/bounded-logging-shutdown`
- 范围：`Full.NET.Hosting` 普通/高优先级日志退出排空、共享总预算、配置校验
- 批准依据：总体架构硬化计划 Task 8B1

## 实现边界

Full.NET 使用自有 `FullNetLoggingPipelineSink` 管理两个 `FullNetBoundedAsyncSink`。运行期间两条固定容量队列由独立后台线程并行消费，写入只使用非阻塞 `TryAdd`；`Error/Critical` 继续与普通日志隔离。

Logger 释放时同时停止两条队列接收新事件并开始排空。高优先级 Worker 先获得等待权，普通 Worker 只能使用同一截止时间的剩余预算，因此总等待不会退化为“两条通道各等待一次超时”。`FullNet:Logging:ShutdownFlushTimeout` 默认 5 秒，只允许大于 0 且不超过 30 秒。

预算到期后只清空尚未进入 Sink 的内存事件并累计丢弃数。已经进入阻塞 Sink 的事件不使用线程中止或同步网络回退；后台线程不会阻止进程退出。该实现不提供磁盘持久化、跨重启重放或外部投递确认。

## TDD 证据

| 阶段 | 结果 |
| --- | --- |
| 隔离工作树基线 | 原有 `HighPriorityLoggingTests` **6/6** 通过 |
| RED 1 | 新测试首次构建因 `LoggingOptions.ShutdownFlushTimeout` 缺失失败，准确证明配置契约尚不存在 |
| RED 2 | 最小配置契约与范围校验加入后，旧 `Serilog.Sinks.Async` 实现仅有 `Logger_disposal_uses_one_total_timeout_for_both_blocked_channels` 失败；双 Sink 阻塞时 1 秒观察窗内未释放 |
| GREEN | 自有双通道调度器实现后，日志聚焦与既有 Monitor 回归 **10/10**，失败 **0**、跳过 **0** |
| 退出故障注入 | 两个 Sink 同时阻塞且共享预算为 100ms 时，Logger 释放在单一预算边界后返回；测试随后释放 Sink，未遗留前台线程 |
| 正常排空 | Logger 写入一条普通日志和一条 Error 后立即释放，两条事件均在预算内到达对应 Sink |
| 配置边界 | `00:00:00` 与 `00:00:31` 均在 Service Defaults 注册阶段被拒绝，错误包含稳定属性名 `ShutdownFlushTimeout` |

## 当前验证

| 门禁 | 结果 |
| --- | --- |
| `dotnet build src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj -c Release --no-restore` | 通过，警告 **0**、错误 **0** |
| 日志聚焦 Unit | **10/10**，失败 **0**、跳过 **0** |
| 预同步 Release 全解决方案 | 通过，警告 **0**、错误 **0** |
| 预同步 Unit / Compatibility / Architecture | **389/389**、**7/7**、**49/49**，失败与跳过均为 **0** |
| 预同步 Naming / Governance / Integration tooling | **23/23**、**11/11**、**4/4** |
| 预同步 Project Skills | `fullnet-module-delivery` **52** 项合同检查通过 |
| 共享 `main` 最终同步 | 已快进到 `cc501fe`；保留 Outbox、Notifications、OpenAPI 与 Realtime 的代码、文档和门槛证据，6 个纯文档冲突已按最终 canonical 合并 |
| 最终 Release 全解决方案 | 通过，警告 **0**、错误 **0** |
| 最终 Unit / Compatibility / Architecture | **395/395**、**7/7**、**49/49**，失败与跳过均为 **0** |
| OpenAPI / breaking | **58/58**；相对 `main` 的冻结夹具 **25/25** 兼容 |
| Governance / Project Skills / Naming | **11/11**、`fullnet-module-delivery` **52** 项合同检查、**23/23** |
| workspace / Integration tooling / partitions | 通过；**4/4**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189**，无遗漏或重复 |
| 最终完整 Integration | **189/189**，失败 **0**、跳过 **0**，**30m21s**，stderr 为 **0** |

## 未完成项

完整 Task 8 仍缺磁盘 Spool、外部可靠 Sink、平台不可用、磁盘满、跨重启重放与投递确认故障注入。这些能力必须先通过 ADR 明确容量、保留、加密、磁盘满策略与重复投递语义；在此之前日志能力继续标记为 `Build-verified`，不能标记为 `Verified`。

## 规则与 Skill 复盘

- 规则：无变化。现有 `development-quality.md` 第 9 节已覆盖日志容量、非阻塞、独立高优先级通道与审计持久化边界；本次没有出现跨任务重复遗漏或新的安全事故证据。
- Skill：无变化。本切片没有形成新的、至少跨两类任务复用的项目特有复杂工作流，既有项目 Skill 也没有暴露真实缺口；完整发布验证仍由现有脚本、门槛与 `fullnet-release-verification` 自动化候选持续治理。
- 代码审查：受当前协作约束未派生子代理，已对生产差异、线程竞态、释放幂等性、配置边界、测试清理和文档契约执行结构化本地审查；未发现 Critical 或 Important 问题。
