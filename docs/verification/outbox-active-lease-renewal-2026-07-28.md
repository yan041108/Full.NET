# Outbox 主动续租验证（2026-07-28）

## 1. 范围与基线

- 基线提交：`1b2f358`（`perf: establish hardening foundation`）
- 分支：`codex/performance-hardening`
- Provider：SQL Server / MySQL Testcontainers
- 目标：关闭“整批消息使用同一初始租约，慢 Handler 与串行批尾可能在处理完成前被其他
  Worker 回收”的已确认可靠性缺口。
- 非目标：不改变至少一次投递、Attempts、重试、Dead Letter、默认并发 `1`，不宣称
  Exactly-Once，也不提高 Worker 默认吞吐参数。

## 2. RED 证据

第一阶段只增加配置、处理器和失败传播测试。Release 构建以 7 个预期编译错误失败：

- `OutboxWorkerOptions` 缺少 `LeaseRenewalSeconds`；
- `IOutboxStore` 缺少 `RenewLeaseAsync`。

补齐最小配置与 Store 契约、尚未修改 Processor 时，`OutboxProcessorTests` 为
**12/14**：

- `ProcessOnceAsync_RenewsLeaseWhileHandlerIsRunning` 观测到零次续租；
- `ProcessOnceAsync_WhenLeaseRenewalFailsCancelsHandlerAndPropagatesFailure` 因 Handler
  未被取消而超时。

失败只指向续租行为缺失，不来自数据库、容器或测试发现数。

## 3. 实现与不变量

- 新增 `OutboxWorker:LeaseRenewalSeconds`，默认 `10` 秒，有效范围 `1..1200`，且不得
  超过 `LeaseSeconds / 2`。
- `IOutboxStore.RenewLeaseAsync(messageIds, lockId, lease, ...)` 使用固定参数化 SQL，
  先按精确批次主键集合定位，再匹配相同 `LockId`、`ProcessedAtUtc IS NULL`、
  `DeadLetteredAtUtc IS NULL`；避免按未索引 LockId 周期扫描积压表。
- `LockedUntilUtc` 使用双库通用 `CASE` 单调延长，宿主时钟回拨不会缩短已有租约；
  MySQL 连接策略固定为 matched-row 计数，避免同值更新误报零行。
- Processor 同时运行批次处理与续租循环。串行路径继续复用原批次 Scope；显式并发路径
  继续为每条消息创建独立 Scope；每次续租另建 Async Scope、Host 租户上下文和数据库
  会话，不与 Handler 共享连接。
- 批次完成后通过 linked token 有界停止续租。续租先失败时取消协作式 Handler，等待其
  退出后传播原始续租异常，不把基础设施租约故障改写成普通业务重试。
- 最后一条终态写入与零行续租竞争时，使用单调事件序号记录“终态完成”和“续租失败”；
  只有终态序号更早时才以最终成功的处理结果为准。续租先失败时，即使 Handler 在取消后
  返回成功，也必须传播原租约异常；处理分支先失败时，清理阶段的续租异常不得覆盖原始
  处理异常。两种完成顺序均有确定性单元回归。
- 同一领取批次必须共享一个 `LockId`。单元夹具原先为同批两条假消息生成不同锁标识，
  已修正为符合真实 Store 契约；生产不变量未放宽。

## 4. 双库批尾场景

SQL Server/MySQL 使用相同场景：

1. 插入两条同路由消息；
2. 以 `BatchSize = 2`、`MaxConcurrency = 1`、`LeaseSeconds = 6`、
   `LeaseRenewalSeconds = 1` 领取；
3. 第一条 Handler 进入后阻塞，第二条保持在串行批尾；
4. 将应用时钟推进到初始租约之后，等待数据库中两条租约均被延长；
5. 将应用时钟回拨一分钟并显式续租，数据库截止时间不得缩短；
6. 恢复测试时钟后由第二个 Scope 再次 `AcquireAsync(2, ...)`，必须返回空集合；
7. 释放第一条 Handler，最终两条均只执行一次、`Attempts = 1` 且进入成功终态。

