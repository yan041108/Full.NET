# Notifications SMTP Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付首个可配置的真实邮件渠道 Provider，并用 QQ SMTP 465/SSL 完成不落盘 Secret 的自发自收验证。

**Architecture:** SMTP Adapter 留在 `Full.NET.Modules.Notifications` 主项目的 `Providers/Smtp` 垂直切片中；只有 `Notifications:Providers:Smtp:Enabled=true` 时才进入闭合 Provider 目录。Worker 在事务外解析已发布 Profile 的非密钥配置、通过 `env://<NAME>` 引用读取授权码、解密与该 ProfileVersion 绑定的已验证邮箱端点，再调用 MailKit；API 只展示受控 Provider Schema，不启动投递循环。

**Tech Stack:** .NET 10、MailKit 4.17.0、MimeKit、Dapper、ASP.NET Core Data Protection、MSTest、SQL Server/MySQL。

## Global Constraints

- ProviderTypeKey 固定为 `email.smtp`，ChannelKey 与 EndpointKindKey 固定为 `email`。
- QQ SMTP 使用 `smtp.qq.com:465` 与 `ssl_on_connect`；授权码只能通过运行时环境变量引用，不得进入仓库、数据库明文字段、日志、异常响应或测试结果。
- SMTP 成功只表示服务器接受投递，状态推进到 `sent`；该协议切片没有可信送达/已读回执，`ReceiptModeKey=none`。
- 非密钥字段闭合为 `host`、`port`、`secureSocketMode`、`username`、`fromAddress`、`fromDisplayName`；未知字段、非法端口、非 TLS 模式和非法邮箱失败关闭。
- 只有 `verified` 且与当前 `TenantScopeKey + UserId + ProviderProfileVersionId + email` 精确匹配的收件端点可被解密使用；无端点不得回退到用户 GUID 或其他 Profile 地址。
- MailKit/MimeKit 为 MIT；固定中央版本并更新 `THIRD-PARTY-NOTICES`。不得关闭证书验证或记录 SMTP 协议正文。
- 外部 SMTP I/O 保持在数据库事务外；取消向上传播，认证/地址错误为永久失败，网络/TLS/4xx 服务错误为瞬时失败。
- 本切片不新增迁移，不实现附件、HTML、抄送/密送、批量同连接复用、营销邮件、退订或 SMTP DSN。

---

### Task 1: Worker 请求与安全端点解析

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Notifications/Providers/INotificationProviderAdapter.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Execution/NotificationDeliveryBatchProcessor.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationRecipientEndpointSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationsDapperAotMaterializerContributor.cs`
- Modify: `tests/Full.NET.IntegrationTests/Notifications/TestNotificationProvider.cs`
- Test: `tests/Full.NET.IntegrationTests/Notifications/NotificationDeliveryWorkerAssertions.cs`
- Test: `tests/Full.NET.ArchitectureTests/NativeAotStaticBindingRulesTests.cs`

**Interfaces:**
- Consumes: `NotificationProviderProfileVersionRecord.NonSecretConfigJson/SecretReference`、`NotificationRecipientEndpointProtector`。
- Produces: 带 `NonSecretConfigJson`、`SecretReference` 的 `NotificationProviderRequest`，以及 Adapter 声明的可选 `RecipientEndpointKindKey`。

- [x] **Step 1: 写双库失败回归**：增加一个测试 Adapter，声明 `RecipientEndpointKindKey=email`；不登记端点时断言不调用 Adapter，登记错误 Profile/pending 端点仍不调用，登记当前 ProfileVersion 的 verified 端点后只收到解密邮箱原值。
- [x] **Step 2: 运行 Notifications SQL Server/MySQL 聚焦测试并确认 RED**：失败原因必须是 Worker 仍把 `RecipientKey` 直接交给 Adapter或请求缺少 Profile 配置。
- [x] **Step 3: 实现最小安全读取**：新增精确参数化 SQL 读取一个受保护端点，注册 AOT 读模型；Worker 只对声明端点类型的 Adapter 读取并解密，随后把 Profile 配置和 Secret Reference 交给 Adapter。
- [x] **Step 4: 重跑双库测试与 AOT 静态投影测试并确认 GREEN**。

### Task 2: SMTP Adapter、Secret Resolver 与受控注册

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Modules/Full.NET.Modules.Notifications/Full.NET.Modules.Notifications.csproj`
- Modify: `src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Providers/Smtp/INotificationSecretResolver.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Providers/Smtp/SmtpNotificationProviderAdapter.cs`
- Create: `src/Modules/Full.NET.Modules.Notifications/Providers/Smtp/MailKitSmtpTransport.cs`
- Test: `tests/Full.NET.UnitTests/Notifications/SmtpNotificationProviderAdapterTests.cs`
- Test: `tests/Full.NET.UnitTests/Notifications/NotificationProviderTypeCatalogTests.cs`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: `env://<NAME>` Secret Reference、`NotificationProviderRequest`。
- Produces: `email.smtp` Provider 描述符和 `NotificationProviderResult`；MailKit 类型不进入公共 Contracts。

