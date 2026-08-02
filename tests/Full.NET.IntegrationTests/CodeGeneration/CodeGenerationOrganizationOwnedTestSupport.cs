using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>
/// 为组织归属预览与运行跟踪测试提供稳定的显式 Schema 请求。
/// </summary>
internal static class CodeGenerationOrganizationOwnedTestSupport
{
    internal static CodeGenerationPreviewRequest CreatePreviewRequest() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "tenant.required",
            null,
            [
                new("Id", "Id", "id", "uuid", false, null, null, null),
                new("TenantId", "TenantId", "tenantId", "uuid", false, null, null, null),
                new(
                    "OrganizationUnitId",
                    "OrganizationUnitId",
                    "organizationUnitId",
                    "uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "string",
                    false,
                    200,
                    null,
                    null),
                new("Version", "Version", "version", "int64", false, null, null, null),
                new(
                    "CreatedAtUtc",
                    "CreatedAtUtc",
                    "createdAtUtc",
                    "date.time.utc",
                    false,
                    null,
                    null,
                    null),
                new(
                    "CreatedById",
                    "CreatedById",
                    "createdById",
                    "uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "UpdatedAtUtc",
                    "UpdatedAtUtc",
                    "updatedAtUtc",
                    "date.time.utc",
                    true,
                    null,
                    null,
                    null),
                new(
                    "UpdatedById",
                    "UpdatedById",
                    "updatedById",
                    "uuid",
                    true,
                    null,
                    null,
                    null),
                new(
                    "IsDeleted",
                    "IsDeleted",
                    "isDeleted",
                    "boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "DeletedAtUtc",
                    "DeletedAtUtc",
                    "deletedAtUtc",
                    "date.time.utc",
                    true,
                    null,
                    null,
                    null),
                new(
                    "DeletedById",
                    "DeletedById",
                    "deletedById",
                    "uuid",
                    true,
                    null,
                    null,
                    null),
            ],
            new CodeGenerationEntityCapabilitiesRequest(
                "soft.delete",
                HasCreatedAudit: true,
                HasUpdatedAudit: true,
                HasDeletedAudit: true,
                HasVersion: true,
                "organization.unit"),
            "single",
            []);
}