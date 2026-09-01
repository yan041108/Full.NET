# Notifications 收件端点管理 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为当前登录用户交付按租户作用域和已发布 Provider Profile 版本隔离的收件端点登记、查询、删除 API，并把 SMTP 邮箱端点接入 Vue 通知偏好页。

**Architecture:** 复用 Notifications 已有 `fn_notifications_recipient_endpoint`、Data Protection 和 `RecipientEndpointStore`，不新增迁移。HTTP 只允许从认证 Claim 取得当前用户，登记状态由服务端固定为 `pending`；Profile 版本必须属于当前受信作用域且由已注册 Adapter 声明相同端点类型。列表永不投影受保护原值，删除必须同时匹配当前作用域和当前用户。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、SQL Server/MySQL、System.Text.Json 源生成、Vue 3、TypeScript、Element Plus、Vitest、MSTest。

## Global Constraints

- 不接受请求体中的 `UserId`、`TenantId` 或 `VerificationStatusKey`；用户和租户均来自受信会话，状态固定为 `pending`。
- 端点原值只能经 `NotificationRecipientEndpointProtector` 后写库；HTTP、日志、异常、OpenAPI 夹具和 Vue 状态只保留掩码。
- 只允许当前作用域内、当前已发布的 Provider Profile 版本；未知版本、跨作用域版本、已被新版本替换的版本或 Adapter 不支持的端点类型必须失败关闭。
- 同一 `TenantScopeKey + UserId + ProviderProfileVersionId + EndpointKindKey` 只允许一个端点；本切片使用显式删除后重新登记，不伪装数据库差异下的无锁 Upsert。
- 本切片不把 `pending` 自动升级为 `verified`，不实现邮件验证码、短信验证码、管理员代管或任意用户写入；Worker 仍只消费 `verified` 端点。
- 新增或修改的后端类型、方法和参数必须有中文 XML 文档；关键业务分支必须说明安全边界。

---

### Task 1: 建立双库和 HTTP 契约 RED

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Notifications/NotificationProfileBindingAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiNotificationsProfilesBindingsContractAssertions.cs`
- Modify: `contracts/openapi/notifications-profiles-bindings-v1.json`
- Modify: `tests/openapi/notifications-profiles-bindings-contract.test.mjs`

**Interfaces:**
- Consumes: 已发布 `NotificationProviderProfileResponse.LatestPublishedVersionId`、现有 Host/Tenant 登录辅助能力。
- Produces: `GET/POST/DELETE /api/v1/notifications/my-recipient-endpoints` 的失败证据与冻结契约。

- [x] **Step 1: 写 HTTP 行为测试**

  在 SQL Server/MySQL 共用断言中验证：无 `PreferencesUpdate` 的 POST 为 403；合法 POST 返回 201 且只有掩码与 `pending`；重复登记返回 409；GET 只返回当前用户和当前作用域；DELETE 后 GET 为空；跨租户 ProfileVersion 登记返回 404 或等价失败关闭结果。

- [x] **Step 2: 写运行时 OpenAPI RED**

  断言三个 OperationId：`notificationsListMyRecipientEndpoints`、`notificationsCreateMyRecipientEndpoint`、`notificationsDeleteMyRecipientEndpoint`，并确认请求 Schema 不含 `userId`、`tenantId`、`verificationStatusKey`。

- [x] **Step 3: 运行 RED**

  Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~NotificationsApiSqlServerTests|FullyQualifiedName~NotificationsApiMySqlTests"`

  Expected: 新路径返回 404 或 OpenAPI 缺少新 OperationId；失败原因是端点尚未映射，而不是测试发现或环境错误。

