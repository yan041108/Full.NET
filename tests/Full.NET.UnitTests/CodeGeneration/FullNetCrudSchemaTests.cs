using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class FullNetCrudSchemaTests
{
    [TestMethod]
    public void CreateProject_preserves_confirmed_names_and_column_order()
    {
        var schema = CreateProductSchema();

        Assert.AreEqual("acme", schema.OwnerKey);
        Assert.AreEqual("catalog", schema.ModuleKey);
        Assert.AreEqual("product", schema.EntityKey);
        Assert.AreEqual("acme_catalog_product", schema.DatabaseTableName);
        Assert.AreEqual("Acme.Modules.Catalog", schema.RootNamespace);
        Assert.AreEqual("Product", schema.ClrTypeName);
        Assert.AreEqual("products", schema.ApiResourceName);
        Assert.AreEqual("products", schema.PermissionResourceName);
        Assert.AreEqual("catalog.products.read", schema.ReadPermission);
        Assert.AreEqual("catalog.products.write", schema.WritePermission);
        Assert.AreEqual(
            FullNetCrudDataScope.TenantRequired,
            schema.DataScope);
        Assert.IsTrue(schema.IsTenantScoped);
        CollectionAssert.AreEqual(
            new[]
            {
                "Id",
                "TenantId",
                "Name",
                "Description",
                "IsActive",
                "Version",
                "CreatedAtUtc",
            },
            schema.Columns.Select(column => column.DatabaseName).ToArray());
    }

    [TestMethod]
    public void CreateProject_rejects_names_that_drift_from_the_shared_profile()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            databaseTableName: "acme_catalog_products"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            rootNamespace: "acme.modules.catalog"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            apiResourceName: "Products"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            permissionResourceName: "product-items"));
    }

    [TestMethod]
    public void CreateProject_rejects_duplicate_column_names()
    {
        var columns = ProductColumns();
        columns.Add(new(
            "DisplayName",
            "Name",
            "displayName",
            FullNetScalarType.String,
            MaxLength: 100));

        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(columns: columns));
    }

    [TestMethod]
    public void CreateProject_enforces_tenant_version_and_timeline_invariants()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            columns: ProductColumns()
                .Where(column => column.DatabaseName != "TenantId")
                .ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            columns: ProductColumns()
                .Where(column => column.DatabaseName != "Version")
                .ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            columns: ProductColumns()
                .Where(column => column.DatabaseName != "IsActive")
                .ToArray()));

        var invalidTimeline = ProductColumns();
        invalidTimeline[^1] = new(
            "CreatedAtUtc",
            "CreatedAtUtc",
            "createdAtUtc",
            FullNetScalarType.String,
            MaxLength: 64);
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            columns: invalidTimeline));
    }

    [TestMethod]
    public void CreateProject_rejects_invalid_string_length_and_json_name()
    {
        var invalidLength = ProductColumns();
        invalidLength[2] = new(
            "Name",
            "Name",
            "name",
            FullNetScalarType.String,
            MaxLength: 0);
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            columns: invalidLength));

        var invalidJson = ProductColumns();
        invalidJson[2] = new(
            "Name",
            "Name",
            "display_name",
            FullNetScalarType.String,
            MaxLength: 200);
        Assert.ThrowsExactly<ArgumentException>(() => CreateProductSchema(
            columns: invalidJson));
    }

    [TestMethod]
    public void CreateProject_legacy_false_preserves_unspecified_scope()
    {
        var schema = CreateLegacyScopeSchema(isTenantScoped: false);

        Assert.AreEqual(FullNetCrudDataScope.Unspecified, schema.DataScope);
        Assert.IsFalse(schema.IsTenantScoped);
    }

    [TestMethod]
    public void CreateProject_preserves_explicit_host_and_global_scope()
    {
        var host = CreateExplicitScopeSchema(FullNetCrudDataScope.HostOnly);
        var global = CreateExplicitScopeSchema(FullNetCrudDataScope.Global);

        Assert.AreEqual(FullNetCrudDataScope.HostOnly, host.DataScope);
        Assert.AreEqual(FullNetCrudDataScope.Global, global.DataScope);
        Assert.IsFalse(host.IsTenantScoped);
        Assert.IsFalse(global.IsTenantScoped);
    }

    [TestMethod]
    public void CreateProject_explicit_non_tenant_scope_rejects_tenant_column()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateExplicitScopeSchema(
                FullNetCrudDataScope.HostOnly,
                includeTenantId: true));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateExplicitScopeSchema(
                FullNetCrudDataScope.Global,
                includeTenantId: true));
    }

    [TestMethod]
    public void CreateProject_preserves_explicit_decimal_shape()
    {
        var columns = ProductColumns();
        columns.Insert(
            4,
            new(
                "Price",
                "Price",
                "price",
                FullNetScalarType.Decimal,
                NumericPrecision: 18,
                NumericScale: 2));

        var schema = CreateProductSchema(columns: columns);
        var price = schema.Columns.Single(column =>
            column.DatabaseName == "Price");

        Assert.AreEqual(18, price.NumericPrecision);
        Assert.AreEqual(2, price.NumericScale);
    }

    [TestMethod]
    public void CreateProject_rejects_missing_or_nonportable_decimal_shape()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateProductSchema(columns:
            [
                .. ProductColumns(),
                new(
                    "Price",
                    "Price",
                    "price",
                    FullNetScalarType.Decimal),
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateProductSchema(columns:
            [
                .. ProductColumns(),
                new(
                    "Price",
                    "Price",
                    "price",
                    FullNetScalarType.Decimal,
                    NumericPrecision: 39,
                    NumericScale: 2),
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateProductSchema(columns:
            [
                .. ProductColumns(),
                new(
                    "Price",
                    "Price",
                    "price",
                    FullNetScalarType.Decimal,
                    NumericPrecision: 18,
                    NumericScale: 19),
            ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateProductSchema(columns:
            [
                .. ProductColumns(),
                new(
                    "DisplayOrder",
                    "DisplayOrder",
                    "displayOrder",
                    FullNetScalarType.Int32,
                    NumericPrecision: 10,
                    NumericScale: 0),
            ]));
    }

    [TestMethod]
    public void CreateProject_preserves_explicit_entity_capabilities()
    {
        var capabilities = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.SoftDelete,
            HasCreatedAudit: true,
            HasUpdatedAudit: true,
            HasDeletedAudit: true,
            HasVersion: true,
            FullNetCrudOwnershipMode.OrganizationUnit);

        var schema = CreateCapabilitySchema(
            capabilities,
            CapabilityColumns());

        Assert.AreEqual(capabilities, schema.EntityCapabilities);
        Assert.IsFalse(schema.UsesLegacyEntityCapabilities);
        Assert.IsTrue(schema.HasVersion);
        Assert.IsTrue(schema.EntityCapabilities.CanUpdate);
        Assert.IsTrue(schema.EntityCapabilities.CanDelete);
    }

    [TestMethod]
    public void CreateProject_requires_columns_declared_by_entity_capabilities()
    {
        var capabilities = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.SoftDelete,
            HasCreatedAudit: true,
            HasUpdatedAudit: true,
            HasDeletedAudit: true,
            HasVersion: true,
            FullNetCrudOwnershipMode.OrganizationUnit);
        var requiredColumns = new[]
        {
            "IsDeleted",
            "DeletedAtUtc",
            "DeletedById",
            "CreatedAtUtc",
            "CreatedById",
            "UpdatedAtUtc",
            "UpdatedById",
            "Version",
            "OrganizationUnitId",
        };

        foreach (var requiredColumn in requiredColumns)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                CreateCapabilitySchema(
                    capabilities,
                    CapabilityColumns()
                        .Where(column =>
                            column.DatabaseName != requiredColumn)
                        .ToArray()));
        }
    }

    [TestMethod]
    public void CreateProject_rejects_contradictory_delete_and_immutable_capabilities()
    {
        var hardDelete = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.HardDelete,
            HasCreatedAudit: false,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: true,
            FullNetCrudOwnershipMode.None);
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                hardDelete,
                CapabilityColumns()));

        var immutable = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.Immutable,
            HasCreatedAudit: true,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: false,
            FullNetCrudOwnershipMode.None);
        Assert.IsFalse(immutable.CanUpdate);
        Assert.IsFalse(immutable.CanDelete);
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                immutable with
                {
                    HasUpdatedAudit = true,
                },
                CapabilityColumns()));

        var noUpdateAssignment = hardDelete with
        {
            HasVersion = false,
        };
        var noBusinessColumns = ExplicitBusinessColumns()
            .Where(column => column.DatabaseName is "Id" or "TenantId")
            .ToArray();
        var error = Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                noUpdateAssignment,
                noBusinessColumns));
        StringAssert.Contains(error.Message, "可更新实体");
    }

    [TestMethod]
    public void CreateProject_rejects_reserved_columns_disabled_by_explicit_capabilities()
    {
        var hardDelete = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.HardDelete,
            HasCreatedAudit: false,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: true,
            FullNetCrudOwnershipMode.None);
        var baseColumns = ProductColumns()
            .Where(column => column.DatabaseName != "CreatedAtUtc")
            .ToArray();
        var forbiddenColumns = CapabilityColumns()
            .Where(column => column.DatabaseName is
                "CreatedAtUtc"
                or "CreatedById"
                or "UpdatedAtUtc"
                or "UpdatedById"
                or "IsDeleted"
                or "DeletedAtUtc"
                or "DeletedById"
                or "OrganizationUnitId")
            .ToArray();

        foreach (var forbiddenColumn in forbiddenColumns)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                CreateCapabilitySchema(
                    hardDelete,
                    [.. baseColumns, forbiddenColumn]));
        }

        var softDeleteWithoutAudit = hardDelete with
        {
            DeleteMode = FullNetCrudDeleteMode.SoftDelete,
        };
        FullNetColumn[] softDeleteColumns =
        [
            .. baseColumns,
            forbiddenColumns.Single(column =>
                column.DatabaseName == "IsDeleted"),
        ];
        foreach (var forbiddenColumn in forbiddenColumns.Where(column =>
            column.DatabaseName is "DeletedAtUtc" or "DeletedById"))
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                CreateCapabilitySchema(
                    softDeleteWithoutAudit,
                    [.. softDeleteColumns, forbiddenColumn]));
        }

        var noVersion = hardDelete with
        {
            HasVersion = false,
        };
        var columnsWithoutVersion = baseColumns
            .Where(column => column.DatabaseName != "Version")
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                noVersion,
                [
                    .. columnsWithoutVersion,
                    baseColumns.Single(column =>
                        column.DatabaseName == "Version"),
                ]));
    }

    [TestMethod]
    public void CreateProject_defaults_to_single_scene_without_relationships()
    {
        var schema = CreateCapabilitySchema(
            new FullNetCrudEntityCapabilities(
                FullNetCrudDeleteMode.HardDelete,
                HasCreatedAudit: false,
                HasUpdatedAudit: false,
                HasDeletedAudit: false,
                HasVersion: false,
                FullNetCrudOwnershipMode.None),
            ExplicitBusinessColumns());

        Assert.AreEqual(FullNetCrudScene.Single, schema.Scene);
        Assert.HasCount(0, schema.Relationships);
    }

    [TestMethod]
    public void Stable_wire_values_pin_canonical_values_and_pre_v1_aliases()
    {
        foreach (var (canonical, alias, expected) in new[]
        {
            ("single", "Single", FullNetCrudScene.Single),
            ("tree", "Tree", FullNetCrudScene.Tree),
            ("master.detail", "MasterDetail", FullNetCrudScene.MasterDetail),
            ("many.to.many", "ManyToMany", FullNetCrudScene.ManyToMany),
        })
        {
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                canonical,
                out FullNetCrudScene canonicalValue));
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                alias,
                out FullNetCrudScene aliasValue));
            Assert.AreEqual(expected, canonicalValue);
            Assert.AreEqual(expected, aliasValue);
            Assert.AreEqual(
                canonical,
                FullNetCrudWireValues.ToWireValue(expected));
        }

        foreach (var (canonical, alias, expected) in new[]
        {
            ("hard.delete", "HardDelete", FullNetCrudDeleteMode.HardDelete),
            ("soft.delete", "SoftDelete", FullNetCrudDeleteMode.SoftDelete),
            ("immutable", "Immutable", FullNetCrudDeleteMode.Immutable),
        })
        {
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                canonical,
                out FullNetCrudDeleteMode canonicalValue));
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                alias,
                out FullNetCrudDeleteMode aliasValue));
            Assert.AreEqual(expected, canonicalValue);
            Assert.AreEqual(expected, aliasValue);
            Assert.AreEqual(
                canonical,
                FullNetCrudWireValues.ToWireValue(expected));
        }

        foreach (var (canonical, alias, expected) in new[]
        {
            ("none", "None", FullNetCrudOwnershipMode.None),
            (
                "organization.unit",
                "OrganizationUnit",
                FullNetCrudOwnershipMode.OrganizationUnit),
        })
        {
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                canonical,
                out FullNetCrudOwnershipMode canonicalValue));
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                alias,
                out FullNetCrudOwnershipMode aliasValue));
            Assert.AreEqual(expected, canonicalValue);
            Assert.AreEqual(expected, aliasValue);
            Assert.AreEqual(
                canonical,
                FullNetCrudWireValues.ToWireValue(expected));
        }

        foreach (var (canonical, alias, expected) in new[]
        {
            ("unspecified", "Unspecified", FullNetCrudDataScope.Unspecified),
            ("global", "Global", FullNetCrudDataScope.Global),
            ("host.only", "HostOnly", FullNetCrudDataScope.HostOnly),
            (
                "tenant.required",
                "TenantRequired",
                FullNetCrudDataScope.TenantRequired),
        })
        {
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                canonical,
                out FullNetCrudDataScope canonicalValue));
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                alias,
                out FullNetCrudDataScope aliasValue));
            Assert.AreEqual(expected, canonicalValue);
            Assert.AreEqual(expected, aliasValue);
            Assert.AreEqual(
                canonical,
                FullNetCrudWireValues.ToWireValue(expected));
        }

        foreach (var (canonical, alias, expected) in new[]
        {
            ("uuid", "Uuid", FullNetScalarType.Uuid),
            ("string", "String", FullNetScalarType.String),
            ("int32", "Int32", FullNetScalarType.Int32),
            ("int64", "Int64", FullNetScalarType.Int64),
            ("boolean", "Boolean", FullNetScalarType.Boolean),
            ("date.time.utc", "DateTimeUtc", FullNetScalarType.DateTimeUtc),
            ("decimal", "Decimal", FullNetScalarType.Decimal),
        })
        {
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                canonical,
                out FullNetScalarType canonicalValue));
            Assert.IsTrue(FullNetCrudWireValues.TryParse(
                alias,
                out FullNetScalarType aliasValue));
            Assert.AreEqual(expected, canonicalValue);
            Assert.AreEqual(expected, aliasValue);
            Assert.AreEqual(
                canonical,
                FullNetCrudWireValues.ToWireValue(expected));
        }

        Assert.IsFalse(FullNetCrudWireValues.TryParse(
            "SOFT.DELETE",
            out FullNetCrudDeleteMode _));
        Assert.IsFalse(FullNetCrudWireValues.TryParse(
            "tenant.Required",
            out FullNetCrudDataScope _));
    }

    [TestMethod]
    public void CreateProject_tree_scene_requires_nullable_uuid_parent_id()
    {
        var capabilities = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.HardDelete,
            HasCreatedAudit: false,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: false,
            FullNetCrudOwnershipMode.None);

        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                capabilities,
                ExplicitBusinessColumns(),
                FullNetCrudScene.Tree));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                capabilities,
                [
                    .. ExplicitBusinessColumns(),
                    new(
                        "ParentId",
                        "ParentId",
                        "parentId",
                        FullNetScalarType.Uuid),
                ],
                FullNetCrudScene.Tree));

        var schema = CreateCapabilitySchema(
            capabilities,
            [
                .. ExplicitBusinessColumns(),
                new(
                    "ParentId",
                    "ParentId",
                    "parentId",
                    FullNetScalarType.Uuid,
                    IsNullable: true),
            ],
            FullNetCrudScene.Tree);

        Assert.AreEqual(FullNetCrudScene.Tree, schema.Scene);
    }

    [TestMethod]
    public void CreateProject_relationship_scenes_require_both_sides_and_same_scope()
    {
        var capabilities = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.HardDelete,
            HasCreatedAudit: false,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: false,
            FullNetCrudOwnershipMode.None);
        var sameScope = new FullNetCrudRelationship(
            PrincipalEntityKey: "product",
            PrincipalColumnName: "Id",
            PrincipalDataScope: FullNetCrudDataScope.TenantRequired,
            DependentEntityKey: "product_item",
            DependentColumnName: "ProductId",
            DependentDataScope: FullNetCrudDataScope.TenantRequired);
        var crossScope = sameScope with
        {
            DependentDataScope = FullNetCrudDataScope.HostOnly,
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                capabilities,
                ExplicitBusinessColumns(),
                FullNetCrudScene.MasterDetail));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                capabilities,
                ExplicitBusinessColumns(),
                FullNetCrudScene.MasterDetail,
                [crossScope]));

        var schema = CreateCapabilitySchema(
            capabilities,
            ExplicitBusinessColumns(),
            FullNetCrudScene.MasterDetail,
            [sameScope]);

        Assert.AreEqual(FullNetCrudScene.MasterDetail, schema.Scene);
        Assert.AreEqual(sameScope, schema.Relationships.Single());
    }

    [TestMethod]
    public void CreateProject_many_to_many_requires_two_distinct_principal_sides()
    {
        var capabilities = new FullNetCrudEntityCapabilities(
            FullNetCrudDeleteMode.HardDelete,
            HasCreatedAudit: false,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: false,
            FullNetCrudOwnershipMode.None);
        FullNetCrudRelationship[] relationships =
        [
            new(
                "category",
                "Id",
                FullNetCrudDataScope.TenantRequired,
                "product",
                "CategoryId",
                FullNetCrudDataScope.TenantRequired),
            new(
                "tag",
                "Id",
                FullNetCrudDataScope.TenantRequired,
                "product",
                "TagId",
                FullNetCrudDataScope.TenantRequired),
        ];
        FullNetColumn[] columns =
        [
            .. ExplicitBusinessColumns(),
            new(
                "CategoryId",
                "CategoryId",
                "categoryId",
                FullNetScalarType.Uuid),
            new(
                "TagId",
                "TagId",
                "tagId",
                FullNetScalarType.Uuid),
        ];

        Assert.ThrowsExactly<ArgumentException>(() =>
            CreateCapabilitySchema(
                capabilities,
                columns,
                FullNetCrudScene.ManyToMany,
                [relationships[0]]));

        var schema = CreateCapabilitySchema(
            capabilities,
            columns,
            FullNetCrudScene.ManyToMany,
            relationships);

        Assert.AreEqual(FullNetCrudScene.ManyToMany, schema.Scene);
        Assert.HasCount(2, schema.Relationships);
    }

    internal static FullNetCrudSchema CreateProductSchema(
        string databaseTableName = "acme_catalog_product",
        string rootNamespace = "Acme.Modules.Catalog",
        string apiResourceName = "products",
        string permissionResourceName = "products",
        IReadOnlyList<FullNetColumn>? columns = null) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName,
            rootNamespace,
            clrTypeName: "Product",
            apiResourceName,
            permissionResourceName,
            isTenantScoped: true,
            hasVersion: true,
            columns ?? ProductColumns());

    private static FullNetCrudSchema CreateLegacyScopeSchema(
        bool isTenantScoped) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            isTenantScoped,
            hasVersion: true,
            columns: ProductColumns()
                .Where(column =>
                    isTenantScoped || column.DatabaseName != "TenantId")
                .ToArray());

    private static FullNetCrudSchema CreateExplicitScopeSchema(
        FullNetCrudDataScope dataScope,
        bool includeTenantId = false) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            dataScope,
            hasVersion: true,
            columns: ProductColumns()
                .Where(column =>
                    includeTenantId || column.DatabaseName != "TenantId")
                .ToArray());

    private static FullNetCrudSchema CreateCapabilitySchema(
        FullNetCrudEntityCapabilities capabilities,
        IReadOnlyList<FullNetColumn> columns,
        FullNetCrudScene scene = FullNetCrudScene.Single,
        IReadOnlyList<FullNetCrudRelationship>? relationships = null) =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            FullNetCrudDataScope.TenantRequired,
            capabilities,
            scene,
            relationships ?? [],
            columns);

    private static List<FullNetColumn> ExplicitBusinessColumns() =>
    [
        new("Id", "Id", "id", FullNetScalarType.Uuid),
        new("TenantId", "TenantId", "tenantId", FullNetScalarType.Uuid),
        new(
            "Name",
            "Name",
            "displayName",
            FullNetScalarType.String,
            MaxLength: 200),
    ];

    private static List<FullNetColumn> ProductColumns() =>
    [
        new("Id", "Id", "id", FullNetScalarType.Uuid),
        new("TenantId", "TenantId", "tenantId", FullNetScalarType.Uuid),
        new(
            "Name",
            "Name",
            "displayName",
            FullNetScalarType.String,
            MaxLength: 200),
        new(
            "Description",
            "Description",
            "description",
            FullNetScalarType.String,
            IsNullable: true,
            MaxLength: 500),
        new("IsActive", "IsActive", "isActive", FullNetScalarType.Boolean),
        new("Version", "Version", "version", FullNetScalarType.Int64),
        new(
            "CreatedAtUtc",
            "CreatedAtUtc",
            "createdAtUtc",
            FullNetScalarType.DateTimeUtc),
    ];

    private static List<FullNetColumn> CapabilityColumns() =>
    [
        .. ProductColumns(),
        new(
            "CreatedById",
            "CreatedById",
            "createdById",
            FullNetScalarType.Uuid),
        new(
            "UpdatedAtUtc",
            "UpdatedAtUtc",
            "updatedAtUtc",
            FullNetScalarType.DateTimeUtc,
            IsNullable: true),
        new(
            "UpdatedById",
            "UpdatedById",
            "updatedById",
            FullNetScalarType.Uuid,
            IsNullable: true),
        new(
            "IsDeleted",
            "IsDeleted",
            "isDeleted",
            FullNetScalarType.Boolean),
        new(
            "DeletedAtUtc",
            "DeletedAtUtc",
            "deletedAtUtc",
            FullNetScalarType.DateTimeUtc,
            IsNullable: true),
        new(
            "DeletedById",
            "DeletedById",
            "deletedById",
            FullNetScalarType.Uuid,
            IsNullable: true),
        new(
            "OrganizationUnitId",
            "OrganizationUnitId",
            "organizationUnitId",
            FullNetScalarType.Uuid),
    ];
}
