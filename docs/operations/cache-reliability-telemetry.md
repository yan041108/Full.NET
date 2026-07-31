# 缓存可靠性指标与多实例告警

本文描述 `Full.NET.Caching.Reliability` Meter 的生产观测与告警基线，对应
[缓存可靠性验证记录](../verification/cache-reliability-telemetry-2026-07-26.md)。

## 指标清单

| 指标 | 标签 | 含义 |
| --- | --- | --- |
| `fullnet.cache.invalidation.duration` | `scope=local\|distributed`、`outcome=success\|failure` | 单次失效耗时 |
| `fullnet.cache.invalidation.failures` | 同上 | 失效失败计数 |
| `fullnet.cache.hits.stale` | 无 | Fail-Safe 之外仍命中陈旧条目 |
| `fullnet.cache.backplane.circuit.transitions` | `state=open\|closed` | Backplane 熔断状态转换 |
| `fullnet.cache.backplane.recoveries` | 无 | 熔断恢复次数 |

标签禁止包含租户、域名、缓存键或异常文本，以保持低基数。

## 编排与导出

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
| `fullnet.cache.invalidation.failures` 5 分钟内 `distributed`+`failure` > 0 持续 3 个周期 | Warning | 检查 Redis 连通、Backplane 熔断与 Worker Outbox 重试 |
| `fullnet.cache.hits.stale` 任意副本 > 0 持续 5 分钟 | Critical | 安全关键路径可能读取陈旧权限/租户解析；按 S0 策略 Fail-Safe 已关闭，需立即排查失效链 |
| `fullnet.cache.backplane.circuit.transitions` `open` 在 10 分钟内 ≥ 2 | Warning | 核对 Redis 负载、网络分区与连接池 |
| `fullnet.outbox.pending.messages` 与最老年龄同步上升且缓存失效失败并存 | Warning | 跨节点修复可能受阻；先恢复 Redis 再观察 Outbox 消费 |

阈值需在真实多副本 soak 后按环境调参；本表为编排补证基线，不替代容量测试。

## 分级策略（S0 / S1 / S2）

| 等级 | 典型数据 | Fail-Safe | 跨节点修复 |
| --- | --- | --- | --- |
| S0 安全关键 | 权限、会话、租户解析 | 禁止 | 提交后本机修复 + Outbox 可靠确认 |
| S1 业务读多 | 字典、配置只读投影 | 禁止 | 同上或短 TTL + 显式失效 |
| S2 可丢 | 演示/统计缓存 | 可评估 | 允许仅 L2 提前收敛 |

新增缓存必须声明等级并在模块 Spec 中记录失效点；不得默认 Fail-Safe 用于授权。

## 验证

- 双库集成：`CacheConsistencyTests` **6/6**
- 真实栈：E2E bootstrap 已配置 `Cache:RedisConnectionString` 时，
  `/health/ready` 应包含分布式缓存探针（见 `redis-ready-health.spec.mjs`）
