using System.Text;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 为 Tree 与同模块关系场景生成写入守卫，避免可执行产物缺少环检测或聚合边界。
/// </summary>
internal static class CrudSceneGuardGenerator
{
    internal static string CreateGuardCall(FullNetCrudSchema schema, string idParameter, string organizationExpression = "organizationUnitId")
    {
        var calls = new List<string>();
        var organizationArgument = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema)
            ? organizationExpression + ",\n    " : string.Empty;
        if (schema.Scene == FullNetCrudScene.Tree)
        {
            calls.Add("await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);");
            calls.Add(
                $$"""
                var treeError = await EnsureTreeParentAsync(
                    {{idParameter}},
                    request.{{schema.Columns.Single(column => column.DatabaseName == "ParentId").ClrPropertyName}},
                    {{organizationArgument}}cancellationToken)
                    .ConfigureAwait(false);
                if (treeError is not null)
                {
                    return treeError;
                }
                """);
        }

        foreach (var column in UniqueColumns(schema))
        {
            calls.Add(
                $$"""
                var {{LowerFirst(column.ClrPropertyName)}}UniqueError = await EnsureUnique{{column.ClrPropertyName}}Async(
                    {{idParameter}},
                    request.{{column.ClrPropertyName}},
                    cancellationToken)
                    .ConfigureAwait(false);
                if ({{LowerFirst(column.ClrPropertyName)}}UniqueError is not null)
                {
                    return {{LowerFirst(column.ClrPropertyName)}}UniqueError;
                }
                """);
        }

        foreach (var relationship in PrincipalChecks(schema))
        {
            calls.Add(
                $$"""
                var {{LowerFirst(relationship.DependentColumnName)}}PrincipalError = await Ensure{{relationship.DependentColumnName}}PrincipalExistsAsync(
                    request.{{schema.Columns.Single(column => column.DatabaseName == relationship.DependentColumnName).ClrPropertyName}},
                    {{organizationArgument}}cancellationToken)
                    .ConfigureAwait(false);
                if ({{LowerFirst(relationship.DependentColumnName)}}PrincipalError is not null)
                {
                    return {{LowerFirst(relationship.DependentColumnName)}}PrincipalError;
                }
                """);
        }

