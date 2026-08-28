# Dapper 基础设施 Native AOT 闭环验证

## 结论

Host.Api 可达的 Dapper Outbox、会话锁与 producer fence SQL 路径已完成 Native AOT 静态参数与行物化闭包。Linux `linux-x64` 原生产物已在 SQL Server/MySQL 上登录 Host 管理员，读取 `GET /api/v1/messaging/delivery-status`：积压摘要可反序列化且计数非负，事件流列表非空。

## 变更边界

- Outbox Store、会话锁、producer fence 的匿名 SQL 参数替换为固定键名字典或 `DynamicParameters`；`OutboxAcquireParameters` 注册显式绑定器。
- 会话锁仍直连同一 `DbConnection`，不改走 `IQueryExecutor`，避免跨连接丢失应用锁/命名锁。
- 嵌套领取/积压/退役/fence 行类型改为 `internal`，并在 `DapperAotInfrastructureRegistration` 注册物化器；SQL Server 积压使用 `ReadInt64`，MySQL 时间列保持 `DateTime` 再标 UTC。
- 原生 E2E 读取交付状态积压快照与已注册事件流，不以空 JSON 冒充物化覆盖。
- 不改变 Outbox 状态机、Lease、切流、公开 API 或双库 SQL 语义。

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Dapper 基础设施 Architecture RED | 初始按预期失败：3 个文件仍有匿名参数；缺失积压/领取/fence materializer |
| Dapper 基础设施 Architecture GREEN | 2/2，通过 |
| Data.Dapper AOT compile | `-p:FullNetAotAnalysis=true` 0 警告、0 错误 |
| `pnpm test:aot:analyzers` | 0 警告、0 错误 |
| `pnpm test:aot:publish:linux` | 通过；9 条允许的第三方警告；ELF 72,147,680 bytes |
| Linux Native SQL Server/MySQL | 5/5，通过；7 分 28 秒 |
| Native AOT/Dapper Architecture | 67/67，通过 |
| `pnpm test:inner -- --snapshot native-aot-dapper-infra-20260828` | 14/14（Outbox + smoke），通过 |
| `pnpm test:governance` | 52/52，通过 |
| 独立代码审查 | 无 Critical/Important；未改变公开 API、Outbox 状态机或租户语义 |

原生进程 TRX：`artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-dapper-infra-linux-local.trx`。

## Harness 调试记录

Architecture 选择器不得与 `pnpm test:aot:analyzers` 并行编译：AOT 属性会泄漏到常规 Release 构建。本任务串行执行。

Docker publish 与 Linux 测试容器会把 `project.assets.json` 指到容器 NuGet 路径。Windows 上 inner 前执行 `dotnet restore Full.NET.slnx --force-evaluate`。这与 Identity 闭环同一类本地编排问题，不是产品缺陷。

`Register<DapperEventDeliveryProducerFencePositionReader.RollbackPreparationRow>` 必须写在同一行，Architecture 门禁按字面子串扫描。

## 剩余边界

- 本记录只声明 Host.Api 可达的 Dapper Outbox 积压查询、会话锁参数与 fence 读路径闭包。不声明 Worker CDC 投影、切流/回退执行、Migrator Native AOT 或生成器模板。
- 原生 E2E 覆盖 delivery-status 积压快照；Acquire/续租/Retention/fence 捕获由 Architecture 物化器登记与 JIT inner Outbox 覆盖，未在本次原生产物上逐条写读。
- Windows 上 `pnpm test:aot:native:e2e` 只做发现 skip，不能替代本次 Linux 原生产物证据。
