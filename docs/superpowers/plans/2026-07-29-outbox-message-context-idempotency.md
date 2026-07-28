# Outbox Message Context And Idempotency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Outbox Handler 暴露稳定消息上下文，并在 Worker 启动期拒绝未声明幂等策略的生产 Handler。

**Architecture:** 在 `Full.NET.Abstractions` 增加不依赖持久化层的 `IntegrationEventContext` 和幂等策略枚举。`IIntegrationEventHandler` 保留旧 payload-only 方法，同时增加默认转发的上下文重载；Worker 只调用新重载，因此旧实现保持源兼容，新实现可以读取 MessageId。路由启动校验同时验证幂等策略，现有缓存失效 Handler 明确声明天然幂等。

**Tech Stack:** .NET 10、C# 默认接口实现、MSTest、现有 Outbox Worker。

## Global Constraints

- 任务基线固定为 `df21eb40b9c8ce646c954144880f1da9922277de`。
- 不改变 Outbox 至少一次交付、租约、重试、死信、租户或 MessagePack 语义。
- 本地只运行受影响 Unit/Architecture/Integration；完整 199 项 Integration 仅由 `main` CI 分片执行。
- 公共类型和接口使用英文标识符与中文 XML 注释。

---

### Task 1: 冻结兼容消息上下文

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IntegrationEventContext.cs`
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IIntegrationEventHandler.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Test: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`

**Interfaces:**
- Produces: `IntegrationEventContext(MessageId, MessageType, SchemaVersion, TenantId, TraceId, OccurredAtUtc)`。
- Produces: `IIntegrationEventHandler.HandleAsync(IntegrationEventContext, ReadOnlyMemory<byte>, CancellationToken)`；默认转发到旧重载。

- [x] **Step 1: 写入上下文透传 RED**

  扩展 `ProcessOnceAsync_DispatchesOnlyExactTypeAndVersionThenMarksProcessed`，让测试 Handler 覆盖上下文重载并断言六个字段与 `OutboxEnvelope` 完全一致。

- [x] **Step 2: 运行 RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
    --filter "FullyQualifiedName~OutboxProcessorTests.ProcessOnceAsync_DispatchesOnlyExactTypeAndVersionThenMarksProcessed" `
    --no-restore
  ```

  Expected: 因 `IntegrationEventContext` 和上下文重载尚不存在而编译失败。

- [x] **Step 3: 实现最小兼容路径**

  新增不可变上下文 record；接口默认重载调用旧方法；`OutboxProcessor` 从 Envelope 映射上下文并调用新重载。旧 Handler 不需要修改签名。

- [x] **Step 4: 运行 GREEN**

  重跑 Step 2，Expected: 1/1 通过。

### Task 2: 增加幂等策略启动门禁

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IIntegrationEventHandler.cs`
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IntegrationEventHandlerMatcher.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantProvisionedCacheInvalidationHandler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantChangedCacheInvalidationHandler.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventHandlerMatcherTests.cs`

**Interfaces:**
- Produces: `IntegrationEventIdempotencyStrategy.Unspecified|NaturallyIdempotent|MessageIdDeduplication`。
- Produces: `IIntegrationEventHandler.IdempotencyStrategy`，默认 `Unspecified` 以保持二进制兼容，但启动校验拒绝该值。

- [x] **Step 1: 写入策略门禁 RED**

  测试合法策略通过，未声明和越界策略分别抛出包含 `IdempotencyStrategy` 的启动错误。

- [x] **Step 2: 运行 RED**

  Run:

  ```powershell
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
    --filter "FullyQualifiedName~IntegrationEventHandlerMatcherTests" --no-restore
  ```

  Expected: 因策略契约和校验不存在而编译失败。

- [x] **Step 3: 实现最小门禁**

  增加枚举和默认属性；唯一性校验先拒绝未声明/未知枚举值。两个缓存失效 Handler 声明 `NaturallyIdempotent`，因为重复删除与重复失效广播只会收敛到同一缓存状态。

- [x] **Step 4: 运行 GREEN**

  重跑 Matcher 与 Outbox Processor 聚焦 Unit，Expected: 全部通过。

### Task 3: 同步文档并做受影响验证

**Files:**
- Modify: `docs/operations/outbox-worker-topology.md`
- Modify: `docs/superpowers/plans/2026-07-28-production-performance-hardening.md`
- Create: `docs/verification/outbox-message-context-idempotency-2026-07-29.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`

- [x] **Step 1: 记录兼容与消费约束**

  说明旧 Handler 的默认转发、上下文字段来源、至少一次语义，以及跨数据库/外部副作用必须使用 `MessageIdDeduplication` 并持久化去重，天然幂等声明需经代码审查。

- [x] **Step 2: 运行受影响门禁**

  运行 Outbox/Matcher/Tenancy 聚焦 Unit、Architecture、Release 构建、治理测试、`test:integration:affected:plan` 与选择器；只执行选择出的 Integration。

- [x] **Step 3: 复盘并提交**

  执行规则/Skill 复盘、`git diff --check` 与范围审计；只提交本计划列出的文件。