        return calls.Count == 0
            ? string.Empty
            : string.Join("\n\n", calls) + "\n\n";
    }

    internal static string UpdateGuardCall(FullNetCrudSchema schema, string idParameter) =>
        CreateGuardCall(schema, idParameter, "existingForAuthorization.Value!.OrganizationUnitId");

    internal static string DeleteGuardCall(FullNetCrudSchema schema, string idParameter)
    {
        if (schema.Scene == FullNetCrudScene.Tree)
            return $"await AcquireTreeWriteLockAsync(cancellationToken).ConfigureAwait(false);\nvar treeChildrenError = await EnsureTreeHasNoChildrenAsync({idParameter}, cancellationToken).ConfigureAwait(false);\nif (treeChildrenError is not null) return treeChildrenError;\n\n";
        if (!schema.Relationships.Any(relationship =>
                relationship.CascadeDelete == true
                && relationship.PrincipalEntityKey == schema.EntityKey))
        {
            return string.Empty;
        }

        return $$"""
            await CascadeDeleteDependentsAsync(
                existing.Value!,
                actorUserId,
                cancellationToken)
                .ConfigureAwait(false);

            """;
    }

    internal static string Methods(FullNetCrudSchema schema, string idParameter)
    {
        var methods = new List<string>();
        var organizationOwned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var organizationParameter = organizationOwned ? "Guid organizationUnitId,\n    " : string.Empty;
        if (schema.Scene == FullNetCrudScene.Tree)
        {
            methods.Add(CrudTreeGenerator.Methods(schema));
            var tenantParent = schema.IsTenantScoped
                ? "\n                    TenantId = currentTenant.Id!.Value,"
                : string.Empty;
            methods.Add(
                $$"""
                private async Task<Result<{{schema.ClrTypeName}}Response>?> EnsureTreeParentAsync(
                    Guid {{idParameter}},
                    Guid? parentId,
                    {{organizationParameter}}CancellationToken cancellationToken)
                {
                    if (parentId is null)
                    {
                        return null;
                    }

                    if (parentId.Value == {{idParameter}})
                    {
                        return {{schema.ClrTypeName}}FeatureErrors.InvalidParent();
                    }

                    var parent = await queryExecutor
                        .QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                            {{schema.ClrTypeName}}Sql.FindByIdStatement,
                            new
                            {
                                Id = parentId.Value,{{tenantParent}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (parent is null{{(organizationOwned ? " || parent.OrganizationUnitId != organizationUnitId" : string.Empty)}})
                    {
                        return {{schema.ClrTypeName}}FeatureErrors.InvalidParent();
                    }

                    var current = parentId.Value;
                    for (var depth = 0; depth < 32; depth++)
                    {
                        var node = await queryExecutor
                            .QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                                {{schema.ClrTypeName}}Sql.FindByIdStatement,
                                new
                                {
                                    Id = current,{{tenantParent}}
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (node is null{{(organizationOwned ? " || node.OrganizationUnitId != organizationUnitId" : string.Empty)}})
                        {
                            return {{schema.ClrTypeName}}FeatureErrors.InvalidParent();
                        }
                        if (node.{{schema.Columns.Single(column => column.DatabaseName == "ParentId").ClrPropertyName}} is null)
                        {
                            return null;
                        }

                        if (node.{{schema.Columns.Single(column => column.DatabaseName == "ParentId").ClrPropertyName}}.Value == {{idParameter}})
                        {
                            return {{schema.ClrTypeName}}FeatureErrors.ParentCycle();
                        }

                        current = node.{{schema.Columns.Single(column => column.DatabaseName == "ParentId").ClrPropertyName}}.Value;
                    }

                    return {{schema.ClrTypeName}}FeatureErrors.ParentCycle();
                }
                """);
        }

        foreach (var firstUnique in UniqueColumns(schema))
        {
            var tenantUnique = schema.IsTenantScoped
                ? " AND TenantId = @TenantId"
                : string.Empty;
            var tenantParam = schema.IsTenantScoped
                ? "\n                            TenantId = currentTenant.Id!.Value,"
                : string.Empty;
            var scope = schema.DataScope switch
            {
                FullNetCrudDataScope.HostOnly => "SqlDataScope.HostOnly",
                FullNetCrudDataScope.TenantRequired => "SqlDataScope.TenantRequired",
                _ => "SqlDataScope.Global",
            };
            var tenantBinding = schema.IsTenantScoped
                ? "SqlTenantBinding.CurrentTenantId"
                : "SqlTenantBinding.None";
            methods.Add(
                $$"""
                private async Task<Result<{{schema.ClrTypeName}}Response>?> EnsureUnique{{firstUnique.ClrPropertyName}}Async(
                    Guid {{idParameter}},
                    {{ClrType(firstUnique)}} value,
                    CancellationToken cancellationToken)
                {
                    {{(firstUnique.IsNullable ? "if (value is null) return null;" : string.Empty)}}
                    var existingId = await queryExecutor
                        .QuerySingleOrDefaultAsync<Guid?>(
                            new SqlStatement(
                                "{{schema.ModuleKey}}.find_{{schema.EntityKey}}_by_{{firstUnique.EntityKey()}}",
                                "SELECT Id FROM {{schema.DatabaseTableName}} WHERE {{firstUnique.DatabaseName}} = @Value AND Id <> @Id{{tenantUnique}}",
                                {{scope}},
                                {{tenantBinding}}),
                            new
                            {
                                Value = value,
                                Id = {{idParameter}},{{tenantParam}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return existingId is null
                        ? null
                        : {{schema.ClrTypeName}}FeatureErrors.UniqueConflict();
                }
                """);
        }

        foreach (var relationship in PrincipalChecks(schema))
        {
            var principal = FullNetCrudRelationshipResolver.Principal(schema, relationship);
            var principalTable = principal.DatabaseTableName;
            var dependentColumn = schema.Columns.Single(column => column.DatabaseName == relationship.DependentColumnName);
            var deletedFilter = principal.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete
                ? " AND IsDeleted = 0" : string.Empty;
            var tenantSql = schema.IsTenantScoped
                ? " AND TenantId = @TenantId"
                : string.Empty;
            var tenantParam = schema.IsTenantScoped
                ? "\n                            TenantId = currentTenant.Id!.Value,"
                : string.Empty;
            var scope = schema.DataScope switch
            {
                FullNetCrudDataScope.HostOnly => "SqlDataScope.HostOnly",
                FullNetCrudDataScope.TenantRequired => "SqlDataScope.TenantRequired",
                _ => "SqlDataScope.Global",
            };
            var tenantBinding = schema.IsTenantScoped
                ? "SqlTenantBinding.CurrentTenantId"
                : "SqlTenantBinding.None";
            methods.Add(
                $$"""
                private async Task<Result<{{schema.ClrTypeName}}Response>?> Ensure{{relationship.DependentColumnName}}PrincipalExistsAsync(
                    {{(dependentColumn.IsNullable ? "Guid?" : "Guid")}} principalId,
                    {{organizationParameter}}CancellationToken cancellationToken)
                {
                    {{(dependentColumn.IsNullable ? "if (principalId is null) return null;" : string.Empty)}}
                    var found = await queryExecutor
                        .QuerySingleOrDefaultAsync<Guid?>(
                            new SqlStatement(
                                "{{schema.ModuleKey}}.find_{{relationship.PrincipalEntityKey}}_for_{{schema.EntityKey}}_{{LowerFirst(relationship.DependentColumnName)}}",
                                "SELECT Id FROM {{principalTable}} WHERE {{relationship.PrincipalColumnName}} = @Id{{tenantSql}}{{deletedFilter}}{{(organizationOwned ? " AND OrganizationUnitId = @OrganizationUnitId" : string.Empty)}}",
                                {{scope}},
                                {{tenantBinding}}),
                            new
                            {
                                Id = principalId,{{tenantParam}}
                                {{(organizationOwned ? "OrganizationUnitId = organizationUnitId," : string.Empty)}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return found is null
                        ? {{schema.ClrTypeName}}FeatureErrors.PrincipalNotFound()
                        : null;
                }
                """);
        }

        var cascade = schema.Relationships
            .Where(relationship =>
                relationship.CascadeDelete == true
                && relationship.PrincipalEntityKey == schema.EntityKey)
            .ToArray();
        if (cascade.Length > 0)
        {
            var deletes = string.Join(
                "\n\n",
                cascade.Select(relationship =>
                {
                    var dependent = FullNetCrudRelationshipResolver.Dependent(schema, relationship);
                    var dependentTable = dependent.DatabaseTableName;
                    var softDelete = dependent.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete;
                    var assignments = new List<string> { "IsDeleted = 1" };
                    if (dependent.EntityCapabilities.HasDeletedAudit)
                    {
                        assignments.Add("DeletedAtUtc = @DeletedAtUtc");
                        assignments.Add("DeletedById = @DeletedById");
                    }
                    if (dependent.HasVersion) assignments.Add("Version = Version + 1");
                    var mutation = softDelete ? $"UPDATE {dependentTable} SET {string.Join(", ", assignments)}"
                        : $"DELETE FROM {dependentTable}";
                    var deletedFilter = softDelete ? " AND IsDeleted = 0" : string.Empty;
                    var tenantSql = schema.IsTenantScoped
                        ? " AND TenantId = @TenantId"
                        : string.Empty;
                    var tenantParam = schema.IsTenantScoped
                        ? "\n                            TenantId = currentTenant.Id!.Value,"
                        : string.Empty;
                    var scope = schema.DataScope switch
                    {
                        FullNetCrudDataScope.HostOnly => "SqlDataScope.HostOnly",
                        FullNetCrudDataScope.TenantRequired =>
                            "SqlDataScope.TenantRequired",
                        _ => "SqlDataScope.Global",
                    };
                    var tenantBinding = schema.IsTenantScoped
                        ? "SqlTenantBinding.CurrentTenantId"
                        : "SqlTenantBinding.None";
                    return $$"""
                    await commandExecutor.ExecuteAsync(
                            new SqlStatement(
                                "{{schema.ModuleKey}}.cascade_delete_{{relationship.DependentEntityKey}}_{{LowerFirst(relationship.DependentColumnName)}}",
                                "{{mutation}} WHERE {{relationship.DependentColumnName}} = @Id{{tenantSql}}{{deletedFilter}}{{(organizationOwned ? " AND OrganizationUnitId = @OrganizationUnitId" : string.Empty)}}",
                                {{scope}},
                                {{tenantBinding}}),
                            new
                            {
                                Id = principal.{{schema.Columns.Single(column => column.DatabaseName == relationship.PrincipalColumnName).ClrPropertyName}},{{tenantParam}}
                                {{(organizationOwned ? "OrganizationUnitId = principal.OrganizationUnitId," : string.Empty)}}
                                DeletedAtUtc = clock.UtcNow,
                                DeletedById = actorUserId,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    """;
                }));
            methods.Add(
                "                private async Task CascadeDeleteDependentsAsync(\n"
                + $"                    {schema.ClrTypeName}Response principal,\n"
                + "                    Guid actorUserId,\n"
                + "                    CancellationToken cancellationToken)\n"
                + "                {\n"
                + IndentLines(deletes, 8)
                + "\n                }");
        }

        return methods.Count == 0
            ? string.Empty
            : "\n" + string.Join("\n\n", methods);
    }

    internal static IReadOnlyList<string> ExtraErrorMembers(FullNetCrudSchema schema)
    {
        var members = new List<string>();
        if (schema.Scene == FullNetCrudScene.Tree)
        {
            members.Add(
                $$"""
                public const string InvalidParent =
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.invalid_parent";

                public const string ParentCycle =
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.parent_cycle";
                """);
        }

        if (CrudUniqueKeyGenerator.Keys(schema).Count > 0)
        {
            members.Add(
                $$"""
                public const string UniqueConflict =
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.unique_conflict";
                """);
        }

        if (PrincipalChecks(schema).Count > 0)
        {
            members.Add(
                $$"""
                public const string PrincipalNotFound =
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.principal_not_found";
                """);
        }

        return members;
    }

    internal static string ExtraErrorFactories(FullNetCrudSchema schema)
    {
        var factories = new List<string>();
        if (schema.Scene == FullNetCrudScene.Tree)
        {
            factories.Add(
                $$"""
                internal static Result<{{schema.ClrTypeName}}Response> InvalidParent() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.InvalidParent,
                        "The parent node is missing or outside the current tenant.",
                        ErrorType.Validation));

                internal static Result<{{schema.ClrTypeName}}Response> ParentCycle() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.ParentCycle,
                        "The parent assignment would create a cycle.",
                        ErrorType.Validation));
                """);
        }

        if (CrudUniqueKeyGenerator.Keys(schema).Count > 0)
        {
            factories.Add(
                $$"""
                internal static Result<{{schema.ClrTypeName}}Response> UniqueConflict() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.UniqueConflict,
                        "The unique field value already exists.",
                        ErrorType.Conflict));
                """);
        }

        if (PrincipalChecks(schema).Count > 0)
        {
            factories.Add(
                $$"""
                internal static Result<{{schema.ClrTypeName}}Response> PrincipalNotFound() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.PrincipalNotFound,
                        "The related principal was not found in the current module.",
                        ErrorType.Validation));
                """);
        }

        return factories.Count == 0
            ? string.Empty
            : "\n\n" + string.Join("\n\n", factories);
    }

    private static IReadOnlyList<FullNetColumn> UniqueColumns(FullNetCrudSchema schema) =>
        schema.Columns.Where(column => column.Ui?.Unique == true).ToArray();

    private static IReadOnlyList<FullNetCrudRelationship> PrincipalChecks(
        FullNetCrudSchema schema) =>
        schema.Relationships
            .Where(relationship =>
                relationship.DependentEntityKey == schema.EntityKey)
            .ToArray();

    private static string ClrType(FullNetColumn column) =>
        column.ScalarType switch
        {
            FullNetScalarType.Uuid => column.IsNullable ? "Guid?" : "Guid",
            FullNetScalarType.String => column.IsNullable ? "string?" : "string",
            FullNetScalarType.Int32 => column.IsNullable ? "int?" : "int",
            FullNetScalarType.Int64 => column.IsNullable ? "long?" : "long",
            FullNetScalarType.Boolean => column.IsNullable ? "bool?" : "bool",
            FullNetScalarType.DateTimeUtc => column.IsNullable
                ? "DateTimeOffset?"
                : "DateTimeOffset",
            FullNetScalarType.Decimal => column.IsNullable ? "decimal?" : "decimal",
            _ => "object",
        };

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static string IndentLines(string content, int spaces)
    {
        var indentation = new string(' ', spaces);
        return indentation + content.Replace(
            "\n",
            $"\n{indentation}",
            StringComparison.Ordinal);
    }
}

file static class CrudSceneGuardColumnExtensions
{
    public static string EntityKey(this FullNetColumn column) =>
        char.ToLowerInvariant(column.DatabaseName[0]) + column.DatabaseName[1..];
}