该场景证明主动续租同时保护活跃 Handler 与尚未开始的批尾消息，而不是只延长当前单条。

## 5. 聚焦验证

| 验证 | 结果 |
| --- | --- |
| `OutboxProcessorTests` | **19/19**，失败 0、跳过 0 |
| `MySqlConnectionStringPolicyTests` | **15/15**，失败 0、跳过 0 |
| SQL Server/MySQL 批尾续租与时钟回拨 | 最终代码 **2/2**，失败 0、跳过 0，`1m04.304s` |
| MySQL 独立连续复跑 | 最终代码 **3/3**，每轮 **1/1**，`47.094s` / `48.150s` / `48.235s` |
| Integration 项目 Release 构建 | **0 warning / 0 error** |

首轮最终双库测试为 **1/2**：SQL Server 返回 `DateTimeOffset`，新增测试夹具构造参数误写
为 `DateTime`；堆栈精确指向 Dapper 物化，改正夹具类型后原命令 **2/2**。MySQL 首轮已
通过，生产 SQL 与续租状态机未因该夹具问题调整。

代码审查随后发现原布尔终态标志只能表达最终状态，不能表达终态与续租失败的先后顺序：
续租调用已失败、但 Async Scope Dispose 尚未结束时，终态可能先被处理器观察，故障会被
误判为正常零行续租。第一轮 TDD RED 以缺少 `OutboxLeaseCompletionOrder` 的 1 个预期
编译错误失败；加入序号后的进一步评审用阻塞 Scope Dispose 稳定复现“期望
`OutboxLeaseLostException`，实际无异常”。故障标记下沉到续租调用边界后该场景 GREEN。
第二轮 RED 复现续租先失败、处理随后也失败时错误保留后到的处理异常；最终 GREEN 将续租
失败、处理完成和终态完成统一放入 `Interlocked` 单调序列，并在 renewal-first 与
processing-first 两个分支按实际先后判定异常优先级。第三轮对称竞态 RED 以缺少统一决策
API 的 1 个预期编译错误失败；两个分支最终统一复用
`ShouldPreserveProcessingOutcome`，避免 `Task.WhenAny` 的观察顺序覆盖事件真实顺序。
修正后 `OutboxProcessorTests` 为 **19/19**。

## 6. 完整验证

| 门禁 | 新鲜结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release --no-restore` | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **461/461**、**7/7**、**49/49**，失败 0、跳过 0 |
| 完整 Integration | **193/193**，失败 0、跳过 0，`32m44.276s` |
| Governance / Performance Governance | **11/11**、**3/3** |
| Naming / SQL Safety | **23/23**、**5/5** |
| Integration tooling / partitions | **4/4**；`35 + 35 + 62 + 61 = 193`，无遗漏或重复 |
| 项目 Skills | 契约 **52 + 33**；两个项目 Skill `quick_validate.py` 均通过 |
| `git diff --check` | 通过 |

## 7. 语义边界与后续门禁

- 主动续租缩小正常运行期间的重复消费窗口，但进程崩溃、网络分区、数据库长时间不可用和
  外部副作用完成后终态确认失败仍可能重复；Handler 必须幂等。
- 默认 `MaxConcurrency = 1` 保持不变。提高并发必须等待综合计划 Task 24 的双库容量矩阵。
- 本次不新增迁移或索引。续租已使用主键 `IN` 精确定位；真实持续积压下的连接池占用、
  锁等待和并发容量仍进入 Task 24。
- 现有 `rules/performance-engineering.md` 与 `fullnet-performance-hardening` 已覆盖租约、
  独立 Scope、有界并发和双库门禁；未出现需要新增近义规则或 Skill 的重复缺口。
