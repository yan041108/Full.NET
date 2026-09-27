using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Tests.Shared.CodeGeneration;

/// <summary>Unit预检与双库恢复测试共用输入，避免数据库启动后才发现草案Schema无效。</summary>
internal static class GeneratedMigrationRecoveryFixture
{
    public static FullNetCrudSchema CreateSchema() => FullNetCrudSchema.CreateProject(
        ownerKey: "acme", moduleKey: "catalog", entityKey: "product",
        databaseTableName: "acme_catalog_product", rootNamespace: "Acme.Modules.Catalog",
        clrTypeName: "Product", apiResourceName: "products", permissionResourceName: "products",
        isTenantScoped: true, hasVersion: false,
        columns: [new("Id", "Id", "id", FullNetScalarType.Uuid),
            new("TenantId", "TenantId", "tenantId", FullNetScalarType.Uuid),
            new("Name", "Name", "name", FullNetScalarType.String, MaxLength: 128),
            new("IsActive", "IsActive", "isActive", FullNetScalarType.Boolean)]);
}
