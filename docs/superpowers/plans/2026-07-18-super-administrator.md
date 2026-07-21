# Full.NET 超级管理员实施计划

> **For Codex:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery` and test-driven development. Execute each task from RED to GREEN; do not mark the capability available until SQL Server/MySQL and both admin clients pass.

**Goal:** 将现有首次宿主管理员升级为受保护的超级管理员：默认拥有当前作用域内全部已注册权限，并对未来新增权限自动生效，同时保留租户、安全、审计和最后一名保护边界。

**Architecture:** 继续使用稳定角色 Code `host-administrator`，在系统角色上持久化 `IsSuperAdministrator`，由服务端授权目录动态投影权限。JWT 只携带受信超级管理员标记，不枚举全部权限；普通 RBAC 不改变。高风险授予/撤销通过专用领域服务完成。

**Tech Stack:** .NET 10、ASP.NET Core Authorization、Dapper、DbUp、SQL Server 2022、MySQL 8、System.Text.Json、FusionCache、Vue 3/Element Plus、Layui、MSTest、Testcontainers、Playwright。

---

## 实施进度与迁移顺序

实施时数据库真实基线只有 001-004，而命名规范化尚未开始。超级管理员角色使用 `005_SuperAdministrator.sql`，可追责审计操作者列与索引使用 `006_SuperAdministratorAuditActor.sql`。后续实际分配为：Seed 执行审计使用 007、UUID Binary16 计划使用 008/009、命名 Expand/Contract 计划使用 010/011；禁止修改已经执行或发布的迁移。

截至 2026-07-18，Task 1-3、动态授权主链、逐请求 Session/SecurityStamp/Scope 校验、Task 4 的专用服务、远程 API、当前密码重认证、事务审计与双库并发最后一名保护，以及 Task 5 的共享契约、Vue/Layui 管理页和 30 项双端 Mock E2E 已实现。Production 远程写操作按 ADR-0004 在 TOTP 强认证 Provider 与操作者已登记 TOTP 后可显式开启；双端 MFA UI、真实后端浏览器 E2E 尚未完成。当前状态仍为 `Implemented`，不能标记完整 `Verified`。

### Task 1: 建立双库角色标记和迁移恢复测试

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/005_SuperAdministrator.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/005_SuperAdministrator.sql`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentityRoleRecord.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/SuperAdministratorMigrationRecoveryTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Identity/SuperAdministratorPersistenceTests.cs`

1. 先增加失败测试：旧结构升级后 `fn_identity_role.IsSuperAdministrator` 非空且默认为 false，`host-administrator` 为 true；重复运行与半完成结构都收敛。
2. SQL Server/MySQL 均采用 expand、回填、收紧约束；不得删除现有角色权限行。
3. 投影显式选择 `IsSuperAdministrator`，不使用 `SELECT *`。
4. 运行两套真实数据库测试，预期新测试转为通过。

### Task 2: 动态权限投影与 Token 契约

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Authorization/PermissionSnapshotReader.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Authorization/FullNetPermissionHandler.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/JwtAccessTokenIssuer.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Contracts/CurrentUserResponse.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/GetCurrentUser/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Test: `tests/Full.NET.UnitTests/Identity/PermissionSnapshotReaderTests.cs`
- Test: `tests/Full.NET.UnitTests/Identity/FullNetPermissionHandlerTests.cs`
- Test: `tests/Full.NET.UnitTests/Identity/JwtAccessTokenIssuerTests.cs`

1. 先证明超级管理员无需 `fn_identity_role_permission` 行也能获得当前 Catalog 中、与有效 Host/Tenant 上下文匹配的权限。
2. 增加稳定 Claim `fullnet_super_administrator=true`；普通用户继续携带精确权限 Claim。
3. 策略处理顺序固定为：认证/会话有效、权限存在、作用域匹配、普通权限或可信超级管理员标记。
4. 未知权限、Endpoint 未声明权限和租户作用域不匹配一律拒绝；超级管理员不能成为无条件成功分支。
5. `/api/v1/me` 增加 `isSuperAdministrator`，并返回当前作用域规范权限集合；同步源生成 JSON 契约测试。

### Task 3: 升级 Bootstrap、Baseline Seed 与恢复入口

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/Bootstrap/IdentityBootstrapService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Contracts/IIdentityBootstrapService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Follow: `docs/superpowers/plans/2026-07-17-seed-data-module.md`
- Test: `tests/Full.NET.UnitTests/Identity/IdentityBootstrapServiceTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Identity/SuperAdministratorBootstrapTests.cs`

1. 先覆盖首次创建、旧库升级、重复执行、缺 Secret、普通角色同名冲突和已存在账号场景。
2. Bootstrap 幂等创建/修复唯一系统角色并分配引导账号，不再同步逐项权限；不覆盖已有密码。
3. `identity.host_administrator` Baseline Contributor 只能调用同一服务；Development/Demo/Test Overlay 不得创建额外超级管理员。
4. 恢复命令使用显式 Secret、非交互 CI 友好的退出码和完整审计；日志不得输出密码或 Token。

### Task 4: 最后一名保护和高风险管理服务

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity/Contracts/ISuperAdministratorService.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ManageSuperAdministrators/SuperAdministratorService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs`
- Test: `tests/Full.NET.UnitTests/Identity/SuperAdministratorServiceTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Identity/SuperAdministratorConcurrencyTests.cs`

