---
name: fullnet-performance-hardening
description: Use when analyzing or changing Full.NET request latency, throughput, database round trips, Dapper SQL, pagination, caching, Outbox or Jobs backlog, audit hot paths, allocations, frontend bundle size, load tests, BenchmarkDotNet benchmarks, or performance regressions.
---

# Full.NET 性能硬化

## 核心原则

先用真实指标证明瓶颈，再做保持安全、租户、事务和双库语义的最小改动。静态代码形态只能生成假设，不能单独证明生产吞吐提升。

开始前按根目录 `AGENTS.md` 与 [规则路由](../../../rules/README.md) 读取 `rules/performance-engineering.md` 和受影响领域章节；已在上下文中的有效内容无需重读。修改任务按入口记录基线与快照，只读性能分析不创建快照。需要命令、指标与场景矩阵时读取[性能地图](references/performance-map.md)。

## 1. 建立性能契约

1. 定义场景、数据规模、并发、运行时长、预热、机器与 Provider。
2. 至少记录吞吐、错误率、P50、P95、P99、分配、数据库 CPU/IO/锁和连接池等待中与变更相关的指标。
3. 记录基线提交、Release 配置和原始结果位置；BenchmarkDotNet 必须保留运行环境。
4. 先写会失败的预算、回归测试或可复现实验，再改实现。
5. 缺少真实 SQL Server/MySQL 环境时停止数据库性能结论，只报告静态风险与未验证项。
6. 开发阶段不要求达到 1 万同时在途；开发门禁验证高并发设计、正确性、资源上限和轻量回归。固定容量只能在专用容量环境认证，认证前标记 `Capacity-not-verified`，不得承诺固定 QPS。

## 2. 定位请求链

按入口到出口列出每个同步等待：

- 认证、租户解析和授权；
- Dapper 查询、命令、事务与 Outbox；
- 缓存 L1/L2/Backplane；
- HTTP、文件、实时或其他外部调用；
- 审计、日志和序列化。

对每项标记次数、是否串行、超时、取消、失败语义和数据规模。优先减少毫秒级往返，不先优化纳秒级映射。

Dapper 指标使用稳定 `StatementName`、Provider、操作类型和结果。OpenTelemetry 标签禁止包含原始 SQL、用户、租户、URL、异常消息或其他高基数值。

## 3. 选择改进路径

### SQL 与分页

- COUNT 与列表需要同时返回时，优先评估 QueryMultiple 合并往返。
- 大表深分页优先评估 `(时间, Id)` 等稳定游标分页；不得只把 OFFSET 换成更大的页大小。
- contains 搜索必须有时间范围、专用索引或搜索设施；不能把全表扫描包装成“灵活查询”。
- 索引变更必须提供 SQL Server/MySQL 执行计划、代表性数据量和写放大评估。

### 认证与 FusionCache

- Session、API Key、安全戳、租户状态属于安全关键数据。
- 任何缓存方案先写明撤销时效、源故障、失效事件和 fail-closed 行为。
- 禁止为了命中率开启 Fail-Safe 或让陈旧 L1 独立作出授权决定。
- 缓存失效禁止使用 Outbox。业务事务提交后，当前实例直接失效 L1/L2，再由 Redis Backplane 通知其他实例清理 L1，并以 TTL、版本和权威数据源回退兜底；分布式锁只用于昂贵热点回填防击穿。

### 审计、日志与高频写入

- 先按可靠性分为 B0 Domain Audit、B1 重要 HTTP Operation/Exception Audit、B2 普通 HTTP Operation Log/Access/诊断遥测；三类记录不得因同名而共用可靠性语义。
- B0 Domain Audit 与业务状态在同一数据库事务直接写入；若同一事务还写重要业务 Integration Event 的 Outbox，两者仍是独立事实，Audit 不使用 Outbox。
- B1 采用有界跨请求微批直接写入审计库，请求等待所属批次的写入尝试，默认 `fail-open` 并告警；要求“无审计不成功”的动作必须改为 B0，而不是增强 B1 或改走 Outbox。
- B2 使用有界异步日志管道和集中式平台，可按策略采样或丢弃，不写 Outbox，也不得伪装成合规 Audit。
- 所有异步缓冲必须同时定义有界容量、背压、关闭排空、崩溃丢失预算和自监控；缺少可靠性决策时停止异步化。

