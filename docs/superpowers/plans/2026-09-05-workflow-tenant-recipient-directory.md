# Workflow Tenant 本地收件人候选目录实施计划

> **状态：** Implemented，等待 GitHub Actions 双库与 Native AOT 门禁  
> **任务基线：** `1f5107fcfc91f25207a136ee4348d4dae4580930`  
> **任务快照：** `workflow-tenant-recipient-directory-20260905`

## 目标

关闭 Workflow 设计器在 Tenant 作用域仍枚举全部 Host 活动用户的边界缺口。Host 定义继续使用 Host 用户目录；Tenant 定义只能分页选择当前可信租户内的活动用户，并在发布 `notify.cc` 定义前批量复验所有收件人仍属于该租户且处于活动状态。

本切片不实现角色/组织负责人审批、会签/或签、转办、加签，不新增数据库表或迁移，也不改变 `human.approval` 当前的自办语义。

## 设计边界

- Identity 拥有用户、角色和用户角色关系，新增最小 `ITenantUserSelectionDirectory` Contract；Workflow 禁止读取 Identity 表。
- Tenant 候选包括：直接属于该 Tenant 的活动用户，以及通过当前 Tenant 活动角色获得 Tenant 上下文的活动 Host 用户。
- TenantId 只来自 `ICurrentTenant`；HTTP 请求不得提交 TenantId 作为权威输入。
- 列表和发布校验均使用批量查询，禁止逐收件人跨模块 N+1。
- 分页使用 SQL Server/MySQL 成对 Statement，稳定按规范化用户名和用户 Id 排序。
- 既有 `/api/v1/workflow/definitions/recipient-candidates` 契约保持不变，Vue 设计器无需感知目录来源。

## Task 1：Identity Tenant 用户目录 Contract 与实现

**文件：**

- 新建 `src/Modules/Full.NET.Modules.Identity.Contracts/ITenantUserSelectionDirectory.cs`
- 新建 `src/Modules/Full.NET.Modules.Identity/HostUsers/TenantUserSelectionDirectory.cs`
- 修改 `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- 修改 `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityDomainServiceCollectionExtensions.cs`
- 修改 `tests/Full.NET.UnitTests/Identity/IdentityHostUserDirectoryTests.cs` 或新增聚焦目录测试

**RED：**

- [x] Tenant 分页目录只返回直接 Tenant 用户和拥有当前 Tenant 活动角色的 Host 用户。
- [x] 禁用用户、其他 Tenant 用户、仅拥有其他 Tenant 角色的 Host 用户全部排除。
- [x] 批量校验去重输入，并只返回当前 Tenant 的活动用户。
- [x] 分页参数收敛到 `page >= 1`、`1 <= pageSize <= 100`。

**GREEN：**

- [x] 增加双 Provider 分页 Statement、总数 Statement 和批量 Id Statement。
- [x] 使用稳定排序和参数化 `TenantId`/`TenantScopeKey`，不接受任意 ScopeKey。
- [x] 注册 Scoped Contract；补齐中文 XML 注释和关键作用域逻辑注释。

## Task 2：Workflow 按可信作用域选择目录并批量验证发布引用

**文件：**

- 修改 `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/Endpoint.cs`
- 修改 `src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowDefinitionManagementService.cs`
- 修改 `tests/Full.NET.UnitTests/Workflow/WorkflowDefinitionManagementServiceTests.cs`
- 修改 `tests/Full.NET.UnitTests/Workflow/WorkflowNodeTypeCatalogTests.cs`

**RED：**

- [x] Host 请求仍调用 `IHostUserSelectionDirectory`。
- [x] Tenant 请求只调用 `ITenantUserSelectionDirectory`，且 TenantId 来自 `ICurrentTenant`。
- [x] Tenant 发布一次批量复验全部 `notify.cc` 收件人；任一跨租户或停用用户使发布返回 `workflow.definition.cc_recipients_invalid`。
- [x] Host 发布保持现有活动 Host 用户语义。

**GREEN：**

- [x] 抽取受控候选目录服务，Endpoint 不自行判断不可信作用域。
- [x] 发布校验从逐用户查询改为按作用域的一次批量查询，保持事务外只读校验和 Workflow 本地事务边界。
- [x] 保持现有 HTTP 路径、响应 JSON、权限码和 OpenAPI operationId 不变。

## Task 3：双库 API 验收与客户端契约回归

**文件：**

- 修改 `tests/Full.NET.IntegrationTests/Workflow/WorkflowApiAssertions.cs`
- 修改 `tests/Full.NET.IntegrationTests/Workflow/WorkflowRuntimeApiAssertions.cs`（仅在复用发布夹具确有需要时）
- 修改或确认 `ui/admin/src/api/workflow-definitions.test.ts`
- 修改或确认 `ui/admin/src/workflow/WorkflowVue3Designer.test.ts`

**RED：**

- [x] SQL Server/MySQL 的共享 Tenant 断言覆盖不返回仅属于 Host 的用户；真实双库执行等待 Actions。
- [x] 当前 Tenant 的活动用户可被设计器读取并可发布为 `notify.cc` 收件人。
- [x] 非当前 Tenant 收件人不能发布；Host 同路径仍能读取 Host 候选。
- [x] Vue 继续消费相同分页响应，无需携带 TenantId。

**GREEN：**

- [x] 补齐共享双库断言并进入 `api-sqlserver` / `api-mysql` Actions 分片。
- [x] 客户端无需生产改动，既有契约测试保持分页响应与无 TenantId 请求。

## Task 4：验证、文档与提交

- [x] `dotnet build Full.NET.slnx --no-restore`
- [x] Workflow/Identity 聚焦 Unit 与 Architecture 测试非零通过。
- [x] `pnpm --dir ui/admin exec vitest run` 聚焦 Workflow 定义/设计器测试通过。
- [x] `pnpm test:integration:affected:plan -- --snapshot workflow-tenant-recipient-directory-20260905 --phase slice` 确认双库影响集。
- [x] `git diff --check`、`git status`、Layui 零改动。
- [x] 新建 `docs/verification/2026-09-05-workflow-tenant-recipient-directory.md`，按新鲜证据更新 Workflow 路线图，但不提升为 `Verified`。
- [ ] 提交、推送后核对 GitHub Actions 的 CI、API Native AOT 和 Worker Native AOT；本切片相关失败必须修复。

## 停止条件

- 若 Tenant 用户归属无法由 Identity 的用户/角色权威数据确定，停止实现并回到 Spec 明确数据所有权，禁止跨模块 JOIN Organization 表。
- 若需要修改公开 HTTP 契约、增加 TenantId 请求参数或新增数据库结构，必须先更新计划并重新审查授权边界。
- 若候选目录只能通过逐用户查询实现，停止并改为批量 Contract，不接受 N+1。