- [x] **Step 1: 写 Adapter RED**：覆盖目录 Schema、合法配置成功、Secret 缺失、非法 JSON/端口/TLS/邮箱、认证永久失败、网络瞬时失败与取消传播。
- [x] **Step 2: 运行 SMTP 聚焦 Unit 并确认因类型/注册不存在而 RED**。
- [x] **Step 3: 中央锁定 MailKit 4.17.0 并登记 MIT 声明；实现只接受环境变量引用的 Secret Resolver、闭合配置解析、MimeKit 文本邮件与 `SslOnConnect/StartTls` 显式映射、异常分类和资源释放。**
- [x] **Step 4: 通过 `Notifications:Providers:Smtp:Enabled` 在 API/Worker 使用同一静态注册方法；默认 false 保持未启用环境的空目录语义。**
- [x] **Step 5: 重跑 SMTP Unit、Notifications Unit、许可证/Architecture 与 Host.Api/Worker AOT 分析。**

### Task 3: QQ SMTP 真实自发自收验证

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Notifications/SmtpNotificationProviderExternalTests.cs`

**Interfaces:**
- Consumes: `FULLNET_TEST_SMTP_HOST/PORT/USERNAME/PASSWORD/RECIPIENT` 进程环境变量。
- Produces: 一封主题含唯一 UTC/Guid 标识的纯文本测试邮件；测试输出仅包含是否 Accepted，不输出地址、授权码或协议 transcript。

- [x] **Step 1: 添加环境门控外部测试**：凭据未提供时 Inconclusive；提供时构造 `email.smtp` 请求并发送给显式收件地址。
- [ ] **Step 2: 首次运行确认测试在 Provider 尚未实现时 RED。**
- [x] **Step 3: 通过交互式进程环境注入用户给出的 QQ 参数，收件地址默认与发件账号相同；执行一次外部测试。**
- [x] **Step 4: 若认证或 TLS 失败，只报告稳定失败类别并保留服务端响应摘要，禁止输出授权码；按失败证据修复后重试。**（TLS 连接成功，QQ 在认证阶段拒绝；分类缺陷已回归修复，账号认证仍未通过。）

### Task 4: 收口验证与状态更新

**Files:**
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Create: `docs/verification/2026-08-31-notifications-smtp-provider.md`
- Modify: `eng/testing/test-matrix.json`（仅当最低测试发现数确实变化）

**Interfaces:**
- Consumes: Task 1–3 的新鲜验证证据。
- Produces: 精确区分 Adapter `Build-verified`、QQ SMTP 外部连接实测和未完成生产容量/送达回执的状态记录。

- [x] **Step 1: 运行 `pnpm test:integration:affected:plan -- --snapshot notifications-smtp-provider-20260831 --phase inner` 与 inner。**（选择器确认 inner 无执行目标。）
- [x] **Step 2: 运行同一快照 slice，覆盖 SQL Server/MySQL；运行 `pnpm test:naming`、`pnpm test:openapi`、治理、Release build、Host.Api/Worker AOT 分析。**
- [x] **Step 3: 更新现有路线图和新验证记录；邮件渠道最多标为 `Build-verified`，QQ 单账号成功不得外推为生产容量、送达或多租户认证。**
- [x] **Step 4: 运行 `git diff --check`、`git status` 并确认没有 Secret、临时凭据文件、SMTP transcript 或无关改动。**
