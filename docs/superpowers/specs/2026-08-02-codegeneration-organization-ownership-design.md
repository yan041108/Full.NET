# CodeGeneration 组织归属（OrganizationUnit）生成设计

**状态：** Approved for implementation  
**日期：** 2026-08-02  
**基线：** `main` @ `2006106`  
**上游建议稿：** [codegeneration-organization-ownership-assessment-2026-08-02.md](../../verification/codegeneration-organization-ownership-assessment-2026-08-02.md)

## 1. 决策摘要

在 `Organization.Contracts` 提供 **`IOrganizationOwnedEntityWriteAuthorizer`**（Organization 模块实现），生成器对 `FullNetCrudOwnershipMode.OrganizationUnit` 解除 fail-closed，并在 Feature 层于 Create/Update/Delete 前校验 actor 对目标机构的写入权。`OrganizationUnitId` 永不进入客户端可写契约；列表/分页 SQL 复用 Identity `IDataScopeSqlFilterBuilder` 组织过滤。

首切片仅 `Single` + 既有生命周期 DeleteMode（SoftDelete 优先运行时矩阵）；Tree/关系场景继续 fail-closed。

## 2. 写入授权端口

```csharp
// Organization.Contracts
public interface IOrganizationOwnedEntityWriteAuthorizer
{
    Task<Result> EnsureCanWriteAsync(
        Guid tenantId,
        Guid organizationUnitId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
```

- 实现位于 Organization 模块：结合用户-机构隶属与 Identity 有效数据范围；跨租户、未知机构、无写入权返回稳定错误码（新增 `organization.write_access.denied`）。
- 生成 Feature 注入该端口；禁止把请求 DTO 中的 `OrganizationUnitId` 作为授权事实（列由服务端在 Create 时从受信上下文绑定，或从已存行读取）。

## 3. 生成与 SQL

- Schema 必须声明 `OrganizationUnitId` 列；迁移模板与 DML 与 TenantRequired 一致追加 `TenantId` 谓词。
- 列表/Count 在显式 ownership 时追加 `IDataScopeSqlFilterBuilder.BuildOrganizationUnitFilter` 片段（列名来自 Schema，非请求输入）。
- 解除 `CrudArtifactGenerator.EnsureSupportedExplicitCapabilities` 中对 `OrganizationUnit` 的 `NotSupportedException`。

## 4. 验收

- Organization 端口 Unit + 双库（若涉及 SQL）测试 GREEN。
- 生成器 Unit 更新：组织归属产物含授权调用；客户端契约无 `OrganizationUnitId` 写入字段。
- Integration 运行时矩阵（参照 lifecycle 支持类）双库 SoftDelete 组织归属样例 GREEN。
- 不标 `Verified`；保持 `Build-verified` 直至双端/E2E 齐备。

## 5. 非目标

Tree/MasterDetail/ManyToMany、Host/Global 与组织列组合、HTTP 产品面变更。