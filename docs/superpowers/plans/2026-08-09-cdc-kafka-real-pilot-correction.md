# CDC Kafka Real Pilot Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `fullnet.organization.unit.changed` schema 1 从模拟切流改成可验证的真实“业务事务 → 按流路由 Outbox → CDC → Kafka → Inbox → Identity 投影”试点，同时保留其他事件流的 Legacy 轮询。

**Architecture:** `IOutboxWriter` 继续作为业务模块唯一写边界，但由 scoped 路由器根据持久化事件流所有权选择 Legacy 或 append-only writer；Legacy Worker 和 Kafka Consumer 在同一 Worker 进程并存，各自只处理自己拥有的流。Identity 为候选事件注册显式、稳定的 Kafka subscription，消费始终经过现有 Inbox 本地事务管道。

**Tech Stack:** .NET 10、C#、Dapper、SQL Server 2022、MySQL 8.4、DbUp、Debezium、Kafka、MessagePack、MSTest、Testcontainers。

## Global Constraints

- 不引入 CAP、MassTransit 或 EF Core，不新增业务模块 `.csproj`。
- 保持模块化单体；跨模块只通过现有 Contracts 和 Integration Event 通信。
- SQL Server/MySQL 必须成对实现、成对验证；业务代码只使用 `Guid` UUID v7。
- Outbox、Inbox、业务投影和 processed 标记必须保持既定事务原子性。
- 默认配置保持 `LegacyPolling`；完成全部双库真实链路前不得标记 `Pilot` 或 `Verified`。
- 手写代码标识符使用英文，注释使用中文并解释边界和不变量。
- 每个任务先建立可失败验证，完成后只提交该任务列出的文件。

---

### Task 1: 注册真实 Organization → Identity Kafka 订阅

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/OrganizationUnitChangedKafkaSubscription.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Test: `tests/Full.NET.UnitTests/Identity/IdentityModuleRegistrationTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventSubscriptionCatalogTests.cs`

**Interfaces:**
- Consumes: `OrganizationUnitChangedIntegrationEventHandler.HandleAsync(IntegrationEventContext, ReadOnlyMemory<byte>, CancellationToken)` and `IIntegrationEventSubscription`.
- Produces: scoped subscription with `ConsumerName = "fullnet.identity.organization-unit-projection"`, event type `fullnet.organization.unit.changed`, schema `1`.

- [ ] **Step 1: 写失败测试**

在 `IdentityModuleRegistrationTests` 组装 Worker profile，断言恰好一个 `IIntegrationEventSubscription`，且三元路由为：

```csharp
Assert.AreEqual("fullnet.identity.organization-unit-projection", subscription.ConsumerName);
Assert.AreEqual(IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged, subscription.EventType);
Assert.AreEqual(1, subscription.SchemaVersion);
Assert.AreEqual(ServiceLifetime.Scoped, descriptor.Lifetime);
```

在 catalog 测试中断言重复 `(ConsumerName, EventType, SchemaVersion)` 会启动失败。

- [ ] **Step 2: 运行测试并确认 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~IdentityModuleRegistrationTests|FullyQualifiedName~IntegrationEventSubscriptionCatalogTests"`

Expected: FAIL，原因是没有生产订阅注册。

- [ ] **Step 3: 实现显式适配器**

```csharp
internal sealed class OrganizationUnitChangedKafkaSubscription(
    OrganizationUnitChangedIntegrationEventHandler handler)
    : IIntegrationEventSubscription
{
    public string ConsumerName => "fullnet.identity.organization-unit-projection";
    public string EventType => IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(context, payload, cancellationToken);
}
```

在 `IdentityModule.AddBackgroundServices` 用 `TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventSubscription, OrganizationUnitChangedKafkaSubscription>())` 注册。不得把所有 legacy handler 自动适配为 Kafka subscription。

- [ ] **Step 4: 运行测试并确认 GREEN**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~IdentityModuleRegistrationTests|FullyQualifiedName~IntegrationEventSubscriptionCatalogTests|FullyQualifiedName~MessagingWorkerCatalogGuard"`

Expected: PASS，且无重复路由。

- [ ] **Step 5: 提交**

