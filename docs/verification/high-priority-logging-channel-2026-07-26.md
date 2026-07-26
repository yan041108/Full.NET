# 日志高优先级独立通道验证记录

- 日期：2026-07-26
- 基线：本地 `main`，起始提交 `8f18362`
- 范围：`Full.NET.Hosting` Serilog 双通道、低基数指标、ready 降级检查、三个官方宿主共享注册
- 批准依据：总体架构硬化计划 Task 8；本切片仅关闭 Task 8A

## 实现边界

`Information/Warning` 与 `Error/Critical` 分别进入独立的 Serilog.Sinks.Async 有界队列。两条通道均固定 `blockWhenFull: false`；历史兼容配置 `BlockWhenFull=true` 会在启动时失败。高优先级队列达到容量 90% 时，`high_priority_logging` ready 检查返回 `Degraded`。

Meter `Full.NET.Logging` 保持三个指标名不变，并增加固定 `channel=general|high_priority` 标签：

- `fullnet.logging.queue.depth`
- `fullnet.logging.queue.capacity`
- `fullnet.logging.events.dropped`

审计数据不进入日志队列，继续由业务数据库事务或 Outbox 持久化。本切片没有实现磁盘 Spool、外部 Sink 投递确认或进程退出有界刷新。

## TDD 证据

| 阶段 | 结果 |
| --- | --- |
| RED | 新增 `HighPriorityLoggingTests` 后，Release 构建因 `FullNetLoggingMonitors` 缺失而失败；失败原因精确指向尚未实现的双通道能力 |
| GREEN | 实现双通道路由、监控与健康检查后，高优先级聚焦 **6/6**；连同既有 `FullNetAsyncLogMonitorTests` 为 **7/7** |
| 过载故障注入 | 普通 Sink 阻塞并填满容量时，Error 在普通 Sink 释放前由独立通道交付 |
| 慢 Sink 故障注入 | 高优先级 Sink 阻塞并填满容量时，256 次写入在 2 秒边界内返回并累计丢弃 |
| 指标/健康 | 只出现 `general`、`high_priority` 两种标签；普通通道过载不影响 ready，高优先级达到 90% 返回 `Degraded` |

## 新鲜验证

| 门禁 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release` | 通过，警告 **0**、错误 **0** |
| Unit | **386/386**，失败 **0**、跳过 **0** |
| Compatibility | **7/7**，失败 **0**、跳过 **0** |
| Architecture | **49/49**，失败 **0**、跳过 **0** |
| Naming | **23/23** |
| Project Skills | **52** 项合同检查 |
| Governance | 门槛审计同步前预期失败 **1**；同步后 **11/11** |
| Integration tooling | **4/4** |
| Integration 最终全量 | **184/184**，失败 **0**、跳过 **0**，耗时 **31m00s**，stderr **0** |

## Integration 环境诊断

首次全量运行发现 Docker Desktop Service 处于 `Stopped`，184 项中 172 项以 `DockerUnavailableException` 失败，12 个无容器测试通过。启动 `com.docker.service` 与 Docker Desktop 并确认 Engine `29.6.1` 后，首轮预热全量为 **183/184**；唯一失败是 `MySql_super_administrator_migration_recovers_partial_state` 在资源竞争下命令超时。该用例精确复跑 **1/1** 通过，随后最终全量 **184/184** 通过。没有为环境故障修改业务或测试语义。

## 审查与未完成项

按架构级变更清单复核了计划一致性、路由互斥、非阻塞语义、指标基数、DI 释放、健康状态、审计边界和运维文档。受当前协作约束影响未派生代码审查子代理；本地审查发现并修正了计划中仍引用 `options.BlockWhenFull` 的文档偏差，未发现 Critical 或 Important 代码问题。

Task 8B 仍需完成磁盘 Spool/外部可靠 Sink、磁盘满、平台不可用和进程退出有界刷新演练。因此能力状态只能标记为 `Build-verified`，不能标记为 `Verified`。

## 规则与 Skill 复盘

- 规则：本次没有达到新增规则门槛；独立 Error/Critical 容量、非阻塞与审计边界已由 `rules/development-quality.md` 第 9 节覆盖。
- Skill：更新既有 `fullnet-release-verification` 自动化候选为第 11 次证据；Docker readiness 与冷启动分类适合进入 Integration preflight/脚本，不新增判断型 Skill。