1. 先写失败测试覆盖授予、撤销、禁用、删除、并发撤销最后一名和普通操作者越权。
2. 服务只接受 Host 账号，要求当前操作者为有效超级管理员，并在同一事务中锁定/复核有效数量。
3. 变更后更新 SecurityStamp、撤销目标用户 Session，并在同一事务写入包含 `ActorUserId` 的审计；当前会话判定不使用缓存，因此不产生无消费者的空 Outbox/缓存失效事件，未来引入 S0 缓存时必须在同一切片补可靠传播。
4. 系统角色不能由普通角色 CRUD 删除、改 Code、改作用域或取消标记。
5. SQL Server/MySQL 并发测试必须证明最后一名保护在线性化竞争下成立。

### Task 5: Vue/Layui 功能对等和真实链路 E2E

**Files:**
- Modify: `packages/client-contracts`
- Modify: `ui/admin/src`
- Modify: `ui/admin-layui/js`
- Test: `tests/e2e/admin-parity`
- Test: `tests/e2e/admin-real-stack`

1. 共享契约增加超级管理员标记、稳定错误码与高风险操作状态，不共享框架 UI。
2. 两端同步展示系统角色只读状态、超级管理员标识、授予/撤销二次确认和最后一名保护错误。
3. 菜单/按钮仍按服务端规范权限集合映射本地白名单，客户端不得因标记而绕过权限。
4. Mock 套件已经验证列表、审计、一次性密码重认证授予和双端行为对等；真实 API 双库测试已经覆盖授予/撤销、审计、最后一名保护和旧令牌失效。真实后端浏览器套件仍须覆盖 Cookie/CORS/刷新与页面流程后才能完成本 Task。

### Task 6: 回归、状态和发布说明

1. 运行 Release build、Unit、Architecture、Compatibility、SQL Server/MySQL Integration、Vue/Layui 单测与双端 E2E。
2. 更新 `docs/roadmap/capability-status.md`、`docs/roadmap/adminnet-feature-parity.md`、README 和验证记录；没有双库与双端证据前不得高于 `Implemented`。
3. 对外说明保留稳定角色 Code；升级脚本不删除既有显式权限行。
4. 执行依赖审计、`git diff --check`、规则/Skill 复盘和干净分支检查。

## 完成标准

- 默认引导账号和恢复账号具有动态超级管理员语义；未来注册权限无需数据同步。
- 超级管理员不绕过租户隔离、会话状态、精确权限声明、审计或高风险确认。
- 最后一名保护在 SQL Server/MySQL 并发测试成立。
- Vue/Layui 功能和真实链路 E2E 对等通过。

设计依据：[`../specs/2026-07-18-super-administrator-design.md`](../specs/2026-07-18-super-administrator-design.md)。