```bash
git add src/Modules/Full.NET.Modules.Identity tests/Full.NET.UnitTests/Identity tests/Full.NET.UnitTests/Messaging
git commit -m "feat(messaging): register organization kafka subscription"
```

### Task 2: 用事件流所有权路由 Outbox 写入

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperRoutedOutboxWriter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxWriter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUnits/TenantUnitManagementService.cs`
- Test: `tests/Full.NET.UnitTests/Data/DapperRoutedOutboxWriterTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Organization/OrganizationUnitEventRoutingAssertions.cs`

**Interfaces:**
- Consumes: `IEffectiveEventDeliveryOwnerResolver.GetDeliveryOwnerAsync(string, int, CancellationToken)`.
- Produces: `DapperRoutedOutboxWriter : IOutboxWriter`; legacy and append-only writers remain concrete scoped collaborators and are not exposed to business modules.

- [ ] **Step 1: 写路由 RED 测试**

覆盖四条精确断言：Legacy owner 写 `fn_outbox_message`；CdcKafka owner 写 `fn_messaging_outbox_event`；未知流采用目录默认 owner；CdcKafka 写入缺失 metadata 时抛出失败关闭异常。测试还必须断言一次业务调用只写一张 Outbox 表。

- [ ] **Step 2: 运行并确认 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DapperRoutedOutboxWriterTests"`

Expected: FAIL，原因是 routed writer 尚不存在。

- [ ] **Step 3: 实现 scoped 路由器**

核心签名固定为：

```csharp
internal sealed class DapperRoutedOutboxWriter(
    DapperOutboxWriter legacyWriter,
    DapperAppendOnlyOutboxWriter appendOnlyWriter,
    IEffectiveEventDeliveryOwnerResolver ownerResolver) : IOutboxWriter
```

两个 `AddAsync` overload 都先解析 `(eventType, schemaVersion)` 的有效 owner。`LegacyPolling` 调 legacy writer；`CdcKafka` 只允许 metadata overload 并调 append-only writer。旧 writer 的 metadata overload 应转发到其无 metadata overload，以便候选生产者在切流前仍走 Legacy，而不是抛 `NotSupportedException`。DI 中 `IOutboxWriter` 只注册 routed writer；删除按全局 `MessagingOutboxOptions.Mode` 二选一的注册逻辑，配置类型保留一版并标记 obsolete，供配置迁移使用。

- [ ] **Step 4: 给候选生产者补稳定 metadata**

`TenantUnitManagementService.PublishUnitChangedAsync` 使用：

```csharp
new IntegrationEventMetadata(
    PartitionKey: tenantId.ToString("D"),
    CorrelationId: null,
    CausationId: null,
    Producer: "fullnet.organization")
```

metadata 与业务写入必须继续使用同一个 `ICommandTransaction`/`DbSession`。

- [ ] **Step 5: 双库验证**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~OrganizationUnitEventRoutingAssertions"`

Expected: SQL Server/MySQL 各自证明 Legacy owner 只产生 legacy 行，Cdc owner 只产生 append-only 行，业务事务回滚时两表都不留行。

- [ ] **Step 6: 提交**

```bash
git add src/BuildingBlocks/Full.NET.Data.Dapper src/Modules/Full.NET.Modules.Organization tests/Full.NET.UnitTests/Data tests/Full.NET.IntegrationTests/Organization
git commit -m "feat(messaging): route outbox writes by stream owner"
```

### Task 3: 让 Legacy Worker 与 Kafka Consumer 按流并存

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/MessagingWorkerMode.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/MessagingWorkerOptions.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/MessagingWorkerCatalogGuard.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Test: `tests/Full.NET.UnitTests/Messaging/MessagingWorkerOptionsTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/WorkerCompositionTests.cs`

**Interfaces:**
- Consumes: persistent `EventDeliveryOwner` per stream.
- Produces: `LegacyPolling`, `ShadowCdc`, `HybridKafka` modes; accept legacy config string `CdcKafka` as an obsolete alias for `HybridKafka` for one release.

- [ ] **Step 1: 写 Worker composition RED 测试**

断言 `HybridKafka` 同时注册 `OutboxProcessor`、`OutboxRetentionProcessor` 和 `KafkaConsumerWorker`；`LegacyPolling` 不注册 Kafka；所有 HostedService 单例构造函数不得直接依赖 scoped 服务。

