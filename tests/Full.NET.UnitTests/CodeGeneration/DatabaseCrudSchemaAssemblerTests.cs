using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class DatabaseCrudSchemaAssemblerTests
{
    [TestMethod]
    public void Assemble_valid_metadata_returns_a_validated_crud_schema()
    {
        var schema = DatabaseCrudSchemaAssembler.Assemble(
            DatabaseMetadataProvider.SqlServer,
            ProductOptions(),
            ProductColumns(),
            [new("Id", 1)]);

        Assert.AreEqual("acme_catalog_product", schema.DatabaseTableName);
        Assert.AreEqual("catalog.products.read", schema.ReadPermission);
        Assert.AreEqual("catalog.products.write", schema.WritePermission);
        Assert.IsTrue(schema.IsTenantScoped);
        Assert.IsTrue(schema.HasVersion);
        Assert.IsTrue(schema.UsesLegacyEntityCapabilities);
        CollectionAssert.AreEqual(
            new[]
            {
                "Id",
                "TenantId",
                "Name",
                "IsActive",
                "Version",
                "CreatedAtUtc",
            },
            schema.Columns.Select(column => column.DatabaseName).ToArray());
    }

    [TestMethod]
    public void Assemble_rejects_a_missing_table()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseCrudSchemaAssembler.Assemble(
                DatabaseMetadataProvider.SqlServer,
                ProductOptions(),
                [],
                []));
    }

    [TestMethod]
    public void Assemble_rejects_a_table_without_an_id_primary_key()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseCrudSchemaAssembler.Assemble(
                DatabaseMetadataProvider.SqlServer,
                ProductOptions(),
                ProductColumns(),
                []));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseCrudSchemaAssembler.Assemble(
                DatabaseMetadataProvider.SqlServer,
                ProductOptions(),
                ProductColumns(),
                [new("TenantId", 1)]));
    }

    [TestMethod]
    public void Assemble_rejects_a_composite_primary_key()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseCrudSchemaAssembler.Assemble(
                DatabaseMetadataProvider.SqlServer,
                ProductOptions(),
                ProductColumns(),
                [
                    new("Id", 1),
                    new("TenantId", 2),
                ]));
    }

    [TestMethod]
    public void Assemble_preserves_explicit_host_scope_without_tenant_column()
    {
        var options = new DatabaseCrudImportOptions(
            OwnerKey: "acme",
            ModuleKey: "catalog",
            EntityKey: "product",
            RootNamespace: "Acme.Modules.Catalog",
            ClrTypeName: "Product",
            ApiResourceName: "products",
            PermissionResourceName: "products",
            DataScope: FullNetCrudDataScope.HostOnly,
            HasVersion: true);
        var columns = ProductColumns()
            .Where(column => column.Name != "TenantId")
            .Select((column, index) => column with
            {
                OrdinalPosition = index + 1,
            })
            .ToArray();

        var schema = DatabaseCrudSchemaAssembler.Assemble(
            DatabaseMetadataProvider.SqlServer,
            options,
            columns,
            [new("Id", 1)]);

        Assert.AreEqual(FullNetCrudDataScope.HostOnly, schema.DataScope);
        Assert.IsFalse(schema.IsTenantScoped);
    }

    [TestMethod]
    public void Assemble_preserves_explicit_entity_capabilities()
    {
        var capabilities = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.SoftDelete,
            HasCreatedAudit: true,
            HasUpdatedAudit: true,
            HasDeletedAudit: true,
            HasVersion: true,
            FullNetCrudOwnershipMode.OrganizationUnit);
        var options = new DatabaseCrudImportOptions(
            OwnerKey: "acme",
            ModuleKey: "catalog",
            EntityKey: "product",
            RootNamespace: "Acme.Modules.Catalog",
            ClrTypeName: "Product",
            ApiResourceName: "products",
            PermissionResourceName: "products",
            DataScope: FullNetCrudDataScope.TenantRequired,
            EntityCapabilities: capabilities);

        var schema = DatabaseCrudSchemaAssembler.Assemble(
            DatabaseMetadataProvider.SqlServer,
            options,
            CapabilityColumns(),
            [new("Id", 1)]);

        Assert.AreEqual(capabilities, schema.EntityCapabilities);
        Assert.IsFalse(schema.UsesLegacyEntityCapabilities);
    }

    [TestMethod]
    public void Import_options_freeze_capability_source_state()
    {
        foreach (var propertyName in new[]
        {
            nameof(DatabaseCrudImportOptions.HasVersion),
            nameof(DatabaseCrudImportOptions.EntityCapabilities),
            nameof(DatabaseCrudImportOptions.UsesLegacyEntityCapabilities),
        })
        {
            var property = typeof(DatabaseCrudImportOptions)
                .GetProperty(propertyName);

            Assert.IsNotNull(property);
            Assert.IsNull(
                property.SetMethod,
                $"{propertyName} 不得向调用方公开 init/set 写入口。");
        }
    }

    internal static DatabaseCrudImportOptions ProductOptions() =>
        new(
            OwnerKey: "acme",
            ModuleKey: "catalog",
            EntityKey: "product",
            RootNamespace: "Acme.Modules.Catalog",
            ClrTypeName: "Product",
            ApiResourceName: "products",
            PermissionResourceName: "products",
            IsTenantScoped: true,
            HasVersion: true);

    internal static DatabaseColumnMetadata[] ProductColumns() =>
    [
        new("Id", "uniqueidentifier", "uniqueidentifier", false, null, 1),
        new("TenantId", "uniqueidentifier", "uniqueidentifier", false, null, 2),
        new("Name", "nvarchar", "nvarchar(200)", false, 200, 3),
        new("IsActive", "bit", "bit", false, null, 4),
        new("Version", "bigint", "bigint", false, null, 5),
        new("CreatedAtUtc", "datetimeoffset", "datetimeoffset(7)", false, null, 6),
    ];

    private static DatabaseColumnMetadata[] CapabilityColumns() =>
    [
        .. ProductColumns(),
        new(
            "CreatedById",
            "uniqueidentifier",
            "uniqueidentifier",
            false,
            null,
            7),
        new(
            "UpdatedAtUtc",
            "datetimeoffset",
            "datetimeoffset(7)",
            true,
            null,
            8),
        new(
            "UpdatedById",
            "uniqueidentifier",
            "uniqueidentifier",
            true,
            null,
            9),
        new("IsDeleted", "bit", "bit", false, null, 10),
        new(
            "DeletedAtUtc",
            "datetimeoffset",
            "datetimeoffset(7)",
            true,
            null,
            11),
        new(
            "DeletedById",
            "uniqueidentifier",
            "uniqueidentifier",
            true,
            null,
            12),
        new(
            "OrganizationUnitId",
            "uniqueidentifier",
            "uniqueidentifier",
            false,
            null,
            13),
    ];
}
