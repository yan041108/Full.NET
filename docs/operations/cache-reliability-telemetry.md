# 缓存可靠性指标与多实例告警

本文描述 `Full.NET.Caching.Reliability` Meter 的生产观测与告警基线，对应
[缓存可靠性验证记录](../verification/cache-reliability-telemetry-2026-07-26.md)。

2026-10-13 将 FusionCache 核心、Redis Backplane、OpenTelemetry 与 SystemTextJson 四包统一升级为 2.9.0，带入 StackExchange.Redis 2.10.1。此次版本升级不启用 Eager Refresh、分布式锁或扩大 Fail-Safe 策略。项目注册路径的回归确认，`Clear(false)` 后已逻辑过期的 L2 值不再作为 Fail-Safe 回退；明确允许 `Clear(true)` 时仍可合法降级。升级前 2.6.0 已复现相反行为，不能把修复后的结果外推到旧节点。该单元实验使用内存 L2；真实 Redis 与原生验证状态见[统一计划](../superpowers/plans/2026-10-03-cache-hardening.md)。官方变更依据：[2.9.0](https://github.com/ZiggyCreatures/FusionCache/releases/tag/v2.9.0)、[2.8.0 标签行为修复](https://github.com/ZiggyCreatures/FusionCache/releases/tag/v2.8.0)。

2.9.0 的 `JitterMaxDuration` 需要分层解释：SDK 为 L1 的到期增加抖动，物理 L2 到期不加这项抖动；S0-L2 禁用 L1 后，不能依靠此字段打散共享层过期。正常值与 null 负缓存的独立 Hybrid 实验分别观察到 L1 在基础 TTL 后仍命中、随后在 TTL 加抖动的观察窗口内过期，而存储边界收到的 L2 物理 TTL 保持基础值。这里的窗口是实验范围，不是生产 SLA。Fail-Safe 的实际保留还须结合 L1 抖动和刷新行为核验，不能把配置字段直接等同业务绝对陈旧期限。依据：[2.9.0 分层到期实现](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/FusionCacheEntryOptions.cs)、[抖动开关](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/Internals/FusionCacheInternalUtils.cs)。正式回归及真实 Redis/Native 的当前状态见[统一计划](../superpowers/plans/2026-10-03-cache-hardening.md)。

## 指标清单

缓存容量瓶颈诊断另观察 `fullnet.db.connection.phase.duration`（准入、打开、持有；秒）与 `fullnet.data.sql.command.duration`（取得连接后的命令准备、执行和结果消费；毫秒）。前者按固定访问模式区分授权读取，后者按稳定 SQL 语句区分；两者均不携带用户、租户或原始 SQL。阶段边界、缺失处理及均值限制见[分段诊断说明](../../eng/load/cache-capacity/README.md#连接与命令阶段诊断)。新增观测仍待匹配 .NET/Native 验收与双库实测，不能据此声称吞吐提升。

| 指标 | 标签 | 含义 |
| --- | --- | --- |
| `fullnet.cache.invalidation.duration` | `scope=local\|distributed`、`outcome=success\|failure` | 单次失效耗时 |
| `fullnet.cache.invalidation.failures` | 同上 | 失效失败计数 |
| `fullnet.cache.hits.stale` | 无 | FusionCache Hit 事件的 `IsStale`，通常表示 SDK Fail-Safe 返回过期值；不等于业务版本陈旧 |
| `fullnet.cache.backplane.circuit.transitions` | `state=open\|closed` | Backplane 熔断状态转换 |
| `fullnet.cache.backplane.recoveries` | 无 | 熔断恢复次数 |
| `fullnet.cache.deserialization.rejections` | 无 | 全局 L2 序列化输入在 JSON 解析前因字节超限而被拒绝的次数 |
| `fullnet.cache.runtime.events` | `layer`、`operation`、`result` | 当前节点实际 SDK 的层命中、工厂、Fail-Safe 与 L2 熔断事件，不推断业务版本 |
| `fullnet.cache.invalidation.batch.duration` | `outcome=success\|failure` | 批次实际耗时，单位为毫秒 |
| `fullnet.cache.invalidation.batch.targets` | `kind=unique\|duplicate` | 接纳批次中的唯一目标和重复目标数量，不等于成功删除数量 |
| `fullnet.cache.invalidation.batch.rejections` | `reason=invalid_input\|capacity\|timeout` | 整批输入、接纳或期限拒绝，不能解释为整批执行成功 |

标签禁止包含租户、域名、缓存键或异常文本，以保持低基数。

容量采样另使用 Dapper 的 `fullnet.data.sql.completed_operations`（Prometheus 出口 `fullnet_data_sql_completed_operations_total`）记录操作尝试总量，标签只有稳定的 `statement_name`、`provider`、`operation`。它包含成功、失败和取消，也包含在准入阶段失败的操作，不等于数据库端实际执行次数。原 SQL 执行/失败/耗时指标仍保留结果与失败分类；首次新增失败系列时，分类计数的前置缺失不能补零。新总量按两个 API 各自真实前后基线计算，缺失、重置或系列消失仍不可验收。配方、兼容与验证范围见[容量驱动说明](../../eng/load/cache-capacity/README.md#sql-总量基线)；新增实现尚不能证明旧 Native 发布物已导出此指标或容量通过。

上述表格列出 .NET Instrument 名，不是 Prometheus 出口名。`fullnet.cache.invalidation.duration` 的单位为 `ms`，当前 Collector 实际导出的直方图族为 `fullnet_cache_invalidation_duration_milliseconds`，告警须查询 `_milliseconds_bucket`，不能使用 `_ms_bucket`。Remote Write 默认也附加单位/类型后缀，见[固定版本出口说明](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.123.0/exporter/prometheusremotewriteexporter/README.md#getting-started)。原 500 毫秒阈值和十分钟持有期保持；这个耗时仅包含删除/发布，不代表远端确认或业务最新。

失效指标由统一 `FusionCacheInvalidator` 记录，每次键删除或标签失效各记一次；租户批次、管理控制面不额外记录同名耗时/失败指标。租户准入走权威读取，其写入不再清理无读者的 Redis 条目，也不产生缓存失效样本。`fullnet.cache.policy.events` 的失效事件同样在统一入口记录，`consistency_class` 使用登记类别，操作为 `invalidate_local` 或 `invalidate_distributed`；原调用方的 `invalidate_after_commit` / `invalidate_admin_*` 标签值不再产生，相关仪表盘须同步调整。

当前 SDK stale 指标不能检测“TTL 尚未过期但数据库已更新”的业务旧值。Backplane 发布成功、收到消息、完成本地删除和读到最新业务版本也是四种不同事实；事件计数不能当作全节点确认。熔断转换计数为历史事件，不能在没有状态证据时推断当前连接健康或恢复队列已经清空。

恢复队列是尽力而为的补偿。FusionCache 2.9.0 到达 `AutoRecoveryMaxItems` 时，会按到期时间淘汰已有目标或拒绝新目标；数量检查、淘汰和插入不是原子操作，并发反复提交可超过该阈值，不能把它当作严格条目数或内存硬上限，也不能假定超额最多等于并发数。重试次数耗尽会移除目标，遗漏失效仍依赖条目 TTL 或权威读取收敛。严格内存预算需要独立证据，当前未验收。SDK 关闭时清空队列并取消后台任务，不承诺待处理消息排空或远端确认。滚动停机不能把正常退出当作全部节点缓存已更新的证据，强一致类别继续禁用 L1 或走权威源。依据：[2.9.0 恢复队列与关闭实现](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/Internals/AutoRecovery/AutoRecoveryService.cs)。

Redis Pub/Sub 不持久化断连期间的通知，也不为重连订阅者重放历史消息。SDK 本机恢复队列不能改写这一传输合同；恢复后的新发布成功也不能证明所有断连节点都已清除旧 L1。恢复验收须分别检查实际订阅、一次新的业务失效及另一节点读到的新版本，漏掉的旧通知继续依赖条目 TTL 或权威源兜底。依据：[Redis 投递语义](https://redis.io/docs/latest/develop/pubsub/#delivery-semantics)。

同键工厂合并的默认范围是单个 FusionCache 实例。当前项目没有注册分布式回填锁；多个 API 同时冷启动或失效时仍可能各自回源，不能把本机 `GetOrCreateAsync` 的合并描述为全局只查询一次。C0/N0 直接权威读取也不能用旧缓存命中来合并安全决定。分布式锁仍按统一计划的条件项评估，不因短 TTL 自动引入。依据：[2.9.0 实例内存锁及可选分布式锁](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/FusionCache.cs)。

当前资源选项须区分项目覆盖值与 SDK 默认值：

| 边界 | 当前值与范围 | 解释 |
| --- | --- | --- |
| Redis 连接、同步/异步命令与 L2 hard timeout | `Cache:RedisOperationTimeout` 默认1秒，允许1毫秒至30秒 | 不是整个请求或所有重试的总期限 |
| 提交后失效清理 | `Cache:InvalidationTimeout` 默认5秒，允许1毫秒至1分钟 | 独立于已取消的原请求；不能代表远端节点完成确认 |
| 恢复队列 | `Cache:AutoRecoveryMaxItems` 默认1000，允许1至100000 | 并发下为淘汰阈值，不是内存硬上限 |
| 每项恢复重试 | `Cache:AutoRecoveryMaxRetryCount` 默认3，允许1至100 | 耗尽后可丢弃，不保证可靠投递 |
| L2/Backplane SDK 熔断 | 两个 duration 默认均为0 | 当前项目未覆盖，表示默认不开启SDK熔断；指标存在不代表已启用 |
| SDK 恢复处理延迟 | 默认5秒 | 当前项目未覆盖；故障实验的2秒配置不代表生产默认值 |

项目范围由 `CacheOptions` 与启动验证器约束；SDK 未覆盖值依据[固定版本选项](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/FusionCacheOptions.cs)。现有故障实验验证的是所记录配置与规模，不能把这些默认值称为生产实测最优预算。关闭积压专项及当前完整 Native 的未验收状态见[集中验收记录](../verification/2026-10-14-cache-concentrated-acceptance.md)。

`distributed-cache` ready 检查同时使用受管理 L2 GET 和现有 SDK Backplane 的实际结果。初始状态、重连后缺少新的成功证据为 Degraded（HTTP 200），不会仅因重复检查升级为 Unhealthy。订阅或发布已失败时，首次检查 Degraded、连续第二次 Unhealthy（HTTP 503）；L2 GET 成功不会清除该失败。订阅完成或收到有效消息可以恢复订阅侧，只有真实发布成功才能恢复发布侧；SDK 熔断计时闭合、连接建立或收到消息均不能清除发布失败。两侧成功且 L2 GET 成功后才 Healthy。

该观察复用 SDK 已注册的 Backplane，不新增连接、独立订阅 Worker 或探针广播。重连使旧成功证据失效；操作开始序号和实例代际隔离迟到结果及已退役回调。请求取消不记为 Redis 故障，描述不包含原异常、键或载荷。它是被动观察，不会主动测试每一项 ACL，也无法立即发现空闲期间尚未触发操作的权限变更或静默断连。冷启动或长期没有真实发布的实例可持续 Degraded；不能为消除该状态频繁发送探针广播，更不能将其解释为已证明全节点失效。

Grid 偏好的 S2 合同允许迟到回填：请求已读到旧快照、随后写操作提交并删除缓存、旧请求最后完成时，旧值仍可能重新进入 L1/L2，并由另一节点读到。`Version` 用于保存时的冲突检测，当前不承担缓存回填栅栏。两台实例各自过期，某一台恢复不能证明全部恢复。默认 L1 一分钟、L2 十五分钟并不代表提交后一分内最新；评估窗口必须包含在途回源耗时、回填后的 L2 寿命、L2 再次进入 L1 的寿命与抖动，开启 Fail-Safe 还须另计显式保留期限。没有在途读取耗时上界时，不给出从事务提交起算的固定最大陈旧时间。需要写后权威读取的部署可将该条目配置为 C0/N0，不能依赖增发一次通知达到该合同。

配置中的时长精度与 Redis 物理键精度应分别核对。当前 `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.10 的 `GetExpirationInSeconds` 把绝对过期差值截断为整秒，再调用 Redis 过期命令，因此物理 TTL 可能比配置值短不足一秒；页面保留亚秒时长不代表共享存储具有相同精度。依据：[10.0.10 RedisCache 实现](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Caching/StackExchangeRedis/src/RedisCache.cs)。实际过期验收须同时观察物理键、剩余 TTL、正常/null 命中和到期回源，不能只检查选项或页面字段；本轮实跑状态见[统一计划](../superpowers/plans/2026-10-03-cache-hardening.md)。

## 编排与导出

缓存管理控制面的 Grid 操作经 Settings 所有者契约执行，ObservabilityAdmin 的模块依赖包含 Settings。官方预设已经包含两者；自定义 `FullNet:Modules` 裁剪配置启用 ObservabilityAdmin 时也必须启用 Settings，不能用可选事件依赖代替同步服务依赖。

策略目录包含抖动、负缓存寿命、Fail-Safe 开关及最大保留、版本复核、业务载荷预算和运行状态。当前前后端契约要求字段齐全，可空预算的 null 表示不适用，时长保留亚秒精度。租户为 `authority_read`，不提供失效操作；诊断策略为 `local_snapshot`，不显示虚构 L1/L2 TTL，只提供本机刷新、快照年龄及最大年龄。`snapshot_loaded` 表示已加载本机快照，`safe_default` 表示安全默认，后者不能作为权威加载成功的证据。其他节点通过各自权威轮询收敛，刷新响应不是全节点确认。真实 FusionCache 的 `observing`/`unknown` 只描述当前节点 SDK 事件观察生命周期，不代表 Redis 健康或数据最新。

L1 使用专属 `Cache:L1PayloadCapacity` 预算（默认 10000，允许 1–1000000 个权重单位），治理载荷权重为 1。SDK 标签元数据保留零权重及保护优先级，避免容量淘汰损坏失效语义；这不是字节或总进程内存上限。SDK 依据：[FusionCache 2.9.0 标签默认策略](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/src/ZiggyCreatures.FusionCache/FusionCacheOptions.cs)。`fullnet.cache.runtime.events` 新增 `layer=l1, operation=eviction`，结果为 capacity/expired/removed/replaced/token_expired/unknown；不携带键、用户或租户标签。容量压力下必须同时观察回源率、P99、托管堆和进程内存，不能仅凭淘汰计数判定容量达标。

1. 每个 API/Worker 副本通过 OpenTelemetry 导出上述 Meter；与
   `fullnet.outbox.*` backlog 指标共用同一采集链路。
2. 多副本部署时，按 `service.instance.id` 聚合，告警规则使用 `max` 或
   `sum` 前确认指标语义（失效失败为计数、duration 为直方图）。
3. Redis Backplane 与 FusionCache L2 共用连接串时，ready 探针与缓存失效
   共用同一 Redis；Realtime 专用 Backplane 单独探针，见
   [Realtime Redis 运维说明](./realtime-redis-backplane.md)。

## 建议告警阈值（初版）

| 条件 | 严重级别 | 动作 |
| --- | --- | --- |
| `fullnet.cache.invalidation.failures` 5 分钟内 `distributed`+`failure` > 0 持续 3 个周期 | Warning | 检查 Redis 连通、Backplane 熔断与 TTL/版本兜底窗口 |
| `fullnet.cache.hits.stale` 持续上升 | Warning | 核对是否为已允许的 S2 Fail-Safe，检查权威源错误与最大陈旧预算；不能仅凭此指标断言权限/租户陈旧 |
| `fullnet.cache.backplane.circuit.transitions` `open` 在 10 分钟内 ≥ 2 | Warning | 核对 Redis 负载、网络分区与连接池 |
| Backplane 熔断开启且 `fullnet.cache.invalidation.failures` 同步上升 | Warning | 跨节点失效广播受阻；先恢复 Redis，再核对 TTL/版本是否按预期收敛 |

阈值需在真实多副本 soak 后按环境调参；本表为编排补证基线，不替代容量测试。

## 分级策略

| 等级 | 典型数据 | Fail-Safe | 跨节点修复 |
| --- | --- | --- | --- |
| C0 权威安全决定 | 租户准入、活动状态与域名绑定 | 禁止 | 直接查询权威源；旧缓存删除只用于升级兼容 |
| S0-L2 共享缓存 | 经明确一致性设计允许的共享投影 | 禁止 | 禁用 L1 读写；提交后同步删除 L2，并保留权威兜底 |
| S1 业务读多 | 字典、配置只读投影 | 禁止 | 显式删除 L1/L2 + Backplane，或短 TTL 收敛 |
| S2 可丢 | 演示/统计缓存 | 可评估 | 允许仅 L2 提前收敛 |

新增缓存必须声明等级并在模块 Spec 中记录失效点；不得默认 Fail-Safe 用于授权。

## 诊断策略快照

`settings.diagnostic-policy` 的运行状态由 Settings 所有的权威快照持有，请求热路径读取本进程快照。后台每 30 秒读取权威配置；刷新等待和查询使用 `Cache:InvalidationTimeout` 的取消预算，默认 5 秒。`Cache:DiagnosticSnapshotMaxAge` 默认为 1 分钟，支持 30 秒至 5 分钟；轮询停滞导致快照超龄、时钟回拨或权威查询失败时回退安全默认。临时规则到期后不再参与采样和容量覆盖，不能依赖下一次 Redis 删除来撤销它。

管理控制面删除兼容 Redis 文档后，通过 Settings Port 刷新接收操作的本节点，返回的 `snapshot:settings.diagnostic-policy:current-node` 仅表示本节点快照刷新。其他节点仍按各自权威轮询收敛；共享层删除或 Backplane 发布都不表示其快照已经更新。后台刷新不回填从未被运行路径读取的旧 Redis 诊断文档。

当前双库实验使用两个独立 API 宿主、真实共享 Redis 和独立时钟。轮询验证周期为 100ms，只证明周期入口能够收敛；它不测量生产 30 秒调度时延或给出传播 SLA。完整结果和源码绑定见[统一计划](../superpowers/plans/2026-10-03-cache-hardening.md)。

## 验证

Grid 的 `MaxSerializedBytes` 约束源生成 JSON 编码后的业务载荷字节数，等于上限仍允许缓存；不包含 FusionCache 的封装元数据。超限权威结果可以返回给本次请求，但不回填 L1/L2；配置收紧后命中的超限旧值会被拒绝并回源。此检查发生在对象加载之后。

另有 `Cache:MaxDeserializationBytes` 全局解析前保护，默认 1048576 字节（1 MiB），允许 1 字节至 67108864 字节（64 MiB）。JIT 与 AOT 注册的序列化器在解析 SDK 传入的字节数组前检查总长度，等于上限允许解析，超限抛出 `CacheDeserializationLimitException`；异步入口先检查请求取消。该上限独立于条目业务载荷预算，不按 CLR 类型推断条目归属；写入仍委托原序列化器，缓存格式不变。Redis SDK 缓冲区在此之前可能已分配，因此不能视为网络读取或总内存的硬上限。

Native AOT 的 L2 元数据必须覆盖 SDK 实际传入的 `FusionCacheDistributedEntry<T>`，只登记业务 DTO 不足以支持 Redis 写入。基础设施拥有标签/清空时间戳的 `long` 包装，Settings 仅登记真实缓存消费者 Grid 的包装，诊断文档只保留权威存储 JSON 元数据；使用独立的 General 源生成上下文保持 SDK 默认格式，直接 DTO 继续使用原 Web 上下文。新增缓存载荷时由所有者登记具体包装闭包，并验证 JIT/Native 双向可读；禁止反射兜底或仅凭 DTO round-trip 宣称 Redis 可用。原生运行和当前发布输入的证据以[统一计划](../superpowers/plans/2026-10-03-cache-hardening.md)为准。

本轮 linux-x64 发布后的实际外部进程验收已通过两库 Grid 场景与启动/停止共 3 项，0 跳过：冷节点真实读取 Redis SDK 信封、建立 L1，并在业务提交后于测试窗口内观察到第二节点失效。它验证当前 Grid 缓存闭包，不替代完整 Native 门禁、其他载荷或故障恢复/容量验收。

旧 L2 内容超过新上限时会被拒绝；部署前核对合法历史载荷，并在所有节点配置一致的上限，覆盖业务载荷与 SDK 序列化封装空间。Grid 明确捕获该拒绝（含 SDK 包装），最佳努力清理后读取权威源；其他调用方仍遵循自身 SDK 异常/降级策略，拒绝不能作为合法空值。无标签拒绝计数不含键、租户、异常文本或载荷内容，观察器异常不覆盖原拒绝。2026-10-15 的双库 Native 两项已实际验证精确1MiB/多一字节、多字节 JSON、拥有者条目预算及权威回退不回填，日志按既有脱敏合同核对稳定 `ExceptionType`；结果与来源见[集中验收记录](../verification/2026-10-14-cache-concentrated-acceptance.md)。该结果不外推其他载荷、进程内存硬上限或全项目 Native 通过。

- 历史双库记录见文首链接；不能替代本轮新增准入/故障场景。当前实施与未验证项以[统一计划](../superpowers/plans/2026-10-03-cache-hardening.md)为准。
- 真实栈：E2E bootstrap 已配置 `Cache:RedisConnectionString` 时，
  `/health/ready` 应包含分布式缓存探针（见 `redis-ready-health.spec.mjs`）