- [ ] **Step 2: 运行并确认 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~MessagingWorkerOptionsTests"`

Expected: FAIL，因为当前 `CdcKafka` 分支关闭 Legacy Worker。

- [ ] **Step 3: 实现并存模式**

在非版本退役命令下始终注册 `OutboxProcessor` 与 `OutboxRetentionProcessor`；仅 `HybridKafka` 注册 Kafka。`ShadowCdc` 仍只增加 shadow processor。删除“Kafka 开启就禁止 Legacy poller”的验证；保留 Kafka enabled、bootstrap servers、subscription catalog 和 topic catalog 的失败关闭。

- [ ] **Step 4: 增加流级启动守卫**

`MessagingWorkerCatalogGuard.ValidateHybridKafkaMode` 必须验证每个有效 owner 为 `CdcKafka` 的 topic 恰好存在至少一个 subscription；不得只检查全局订阅数量。默认 owner 全是 Legacy 时允许 Hybrid Worker 启动但 Kafka 不产生业务副作用。

- [ ] **Step 5: 运行 composition 与架构测试**

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~WorkerCompositionTests|FullyQualifiedName~ServiceLifetime"`

Expected: PASS，0 个 captive dependency。

- [ ] **Step 6: 提交**

```bash
git add src/Hosts/Full.NET.Host.Worker tests/Full.NET.UnitTests/Messaging tests/Full.NET.ArchitectureTests
git commit -m "feat(worker): run legacy and kafka delivery by stream"
```

### Task 4: 把切流门禁从全局积压改成目标流积压

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Messaging/Features/ChangeDeliveryOwner/DeliveryCutoverService.cs`
- Modify: `src/Modules/Full.NET.Modules.Messaging/Persistence/EventStreamOwnershipSql.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/EventDeliveryCutoverTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/EventDeliveryRollbackTests.cs`

**Interfaces:**
- Produces: stream-scoped backlog query keyed by `(MessageType, SchemaVersion)` and compare-and-swap ownership update keyed by current `Version`.

- [ ] **Step 1: 写并发和隔离 RED 测试**

构造目标流已排空、另一个 legacy 流仍有积压的场景，切流必须成功且其他流仍被 legacy handler 处理；再构造目标流 pending/retry/active lease 各一条，切流必须逐项失败。并发发布与切流测试必须证明事件只落入一个 Outbox，不丢失、不重复产生业务副作用。

- [ ] **Step 2: 实现流级查询与 CAS**

Legacy backlog SQL 必须过滤 `MessageType = @MessageType AND SchemaVersion = @SchemaVersion`。所有权更新使用 `WHERE ... AND Version = @Version AND CurrentOwner = @ExpectedOwner`，受影响行不是 1 时返回 conflict。cutoff 取目标流最后一条已确认 legacy event，而不是全局最后一条。

- [ ] **Step 3: 处理切流竞态**

生产者路由解析、Outbox 插入与所有权切换必须使用数据库可证明的顺序边界。允许的实现是：切流对目标流获取数据库 application lock/advisory lock；routed writer 在同一数据库事务中获取同名 shared lock 后读取 owner 并写入。SQL Server 使用 `sp_getapplock`，MySQL 使用 `GET_LOCK`/`RELEASE_LOCK`，锁名由规范事件类型和 schema 的固定哈希组成，禁止拼接 SQL 标识符。

- [ ] **Step 4: 双库运行**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~EventDeliveryCutoverTests|FullyQualifiedName~EventDeliveryRollbackTests"`

Expected: SQL Server/MySQL 全部 PASS，且测试不手工镜像 append-only 行。

- [ ] **Step 5: 提交**

```bash
git add src/Modules/Full.NET.Modules.Messaging src/Hosts/Full.NET.Host.Worker tests/Full.NET.IntegrationTests/Messaging
git commit -m "fix(messaging): make delivery cutover stream scoped"
```

