# Cursor 多任务交付审查记录（2026-08-09）

> 审查基线：`051af3be8068b0c0eb0d65529f42ee22d1da0757`
> 任务快照：`cursor-review-20260809`
> 范围：Document 对标提交、Messaging Outbox/Inbox/Kafka/CDC/切流提交及其关联测试、迁移和状态文档。

## 结论

本轮交付不能整体视为完成。Document 存在公共契约源码兼容破坏、093 迁移与运行时 SQL 模型不一致、分享口令明文写入和响应泄漏；Messaging 存在 DI 启动异常、Kafka HostedService 捕获 scoped 依赖、无真实业务订阅时静默空跑，以及把模拟切流误标为 `Build-verified / Pilot` 的问题。

当前修复将不可安全使用的路径改为失败关闭，并撤销 CDC/Kafka 的 Pilot 状态。真正的单流试点仍须按后续计划完成。

## 已纠正问题

| 严重度 | 问题 | 纠正 |
| --- | --- | --- |
| P0 | `MessagingModule` 使用无法区分实现类型的 `TryAddEnumerable` 工厂注册 Topic，API/Worker 组装时抛 `ArgumentException` | 改为按官方 Topic 实例幂等注册，并增加重复注册回归测试 |
| P0 | `KafkaConsumerWorker` 单例直接持有 scoped dispatcher、catalog 和业务订阅 | Worker 仅持有 `IServiceScopeFactory`，按消息创建异步作用域；catalog 改为 scoped |
| P0 | catalog 改为 scoped 后，未加载 Messaging 模块的精简宿主只注册 Dispatcher，DI 图不闭合 | Modularity 核心注册 scoped 空目录作为部分装配默认值，完整 Messaging 模块仍替换为真实目录；补充部分宿主回归测试 |
| P0 | `CdcKafka` 没有任何生产 `IIntegrationEventSubscription` 仍可静默启动 | 增加启动目录守卫，无真实订阅时失败关闭 |
| P0 | Document 093 迁移列与 `DocumentPermissionSql` / `DocumentShareSql` 不一致，首次调用会 SQL 失败 | SQL Server/MySQL 表结构按实际持久化契约对齐 |
| P0 | Document 分享口令明文持久化且响应回显，匿名访问接口又不校验口令 | 响应字段加 `JsonIgnore`、映射强制置空；在安全口令协议实现前，带口令创建失败关闭；分享码改用密码学安全随机源 |
| P1 | Document 公共 positional records 新增必填参数，旧调用方无法编译 | 恢复旧构造函数重载并增加兼容测试 |
| P1 | 新增权限/字段/注册顺序后，精确目录测试仍保留旧期望 | 核对 Endpoint 授权绑定后更新精确契约测试 |
| P1 | 087/088/089/090 的受控幂等 DDL 未登记命名债务，命名门禁失败 | 增加按文件、规则和过期日期限定的债务记录 |
| P1 | 模拟 append-only 行的测试被描述成真实 CDC/Kafka 试点 | 状态降为 `Designing / Shadow-only`，运维文档明确禁止切流 |

## 尚未完成

- Organization 生产者尚未按事件流所有权在 Legacy 与 append-only Outbox 间路由。
- Identity 业务处理器尚未注册稳定 Kafka consumer identity。
- Legacy Worker 与 Kafka Consumer 尚不能在同一运行角色中按事件流并存。
- 没有真实 Debezium + Kafka + Inbox + Identity 投影副作用的 SQL Server/MySQL 端到端证据。
- 093/094 尚缺独立迁移恢复测试和 `eng/testing/test-matrix.json` 选择器注册。
- Document 分享口令的哈希、验证、限流及原子访问计数协议尚未实现；当前仅安全地拒绝带口令创建。
- Cursor 生成的 `tests/e2e/admin-parity/test-results.json` 显示 WCAG 对比度和表单标签失败，属于待处理测试产物，不能作为通过证据。
- OpenAPI/Vue coverage 门禁仍报告 7 个未登记 Document API 模块，且新版 TypeScript permission/share DTO 与 C# 契约字段不一致；按 Document 收口计划 Task 5 统一，当前不得把这些客户端 API 视为可交付。
- `@fullnet/client-contracts` 现有 126 项中 5 项失败：Document category/tag、Jobs definition/schedule、Identity user fixture 未随新增字段同步。
- `@fullnet/admin` typecheck 失败：Document 同一路由存在两套不兼容客户端契约，Jobs 使用错误的 Element Plus drawer 类型并存在 fixture/literal 漂移，Identity 用户编辑器新增 props 与用户 fixture 未补齐。后两类按独立客户端计划收口。

后续分别按以下计划实施：

- [`2026-08-09-cdc-kafka-real-pilot-correction.md`](../superpowers/plans/2026-08-09-cdc-kafka-real-pilot-correction.md)
- [`2026-08-09-document-parity-security-closure.md`](../superpowers/plans/2026-08-09-document-parity-security-closure.md)
- [`2026-08-09-admin-parity-client-contract-closure.md`](../superpowers/plans/2026-08-09-admin-parity-client-contract-closure.md)

## 验证记录

以下结果均来自本轮工作区的新鲜执行；失败项保留为后续计划的真实起点，不得表述为通过。

| 验证 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release --no-restore` | 通过，0 warning / 0 error |
| `Full.NET.UnitTests.exe --no-ansi --progress off` | 通过，1211/1211 |
| `Full.NET.ArchitectureTests.exe --no-ansi --progress off` | 通过，95/95 |
| `Full.NET.CompatibilityTests.exe --no-ansi --progress off` | 通过，7/7 |
| `pnpm test:governance` | 通过，27/27 |
| `pnpm test:naming` | 通过，24/24 |
| `pnpm test:skills` | 通过，module 79/79、performance 48/48 |
| `pnpm test:sql-safety` | 通过，5/5 |
| 影响集 migrations shard | 通过，290/290，52m33s |
| `pnpm test:integration:smoke` | 首轮发现部分宿主 DI 缺口；修复并重建后通过，8/8，2m09s |
| OpenAPI 门禁 | 82/84；2 项 Document 客户端覆盖失败 |
| `@fullnet/client-contracts` build | 通过 |
| `@fullnet/client-contracts` tests | 121/126；5 项 fixture 漂移失败 |
| `@fullnet/admin` typecheck | 失败；Document 重复契约及 Jobs/Identity 类型与 fixture 漂移 |
