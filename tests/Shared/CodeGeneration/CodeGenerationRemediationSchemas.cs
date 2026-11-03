using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Tests.CodeGeneration;

/// <summary>冻结整改场景的生成输入，供真实生成代码与双库验收共同使用。</summary>
public static class CodeGenerationRemediationSchemas
{
    /// <summary>真实树写入的双会话环检测与叶子删除验证输入。</summary>
    public static FullNetCrudSchema TreeNode() => Entity("tree_node", "TreeNode",
        [Text("Name"), Uuid("ParentId") with { IsNullable = true }], scene: FullNetCrudScene.Tree);

    /// <summary>同租户仍须遵守组织边界，不能把另一机构节点用作父节点。</summary>
    public static FullNetCrudSchema OrganizationTreeNode() => Entity("organization_tree_node", "OrganizationTreeNode",
        [Text("Name"), Uuid("ParentId") with { IsNullable = true }], scene: FullNetCrudScene.Tree, ownership: FullNetCrudOwnershipMode.OrganizationUnit);

    /// <summary>同组织关系使用真实主从表，外键 CLR 名称与物理名不同。</summary>
    public static FullNetCrudSchema OrganizationParent()
    {
        var child = Entity("organization_child", "OrganizationChild", [Text("Name"), Uuid("ParentId") with { ClrPropertyName = "ParentReferenceId", JsonPropertyName = "parentReferenceId" }],
            ownership: FullNetCrudOwnershipMode.OrganizationUnit);
        return Entity("organization_parent", "OrganizationParent", [Text("Name")], FullNetCrudScene.MasterDetail,
            [new("organization_parent", "Id", FullNetCrudDataScope.TenantRequired, "organization_child", "ParentId",
                FullNetCrudDataScope.TenantRequired, ["ParentId", "Id"], true, DependentSchema: child)], ownership: FullNetCrudOwnershipMode.OrganizationUnit);
    }

    /// <summary>授权写入当前组织不代表可以引用其他组织的主端。</summary>
    public static FullNetCrudSchema OrganizationLink()
    {
        var source = Entity("organization_source", "OrganizationSource", [Text("Name")], ownership: FullNetCrudOwnershipMode.OrganizationUnit);
        var target = Entity("organization_target", "OrganizationTarget", [Text("Name")], ownership: FullNetCrudOwnershipMode.OrganizationUnit);
        return Entity("organization_link", "OrganizationLink", [Uuid("SourceId"), Uuid("TargetId")], FullNetCrudScene.ManyToMany,
            [new("organization_source", "Id", FullNetCrudDataScope.TenantRequired, "organization_link", "SourceId", FullNetCrudDataScope.TenantRequired,
                ["SourceId", "TargetId"], false, PrincipalSchema: source),
             new("organization_target", "Id", FullNetCrudDataScope.TenantRequired, "organization_link", "TargetId", FullNetCrudDataScope.TenantRequired,
                ["SourceId", "TargetId"], false, PrincipalSchema: target)], ownership: FullNetCrudOwnershipMode.OrganizationUnit);
    }

    /// <summary>单表工作台覆盖字符串大数、可空布尔、UUID 与微秒时间，范围过滤使用冻结类型。</summary>
    public static FullNetCrudSchema Workbench() => Entity("workbench", "Workbench",
    [
        Text("Name"),
        new("Quantity", "Quantity", "quantity", FullNetScalarType.Int64),
        new("Price", "Price", "price", FullNetScalarType.Decimal, NumericPrecision: 18, NumericScale: 4,
            Ui: FullNetColumnUi.DefaultFor("Price", FullNetScalarType.Decimal, false) with { QueryKind = FullNetColumnQueryKind.Range }),
        new("IsReady", "IsReady", "isReady", FullNetScalarType.Boolean, IsNullable: true),
        new("ReferenceId", "ReferenceId", "referenceId", FullNetScalarType.Uuid, IsNullable: true),
        new("OccurredAtUtc", "OccurredAtUtc", "occurredAtUtc", FullNetScalarType.DateTimeUtc, IsNullable: true),
    ], resourceName: "workbenches");