### Task 5: 补齐 094 迁移恢复门禁

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration094MessagingStreamOwnershipRecoveryTests.cs`
- Modify: `eng/testing/test-matrix.json`
- Modify: `contracts/naming/naming-debt.json` only if the naming gate reports a precise 094 exception

**Interfaces:**
- Produces: `migrationSelections["094"]` with SQL Server/MySQL recovery test filter.

- [ ] **Step 1: 写恢复 RED 测试**

两种 Provider 都执行：完整迁移；插入 ownership 行；删除一个非主键索引并制造可恢复的列/索引形状；删除 094 的 schema version；重跑迁移；断言数据保留、列/索引恢复、第三次迁移执行数为 0。

- [ ] **Step 2: 注册选择器并运行**

在 `migrationSelections` 增加：

```json
"094": {
  "filter": "FullyQualifiedName~Migration094MessagingStreamOwnershipRecoveryTests.MySql_|FullyQualifiedName~Migration094MessagingStreamOwnershipRecoveryTests.SqlServer_"
}
```

Run: `pnpm test:integration:affected -- --base HEAD~1 --phase slice`

Expected: 094 双库 recovery 命中且 PASS，不出现“migration 094 is not registered”。

- [ ] **Step 3: 提交**

```bash
git add tests/Full.NET.IntegrationTests/Migrations/Migration094MessagingStreamOwnershipRecoveryTests.cs eng/testing/test-matrix.json contracts/naming/naming-debt.json
git commit -m "test(migrations): cover messaging ownership recovery"
```

### Task 6: 建立不使用合成镜像行的真实 E2E

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Messaging/OrganizationUnitCdcKafkaEndToEndTests.cs`
- Modify: `deploy/compose/compose.messaging-cdc.yml`
- Modify: `tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs` only for reusable Kafka/Debezium fixture lifecycle

**Interfaces:**
- Consumes: real Organization API/service write, Debezium connector, Kafka broker, Kafka Worker, Inbox and Identity projection query.
- Produces: one SQL Server test and one MySQL test with the same assertions.

- [ ] **Step 1: 写端到端测试并确认 RED**

测试必须通过真实 Organization 命令创建/更新机构单元，不得直接向 `fn_messaging_outbox_event` 插入或复制行。等待条件按顺序断言：append-only 行存在；Debezium source position 前进；Kafka 消费 offset 提交；Inbox `(ConsumerName, EventId)` processed；Identity 投影版本等于事件版本；legacy 表没有该切流后事件。

- [ ] **Step 2: 增加故障矩阵**

同一 fixture 覆盖：Consumer 在业务提交前崩溃、业务提交后 offset commit 前崩溃、重复 Kafka 消息、乱序旧版本、Kafka 暂停恢复、回退 Legacy。最终投影必须收敛，Inbox 不允许同一 EventId 不同 payload。

- [ ] **Step 3: 运行真实双库 E2E**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~OrganizationUnitCdcKafkaEndToEndTests"`

Expected: SQL Server/MySQL 全部 PASS；日志中没有 synthetic/mirror insert。

- [ ] **Step 4: 提交**

```bash
git add tests/Full.NET.IntegrationTests/Messaging tests/Full.NET.IntegrationTests/SharedDatabaseFixture.cs deploy/compose/compose.messaging-cdc.yml
git commit -m "test(messaging): verify real cdc kafka pilot end to end"
```

### Task 7: 重新认证状态并更新运维文档

**Files:**
- Modify: `docs/verification/cdc-kafka-pilot-2026-08-08.md`
- Modify: `docs/operations/cdc-kafka-event-delivery.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: Task 1–6 的新鲜 CI/本地命令输出。
- Produces: 可追溯验证记录；没有生产等价环境证据时最高只能是 `Build-verified / Pilot`，并保留 `Capacity-not-verified`。

- [ ] **Step 1: 运行合并候选门禁**

Run: `pnpm test:governance`

Run: `pnpm test:naming`

Run: `dotnet build Full.NET.slnx -c Release --no-restore`

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build`

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --no-build`

Run: `pnpm test:integration:affected -- --base HEAD~7 --phase merge`

Expected: 全部 exit 0；任何未执行或环境阻塞项必须原样记为未验证。

- [ ] **Step 2: 更新状态**

只有 Task 6 的两种 Provider 真实 E2E 都通过，才把状态从 `Designing / Shadow-only` 改为 `Build-verified / Pilot`。记录确切提交、日期、命令、测试数、Broker/Connector 版本、已验证故障和未验证容量项。

- [ ] **Step 3: 提交**

```bash
git add docs README.md
git commit -m "docs(messaging): certify real cdc kafka pilot"
```
