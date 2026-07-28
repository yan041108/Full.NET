---
name: fullnet-performance-hardening
description: Use when analyzing or changing Full.NET request latency, throughput, database round trips, Dapper SQL, pagination, caching, Outbox or Jobs backlog, audit hot paths, allocations, frontend bundle size, load tests, BenchmarkDotNet benchmarks, or performance regressions.
---

# Full.NET 性能硬化

## 核心原则

先用真实指标证明瓶颈，再做保持安全、租户、事务和双库语义的最小改动。静态代码形态只能生成假设，不能单独证明生产吞吐提升。

开始前读取根目录 `AGENTS.md`、`rules/performance-engineering.md` 和受影响领域规则，并运行 `git rev-parse HEAD` 记录任务基线。需要命令、指标与场景矩阵时读取[性能地图](references/performance-map.md)。

## 1. 建立性能契约

1. 定义场景、数据规模、并发、运行时长、预热、机器与 Provider。
2. 至少记录吞吐、错误率、P50、P95、P99、分配、数据库 CPU/IO/锁和连接池等待中与变更相关的指标。
3. 记录基线提交、Release 配置和原始结果位置；BenchmarkDotNet 必须保留运行环境。
4. 先写会失败的预算、回归测试或可复现实验，再改实现。
5. 缺少真实 SQL Server/MySQL 环境时停止数据库性能结论，只报告静态风险与未验证项。

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

### 审计与高频写入

- 先区分合规 Audit、可靠业务记录和可采样访问遥测。
- 合规 Audit 必须进入数据库事务或 Outbox；禁止使用无界队列、无人观察任务或普通日志替代。
- 异步缓冲必须同时定义有界容量、背压、关闭排空、崩溃丢失预算和自监控；缺少可靠性决策时停止异步化。

### Outbox 与 Jobs

- 取满 BatchSize 表示可能仍有积压，应立即继续领取；未满批次才等待 PollMilliseconds。
- 有界并发必须有明确顺序键、租约、Handler 作用域和最大并行度。
- 审查领取、续租与终态更新的锁顺序；SQL Server 使用 `UPDLOCK`/`READPAST` 等非阻塞领取，MySQL 8 在短事务内用 `FOR UPDATE SKIP LOCKED` 锁定候选后按主键更新，并以多 Worker 并发验证死锁为零。
- 消除逐条目录/Definition 查询，但不得把外部调用放入数据库事务。
- 记录吞吐、失败/重复率、队列深度与最老消息年龄；CDC/Kafka 仍服从现有 Decision Gate。

### 前端包体

- Vue 页面优先路由动态导入，ECharts 保持按需加载。
- Vue 与 Layui 分别记录 minified、gzip 和 Brotli；CI 使用基线相对退化预算。
- 首屏静态依赖图和大体积延迟 chunk 必须分别建立预算；禁止只把依赖移出首屏图来隐藏总体积回涨。
- 拆包后验证首屏、权限导航、错误页、双端关键流程和缓存策略。

## 4. 停止条件

遇到以下任一条件时停止实现并补证据或设计：

- 会改变认证撤销时效、租户隔离或 fail-closed 行为；
- 会降低 Audit/Outbox 可靠性或改变至少一次语义；
- 涉及数据库结构却没有成对迁移与双库测试；
- 没有代表性数据、执行计划或可重复基线；
- 改进只移动平均值，却恶化错误率、P99、恢复或资源上限；
- 需要引入 Broker、CDC、搜索引擎或新缓存实现。

## 5. 完成验证

1. 重跑相同场景与相同环境，比较基线和候选。
2. 运行受影响 Unit/Architecture/Compatibility/Integration；共享宿主、认证、Outbox、缓存或 Dapper 基础设施使用选择器登记的 Smoke、双库过滤集或专项分片。
3. 数据库变化必须真实覆盖 SQL Server 与 MySQL。
4. 前端变化运行双管理端测试、生产构建并记录包体。
5. 更新 Verification、运行 `git diff --check` 和 `git status`。
6. 只声明数据支持的收益；未执行生产等价压测时禁止承诺固定 QPS。
7. 先运行 `pnpm test:integration:affected:plan -- --base <任务基线>` 审查影响集，再运行 `pnpm test:integration:affected -- --base <任务基线>`。本地任务禁止运行 193 项全量；完整 193 项只保留给 `main` CI 的互斥并行分片。

## 常见错误

| 错误 | 正确处理 |
| --- | --- |
| 看到 async 就认为没有阻塞 | 统计实际同步等待与下游 I/O |
| 只跑序列化微基准 | 先比较数据库与网络往返 |
| 缓存认证结果但忽略撤销 | 明确撤销 SLA、事件失效和 fail-closed |
| 用 fire-and-forget 优化 Audit | 先满足事务/Outbox 与崩溃恢复 |
| 盲目增加并行度或 BatchSize | 验证租约、顺序、连接池和尾延迟 |
| 只在一个数据库看执行计划 | SQL Server/MySQL 成对验证 |
| 只看未压缩 JS 大小 | 同时记录 minified、gzip、Brotli 与首屏请求 |
