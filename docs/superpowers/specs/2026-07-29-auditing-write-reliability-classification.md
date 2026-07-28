# Audit 写入可靠性分类与请求内批处理规格

- 状态：Approved
- 批准日期：2026-07-29
- 批准来源：当前授权用户要求继续执行
  [`2026-07-28-production-performance-hardening.md`](../plans/2026-07-28-production-performance-hardening.md)
  的 Task 22
- 适用范围：`Full.NET.Modules.Auditing` 的 Access、Operation、Exception HTTP 汇总记录
- 基线提交：`562eb758dd4e87c67e9ea6e50937e024429259e0`
- 不替代：Identity、租户、配置、资金等业务用例在自身数据库事务中保存的领域审计

## 1. 背景

当前三个 HTTP Middleware 都会在请求链退出前同步等待独立 `INSERT`。普通写请求形成
Operation、Access 两次串行数据库往返，未处理异常请求形成 Exception、Operation、Access
三次串行往返。双库短样本中，完整写入相对关闭写入的 P95 增量为 SQL Server `12.956ms`、
MySQL `28.883ms`；详细证据见
[`auditing-write-tail-latency-2026-07-28.md`](../../verification/auditing-write-tail-latency-2026-07-28.md)。

三个 Writer 当前都在数据库异常时记录 Warning 并保留原业务响应。因此“同步等待写库”只表示
请求结束前完成持久化尝试，不能被描述成与业务数据原子提交或绝对不丢。

## 2. 可靠性分类

| 类型 | 分类 | 强制语义 | 不承担的职责 |
| --- | --- | --- | --- |
| Access | 请求访问遥测 | 默认采集；允许未来经独立规格实施有界采样或过载丢弃；不得包含 QueryString、Body 或明文 IP | 不作为登录、授权、租户、配置、资金或其他合规事实源 |
| Operation | 安全审计摘要 | 已认证写请求必须采集；禁止采样、fire-and-forget、无界队列和无指标丢弃；请求管道返回前完成数据库提交尝试 | 不替代业务事务中的变更前后值、稳定结果码和领域审计 |
| Exception | 安全相关异常审计摘要 | 未处理且非取消异常必须采集；禁止采样、fire-and-forget、无界队列和无指标丢弃；请求管道退出前完成数据库提交尝试 | 不保存原始异常消息、StackTrace、请求体或秘密，也不替代 Error/Critical 诊断通道 |

Identity 登录、刷新、退出、会话撤销等现有领域审计继续遵循其原规格：与对应安全状态写入共享
业务事务。HTTP Operation/Exception 汇总只能补充 Trace、路径和请求结果，不能作为降低领域审计
可靠性的理由。

## 3. 请求内批处理

采用请求作用域的固定三槽缓冲区，每类记录最多一条。缓冲区不是跨请求队列，不启动后台任务，
容量不会随流量增长。

1. Access、Operation、Exception Middleware 只构造脱敏模型并写入对应槽位。
2. 位于三者外层的协调 Middleware 在请求管道退出时创建不可变快照。
3. 空快照不访问数据库；非空快照只执行一次 Dapper 命令。
4. 单条命令按实际组合包含一至三个参数化 `INSERT`，并在显式数据库事务中提交。
5. 任一 `INSERT` 失败时整批回滚，禁止留下同一请求的半批记录。
6. 提交继续使用 `CancellationToken.None`，避免客户端断开连带取消审计尝试。
7. 数据库失败保留当前“不得覆盖原业务响应”的兼容语义，但必须由稳定 Statement 指标和
   Warning 暴露；禁止静默吞掉。

## 4. Benchmark 隔离

生产 Host 不提供关闭 Operation 或 Exception 的配置。混合负载 Benchmark 可通过测试专用 DI
策略按请求选择 `none/access/operation/exception/all`，该策略不得在生产注册中读取 Header。
报告必须按批 Statement 的实际组成还原三类写入次数，并继续核对三张表的行数增量。

正式保留候选前必须使用相同 Host、数据库、场景和并发做单变量 A/B：

- 基线：提交 `562eb75` 的逐条同步写入；
- 候选：请求内单命令批处理；
- 指标：QPS、P50/P95/P99、错误率、Dapper 命令数、三表行数、连接池、数据库锁等待、
  Host/数据库 CPU；
- Provider：SQL Server 2022 与 MySQL 8；
- 停止条件：任一审计类型缺失、半批提交、业务响应语义改变、错误率上升，或任一 Provider
  的 P95/P99 无实质收益。

## 5. 明确拒绝的方案

- 直接 `Task.Run`、不等待的异步调用或进程退出即丢失的 fire-and-forget；
- 无界 Channel、普通日志 Sink 或 Error 日志替代数据库 Audit；
- 为降低尾延迟默认采样 Operation/Exception；
- 把 Middleware 汇总记录伪装成与任意业务写事务原子一致；
- 未经正式矩阵证据提高数据库连接池、Worker 并发或批量上限。

## 6. 验收

- 单元测试覆盖空批、一/二/三类组合、重复捕获拒绝、一次命令、显式事务、整批失败和取消边界；
- SQL Server/MySQL 聚焦 Integration 验证三类记录均可查询、异常路径无半批提交；
- Benchmark 契约验证测试专用 profile 不进入生产配置，且归因次数与数据库行数一致；
- 仅运行受影响测试；完整 193 项 Integration 继续只由 `main` CI 四分片执行。