    /// <summary>从编译输入定位仓库，支持测试产物位于独立系统临时目录。</summary>
    public static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("无法从测试输入定位 Full.NET 仓库。");
    }

    /// <summary>主实体有两个异类型唯一列、decimal 和带审计软删除明细。</summary>
    public static FullNetCrudSchema Product()
    {
        var child = Entity("product_item", "ProductItem", [Uuid("ProductId"), Text("Description")]);
        return Entity("product", "Product",
            [Text("Name", unique: true), new("Number", "Number", "number", FullNetScalarType.Int32,
                Ui: FullNetColumnUi.DefaultFor("Number", FullNetScalarType.Int32, false) with { Unique = true }),
             new("Price", "Price", "price", FullNetScalarType.Decimal, NumericPrecision: 8, NumericScale: 2),
             new("OptionalCode", "OptionalCode", "optionalCode", FullNetScalarType.String, IsNullable: true, MaxLength: 64,
                Ui: FullNetColumnUi.DefaultFor("OptionalCode", FullNetScalarType.String, true) with { Unique = true })],
            FullNetCrudScene.MasterDetail,
            [new("product", "Id", FullNetCrudDataScope.TenantRequired, "product_item", "ProductId",
                FullNetCrudDataScope.TenantRequired, ["ProductId", "Id"], true, DependentSchema: child)]);
    }

    /// <summary>关联实体以两个主端构成业务唯一键，不能只靠代理 Id 排重。</summary>
    public static FullNetCrudSchema ProductTag()
    {
        var source = Product();
        var product = FullNetCrudSchema.CreateProject(source.OwnerKey, source.ModuleKey, source.EntityKey,
            source.DatabaseTableName, source.RootNamespace, source.ClrTypeName, source.ApiResourceName,
            source.PermissionResourceName, source.DataScope, source.EntityCapabilities, source.Columns);
        var tag = Entity("tag", "Tag", [Text("Name")]);
        return Entity("product_tag", "ProductTag", [Uuid("ProductId") with { ClrPropertyName = "ProductReferenceId", JsonPropertyName = "productReferenceId" }, Uuid("TagId")],
            FullNetCrudScene.ManyToMany,
            [new("product", "Id", FullNetCrudDataScope.TenantRequired, "product_tag", "ProductId",
                FullNetCrudDataScope.TenantRequired, ["ProductId", "TagId"], false, PrincipalSchema: product),
             new("tag", "Id", FullNetCrudDataScope.TenantRequired, "product_tag", "TagId",
                FullNetCrudDataScope.TenantRequired, ["ProductId", "TagId"], false, PrincipalSchema: tag)]);
    }

    /// <summary>取得主实体声明的真实从端输入。</summary>
    public static FullNetCrudSchema ProductItem() => Product().Relationships[0].DependentSchema!;

    private static FullNetCrudSchema Entity(string key, string type, IReadOnlyList<FullNetColumn> business,
        FullNetCrudScene scene = FullNetCrudScene.Single, IReadOnlyList<FullNetCrudRelationship>? relationships = null, string? resourceName = null,
        FullNetCrudOwnershipMode ownership = FullNetCrudOwnershipMode.None) =>
        FullNetCrudSchema.CreateProject("acme", "catalog", key, "acme_catalog_" + key,
            "Acme.Remediation", type, resourceName ?? key.Replace('_', '-') + "s", resourceName ?? key + "s",
            FullNetCrudDataScope.TenantRequired,
            new(FullNetCrudDeleteMode.SoftDelete, false, false, true, true, ownership),
            scene, relationships ?? [],
            new FullNetColumn[] { Uuid("Id"), Uuid("TenantId") }.Concat(ownership == FullNetCrudOwnershipMode.OrganizationUnit ? [Uuid("OrganizationUnitId")] : []).Concat(business).Concat(
            [new("Version", "Version", "version", FullNetScalarType.Int64),
             new("IsDeleted", "IsDeleted", "isDeleted", FullNetScalarType.Boolean),
             new("DeletedAtUtc", "DeletedAtUtc", "deletedAtUtc", FullNetScalarType.DateTimeUtc, IsNullable: true),
             new("DeletedById", "DeletedById", "deletedById", FullNetScalarType.Uuid, IsNullable: true)]).ToArray());

    private static FullNetColumn Uuid(string name) => new(name, name, char.ToLowerInvariant(name[0]) + name[1..], FullNetScalarType.Uuid);

    private static FullNetColumn Text(string name, bool unique = false) =>
        new(name, name, char.ToLowerInvariant(name[0]) + name[1..], FullNetScalarType.String, MaxLength: 64,
            Ui: FullNetColumnUi.DefaultFor(name, FullNetScalarType.String, false) with { Unique = unique });
}
