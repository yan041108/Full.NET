# Identity Authoritative Profile Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Host 用户资料建立服务端权威规范化、格式校验、Host 目录全局唯一性和并发冲突语义。

**Architecture:** Identity 模块在合并字段投影补丁后生成规范资料，再执行纯规则校验；`fn_identity_user_profile` 通过双库唯一索引作为并发最终裁决。资料写失败使用 `ICommandTransaction.ExecuteResultAsync` 回滚整个用户写事务，数据库唯一异常映射为稳定的字段级冲突错误码。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、DbUp、SQL Server、MySQL、System.Text.Json source generation、Microsoft Testing Platform。

## Global Constraints

- 范围仅限 Host 用户目录；不新增 Tenant 用户资料语义。
- 手机号采用规范 E.164 形状 `+?[1-9][0-9]{7,14}`；不猜测国家区号。
- Email 去首尾空白并转小写；工号和证件号码去首尾空白并转大写；证件类型转小写。
- 手机号、Email、工号分别在 Host 目录全局唯一；证件按 `(IdCardType, IdCardNumber)` 组合唯一；空值允许重复。
- `IdCardType` 与 `IdCardNumber` 必须同时为空或同时提供；支持稳定类型 `id_card`、`passport`、`hk_macau_pass`、`taiwan_pass`、`military_id`、`other`。
- `id_card` 使用中国居民身份证 18 位校验码和出生日期校验；其他证件使用有界 ASCII 机器可比格式。
- 迁移不得静默删除或合并历史重复数据；规范化后存在冲突时失败关闭，由运维先修复数据。
- SQL Server 与 MySQL 必须具备等价迁移、恢复与真实 API 验证；Host.Api 可达代码必须保持 Native AOT 静态闭包。

---

### Task 1: 纯规则规范化与校验

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserProfilePolicy.cs`
- Test: `tests/Full.NET.UnitTests/Identity/HostUserProfilePolicyTests.cs`

**Interfaces:**
- Produces: `HostUserProfilePolicy.NormalizeAndValidate(HostUserProfileWriteRequest)`，成功返回规范请求，失败返回稳定 `Result`。

- [x] 写手机号、Email、工号、证件配对、身份证校验码和其他证件字符边界的失败测试。
- [x] 运行聚焦测试并确认因为策略类型缺失而失败。
- [x] 实现无反射、无动态正则的最小纯函数策略。
- [x] 运行聚焦测试并确认全部通过。

### Task 2: 写事务与稳定冲突语义

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserProfileMapper.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity.Contracts/IdentityErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.resx`
- Modify: `src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.en-US.resx`
- Test: `tests/Full.NET.UnitTests/Identity/HostUserProfileMapperTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentityUserManagementAssertions.cs`

**Interfaces:**
- Consumes: Task 1 的规范化结果。
- Produces: `identity.users.profile_invalid`、`identity.users.phone_number_exists`、`identity.users.email_exists`、`identity.users.employee_number_exists`、`identity.users.id_card_exists`。

- [x] 先增加无效资料返回 400、重复资料返回 409、失败创建不残留用户、失败更新不推进基础资料版本的测试。
- [x] 运行 Unit/API 聚焦测试并确认按预期失败。
- [x] 将 Create/Update 切换为 `ExecuteResultAsync`，在合并后校验规范资料，并捕获 `DataCommandException.UniqueConstraint`。
- [x] 唯一异常后按规范值查询冲突字段，返回精确稳定错误码；无法识别的唯一异常继续抛出。
- [x] 运行聚焦测试并确认通过。

### Task 3: 双库唯一索引与恢复

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/101_IdentityHostUserProfileAuthority.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/101_IdentityHostUserProfileAuthority.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration101IdentityHostUserProfileAuthorityRecoveryTests.cs`
- Modify: `contracts/database/object-comments.json` only if the migration adds a database object requiring catalog metadata.

**Interfaces:**
- Produces: filtered SQL Server unique indexes and NULL-compatible MySQL unique indexes for phone, email, employee number, and card pair.

- [x] 先写双库测试，覆盖规范化回填、空值重复、重复阻止、缺失索引重建和畸形索引替换。
- [x] 运行迁移聚焦测试并确认 101 脚本缺失导致失败。
- [x] 实现可重入迁移：先规范化非空历史值，再探测重复并失败关闭，最后收敛索引形状。
- [x] 运行双库迁移恢复测试并确认通过。

### Task 4: 契约、Native AOT 与交付记录

**Files:**
- Modify: `contracts/openapi/identity-host-users-v1.json`（若生成器确认运行时契约有变化）
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Create: `docs/verification/2026-08-30-identity-authoritative-profile-validation.md`

**Interfaces:**
- Consumes: Tasks 1-3 的实现和验证证据。
- Produces: 可审计的 Build-verified 状态；生产真实栈认证前不提升为 Verified。

- [x] 运行 OpenAPI check、错误码本地化、Naming、Governance 与 AOT analyzers。
- [x] 用任务基线运行 inner 影响集，并单独执行 SQL Server/MySQL API 与迁移恢复用例。
- [ ] 运行 Linux Native AOT publish 和 Identity 原生进程聚焦验证（publish 已完成；Linux 原生进程留给 CI）。
- [x] 更新唯一测试矩阵、路线图和验证记录，执行 `git diff --check` 与 `git status`。
