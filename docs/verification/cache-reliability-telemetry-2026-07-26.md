# 缓存可靠性指标与延迟 Worker 验证

## 范围

本切片继续强化模块化单体 Task 7，只增加缓存可靠性可观测性与确定性事件确认断言：

- `Full.NET.Caching.Fusion` 统一拥有 Meter 与 FusionCache 事件桥接；
- Tenancy 只在既有 `TenantCacheInvalidator` 边界记录本机/分布式失效；
- 指标标签固定为 `scope`、`outcome`、`state`，不包含租户、域名、缓存键或异常文本；
- API 本机修复、共享 L2 提前收敛与事务 Outbox 可靠确认的职责不变。

## RED / GREEN

| 阶段 | 结果 |
| --- | --- |
| 指标合同 RED | 新测试因 `CacheReliabilityTelemetry`、`FusionCacheReliabilityMonitor` 不存在而编译失败 |
| 指标合同 GREEN | 失效指标与 FusionCache 事件桥接 **2/2** 通过 |
| Tenancy 接入 RED | 本机成功与分布式失败测试均因找不到失效指标而失败 |
| Tenancy 接入 GREEN | 指标、原 Backplane/L2 异常传播与缓存修复聚焦 **4/4** 通过 |
| 指标旁路 | 模拟 `MeterListener` 抛出异常，记录 API 不向缓存调用方传播 |

进程级 `MeterListener` 会捕获同名指标，因此相关测试使用 `DoNotParallelize`；没有为测试
增加租户或缓存键标签，避免破坏生产低基数约束。

## 双库故障注入结论

最初尝试把“Worker 延迟时 secondary 必须读到旧名称”写成合同，但 SQL Server/MySQL
均证明该假设错误：共享 Redis L2 的标签版本可以让 secondary 在 Outbox 消费前提前收敛；
同时 `/tenancy/current` 的业务 Handler 会在已解析租户作用域内重新查询数据库，也不能
代表 TenantResolver 缓存值。

最终合同改为可靠性所有权：`TenantChanged` 写入后、Worker 尚未运行时
`ProcessedAtUtc` 与 `DeadLetteredAtUtc` 均为空；Worker 恢复并成功发布后才写入
`ProcessedAtUtc`。共享 L2 的提前收敛是允许的优化，但不得替代事务 Outbox 确认。
`CacheConsistencyTests` 在 SQL Server/MySQL **6/6** 通过。

## 指标合同

| 指标 | 标签 |
| --- | --- |
| `fullnet.cache.invalidation.duration` | `scope=local\|distributed`、`outcome=success\|failure` |
| `fullnet.cache.invalidation.failures` | 同上，仅失败增加 |
| `fullnet.cache.hits.stale` | 无 |
| `fullnet.cache.backplane.circuit.transitions` | `state=open\|closed` |
| `fullnet.cache.backplane.recoveries` | 无 |

Meter 名为 `Full.NET.Caching.Reliability`，由 `AddFullNetCaching` 显式加入 OpenTelemetry。

## 当前门槛与未完成项

canonical 门槛更新为 **380/7/49/184**。本切片仍不把缓存能力标记为 `Verified`：
Outbox backlog 指标、生产多实例导出/告警以及完整 S0/S1/S2 策略矩阵仍待后续交付。

## 完整验证

| 门禁 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release` | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **380/380** / **7/7** / **49/49** |
| Integration 全量 | **184/184**，失败 **0**、跳过 **0**，**26m 44s** |
| Naming / Skill / Governance | **23/23** / **52** 项 / **11/11** |
| Integration tooling / 分片 | **4/4**；**35 + 35 + 62 + 52 = 184**，无遗漏或重复 |

## 规则与 Skill 复盘

- 规则：本切片没有出现第二次可泛化遗漏或高风险边界缺失，不升级强制规则。
- Skill：现有 `fullnet-module-delivery` 已覆盖缓存一致性、双库故障注入、门槛和验证记录；
  本切片没有形成可独立复用的新交付流程，不新增或修改项目 Skill。