### Task 2: 实现受信作用域的当前用户端点 API

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Contracts/InboxMessageContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Features/ManageRecipientEndpoints/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/ManageRecipientEndpoints/RecipientEndpointStore.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationRecipientEndpointSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Serialization/NotificationsJsonSerializerContext.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`

**Interfaces:**
- Consumes: `RecipientEndpointStore`、`NotificationInboxScope.Resolve`、`IEnumerable<INotificationProviderAdapter>`、`NotificationPlatformPermissions.PreferencesRead/PreferencesUpdate`。
- Produces: `CreateMyRecipientEndpointRequest(Guid ProviderProfileVersionId, string EndpointKindKey, string RawValue)` 与三个受保护 Endpoint。

- [x] **Step 1: 定义最小请求契约和 JSON 元数据**

  ```csharp
  /// <summary>当前用户登记收件端点的请求；服务端始终以待验证状态保存。</summary>
  public sealed record CreateMyRecipientEndpointRequest(
      Guid ProviderProfileVersionId,
      string EndpointKindKey,
      string RawValue);
  ```

- [x] **Step 2: 增加作用域校验、插入冲突和精确删除 SQL**

  Profile 校验使用 Profile 与 Version 联表，并要求 `TenantScopeKey`、`LatestPublishedVersionId` 和版本 ID 同时匹配；删除 SQL 必须包含 `Id + TenantScopeKey + UserId`。SQL 不读取其他模块表，不新增跨模块事务。

- [x] **Step 3: 收紧 Store**

  对公开 API 调用增加 `CreateMineAsync` 和 `DeleteMineAsync`；`CreateMineAsync` 固定写入 `pending`，先验证 Profile 归属和 Adapter 的 `RecipientEndpointKindKey`，再保护原值。对 email 使用 `MailAddress` 等价的闭合格式验证；数据库唯一冲突映射为 `409` 稳定错误，不回显原值。

- [x] **Step 4: 映射路由与权限**

  ```text
  GET    /api/v1/notifications/my-recipient-endpoints      PreferencesRead
  POST   /api/v1/notifications/my-recipient-endpoints      PreferencesUpdate
  DELETE /api/v1/notifications/my-recipient-endpoints/{id} PreferencesUpdate
  ```

  POST 从 `ClaimTypes.NameIdentifier` 解析用户并返回 201；DELETE 成功返回 204，未知或越权 ID 返回 404。

- [x] **Step 5: 重跑双库与 OpenAPI GREEN**

  Run: 与 Task 1 相同的双库聚焦测试，并运行 `pnpm test:openapi`。

  Expected: SQL Server/MySQL 各自通过；OpenAPI 契约通过且响应中不存在原值字段。

### Task 3: Vue 通知偏好页接入端点管理

**Files:**
- Modify: `ui/admin/src/api/notification-platform.ts`
- Modify: `ui/admin/src/views/NotificationPreferencesView.vue`
- Modify: `ui/admin/src/views/NotificationPreferencesView.test.ts`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**
- Consumes: `listNotificationProviderProfiles` 与 Task 2 的端点 API。
- Produces: `listMyRecipientEndpoints`、`createMyRecipientEndpoint`、`deleteMyRecipientEndpoint` 以及可操作的通知偏好页。

- [x] **Step 1: 写 Vue RED**

  验证页面只展示已发布且启用、具备端点类型的 Provider；提交邮箱后请求体不含用户、租户和状态；列表只显示掩码与“待验证”；无 `PreferencesUpdate` 时不渲染登记和删除按钮。

- [x] **Step 2: 实现 API 类型与调用**

  TypeScript 请求类型只包含 `providerProfileVersionId`、`endpointKindKey`、`rawValue`；响应类型不包含 `rawValue` 或 `protectedValue`。

- [x] **Step 3: 替换诚实占位页**

  页面加载 Profile 与本人端点；允许选择已发布邮件 Profile、输入邮箱并登记，成功后立即清空输入并刷新掩码列表。明确提示“待验证端点不会参与真实投递”；删除前要求确认。

- [x] **Step 4: 运行 Vue GREEN**

  Run: `pnpm --dir ui/admin test -- NotificationPreferencesView.test.ts`

  Expected: 聚焦 Vitest 全部通过，无未处理 Promise 或 Vue 警告。

### Task 4: 收口验证与状态同步

**Files:**
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Create: `docs/verification/2026-09-01-notifications-recipient-endpoint-management.md`

**Interfaces:**
- Consumes: Task 1–3 的新鲜命令输出。
- Produces: 精确区分“端点管理 Build-verified”和“验证闭环仍开放”的证据。

- [x] **Step 1: 执行任务影响集**

  Run: `pnpm test:integration:affected:plan -- --snapshot notifications-workflow-continue-20260901 --phase inner`

  Run: `pnpm test:inner -- --snapshot notifications-workflow-continue-20260901`

  Run: `pnpm test:slice -- --snapshot notifications-workflow-continue-20260901`

- [x] **Step 2: 执行静态门禁**

  Run: `pnpm test:naming`

  Run: `pnpm test:openapi`

  Run: `pnpm test:governance`

  Run: `dotnet build Full.NET.slnx -c Release`

- [x] **Step 3: 更新状态但不夸大**

  端点 API/UI 最多标记 `Build-verified`；邮件验证码、自动升级 `verified`、QQ 生产账号认证、容量、退信和送达回执仍保持未完成。

- [x] **Step 4: 检查影响集和敏感数据**

  Run: `git diff --check`

  Run: `git status --short`

  Run: `git diff | rg -n "277897504|meyhjhagvialcicf"`

  Expected: diff 中没有邮箱账号、授权码、SMTP transcript 或无关 SQL 文件内容。