### Outbox 与 Jobs

- 取满 BatchSize 表示可能仍有积压，应立即继续领取；未满批次才等待 PollMilliseconds。
- 有界并发必须有明确顺序键、租约、Handler 作用域和最大并行度。
- 审查领取、续租与终态更新的锁顺序；SQL Server 使用 `UPDLOCK`/`READPAST` 等非阻塞领取，MySQL 8 在短事务内用 `FOR UPDATE SKIP LOCKED` 锁定候选后按主键更新，并以多 Worker 并发验证死锁为零。
- 消除逐条目录/Definition 查询，但不得把外部调用放入数据库事务。
- 记录吞吐、失败/重复率、队列深度与最老消息年龄。CDC/Kafka 已获 ADR-0006 单项提前实施批准，只能按正式 Spec/计划的追加式 Outbox、双库 Shadow、Inbox、单一发布所有权和回退门禁推进；基线仍用于决定试点、容量和生产切流，不得因为已批准而省略。

### 前端包体

- Vue 页面优先路由动态导入，ECharts 保持按需加载。
- 受影响 Vue 产物记录 minified、gzip 和 Brotli，CI 使用基线相对退化预算；Layui 已冻结，仅在明确授权修改其存量发布物时验证对应预算。
- 首屏静态依赖图和大体积延迟 chunk 必须分别建立预算；禁止只把依赖移出首屏图来隐藏总体积回涨。
- 拆包后验证受影响客户端的首屏、权限导航、错误页、关键流程和缓存策略。

## 4. 停止条件

遇到以下任一条件时停止实现并补证据或设计：

- 会改变认证撤销时效、租户隔离或 fail-closed 行为；
- 会降低 Audit/Outbox 可靠性或改变至少一次语义；
- 涉及数据库结构却没有成对迁移与双库测试；
- 没有代表性数据、执行计划或可重复基线；
- 改进只移动平均值，却恶化错误率、P99、恢复或资源上限；
- 需要引入尚未获 ADR/Spec 批准的 Broker、CDC、搜索引擎或新缓存实现。ADR-0006 范围内的 Kafka/CDC 不因本条自动停止，但命中该 ADR 的双库、Shadow、所有权、许可或恢复停止条件时仍必须停止。

## 5. 完成验证

执行位置、影响集、双库、页面验收与能力状态统一遵守 [测试与验证](../../../rules/development-quality.md#11-测试与验证)，不在本 Skill 另设本地 Integration 步骤。

1. 重跑相同场景与环境，比较基线和候选的收益、错误率、P99、恢复和资源上限；环境重型实验按权威规则选择执行位置。
2. 数据库变化必须有同场景 SQL Server 与 MySQL 证据；指标缺失时只给静态风险或未验证结论，不承诺固定 QPS。
3. 前端变化构建受影响 Vue 产物并记录包体；冻结 Layui 仅在明确授权修改时运行相关检查。
4. 性能基准证据按文档预算保存 Verification；局部优化的普通测试结果保留在 CI 与交付说明。按入口检查 `git diff --check` 与工作区状态。

## 常见错误

| 错误 | 正确处理 |
| --- | --- |
| 看到 async 就认为没有阻塞 | 统计实际同步等待与下游 I/O |
| 只跑序列化微基准 | 先比较数据库与网络往返 |
| 缓存认证结果但忽略撤销 | 明确撤销 SLA、事件失效和 fail-closed |
| 用 fire-and-forget 优化 Audit | 先按 B0/B1/B2 分类，再选择同事务或有界微批；Audit 不使用 Outbox |
| 盲目增加并行度或 BatchSize | 验证租约、顺序、连接池和尾延迟 |
| 只在一个数据库看执行计划 | SQL Server/MySQL 成对验证 |
| 只看未压缩 JS 大小 | 同时记录 minified、gzip、Brotli 与首屏请求 |
