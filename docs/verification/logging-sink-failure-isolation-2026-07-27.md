# 日志 Sink 单事件故障隔离验证记录

- 日期：2026-07-27
- 初始基线：`5efe8f93e76ec88766ad18f878976b1d87815699`
- 分支：`codex/logging-sink-failure-isolation`
- 范围：`Full.NET.Hosting` 普通/高优先级日志内部 Sink 失败传播、事件级异常隔离与 dropped 计数
- 批准依据：总体架构硬化计划 Task 8B

## 根因与实现边界

`FullNetLoggingPipeline` 原先使用普通 `WriteTo` 子 Logger 包装实际 Sink。Serilog 会在该边界吞掉 Sink 异常并写 SelfLog，导致后台 Worker 不知道事件已投递失败：后续事件虽然继续处理，但 `fullnet.logging.events.dropped{channel}` 保持零，监控产生假绿。

本切片把后台 Worker 与实际 Sink 之间的内部包装改为 `AuditTo`，使失败传播到 Full.NET 自有边界；`FullNetBoundedAsyncSink` 再按单条事件捕获异常、递增该通道 dropped 并继续消费。Audit 只表达内部 Sink 失败传播，不改变业务审计、请求线程非阻塞、双通道容量或退出预算。

降级 SelfLog 只包含异常 CLR 类型，不包含异常消息、日志正文或事件属性。失败事件不会重入队列，避免连续失败形成无限循环。

## TDD 与系统化调试证据

| 阶段 | 结果 |
| --- | --- |
| 隔离工作树基线 | Release Unit 项目构建 0 warning / 0 error；`HighPriorityLoggingTests` 实际 **9/9**，旧计划中的 10 项已过时 |
| 初始 RED | 普通与高优先级两个新用例都成功送达第二条事件，但 dropped 实际为 **0**；证明问题不是 Worker 已退出，而是失败不可观测 |
| 根因验证 | 仅将内部包装从 `WriteTo` 切换为 `AuditTo` 后，两个用例都因第二条事件在 2 秒内未送达而失败；证明异常已传播，且现有消费循环会在外层 catch 后终止 |
| GREEN | 将 catch 收紧到单条事件后聚焦 **11/11**，失败 0、跳过 0；两个通道均丢弃首条失败事件、投递第二条事件，dropped 各为 **1** |

## 当前验证

| 门禁 | 结果 |
| --- | --- |
| `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore` | 通过，警告 **0**、错误 **0** |
| 日志聚焦 Unit | **11/11**，失败 **0**、跳过 **0** |
| 当前树 Release | `dotnet build Full.NET.slnx -c Release --no-restore` 通过，警告 **0**、错误 **0** |
| 当前树测试 | Unit **402/402**、Compatibility **7/7**、Architecture **49/49**，失败 **0**、跳过 **0** |
| 当前树静态门禁 | Governance **11/11**、Project Skills **52 checks**、Naming **23/23**、workspace、OpenAPI **58/58**、OpenAPI breaking **25/25**、Integration tooling **4/4** 均通过 |
| 当前树 Integration 分区发现 | **35 + 35 + 62 + 57 = 189**，四分区发现门禁均通过 |
| 最终主线复验 | 等待 Jobs、Files、IdentityOptions 前序队列同步最新 `main` 后执行 |
| 完整 Integration | Hosting 共享基础设施触发全量；等待前序 Docker 队列释放后执行 |

## 未完成项

本切片不提供磁盘 Spool、容量/保留/加密、连续平台不可用持久化、磁盘满处置、跨重启重放或外部投递确认。后续 Task 8B 必须明确至少一次语义及重复事件处理，不能把本次“Worker 继续运行”描述成日志已经可靠交付。
