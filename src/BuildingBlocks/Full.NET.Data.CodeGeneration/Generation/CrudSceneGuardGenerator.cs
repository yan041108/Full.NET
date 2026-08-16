using System.Text;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 为 Tree 与同模块关系场景生成写入守卫，避免可执行产物缺少环检测或聚合边界。
/// </summary>
internal static class CrudSceneGuardGenerator
{
    internal static string CreateGuardCall(FullNetCrudSchema schema, string idParameter)
    {
        var calls = new List<string>();
        if (schema.Scene == FullNetCrudScene.Tree)
        {
            calls.Add(
                $$"""
                var treeError = await EnsureTreeParentAsync(
                    {{idParameter}},
                    request.ParentId,
                    cancellationToken)
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
                var {{LowerFirst(column.ClrPropertyName)}}UniqueError = await EnsureUniqueAsync(
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
                var {{LowerFirst(relationship.DependentColumnName)}}PrincipalError = await EnsurePrincipalExistsAsync(
                    request.{{relationship.DependentColumnName}},
                    cancellationToken)
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
        CreateGuardCall(schema, idParameter);

    internal static string DeleteGuardCall(FullNetCrudSchema schema, string idParameter)
    {
        if (!schema.Relationships.Any(relationship =>
                relationship.CascadeDelete == true
                && relationship.PrincipalEntityKey == schema.EntityKey))
        {
            return string.Empty;
        }

        return $$"""
            await CascadeDeleteDependentsAsync(
                {{idParameter}},
                cancellationToken)
                .ConfigureAwait(false);

            """;
    }

    internal static string Methods(FullNetCrudSchema schema, string idParameter)
    {
        var methods = new List<string>();
        if (schema.Scene == FullNetCrudScene.Tree)
        {
            var tenantParent = schema.IsTenantScoped
                ? "\n                    TenantId = currentTenant.Id!.Value,"
                : string.Empty;
            methods.Add(
                $$"""
                private async Task<Result<{{schema.ClrTypeName}}Response>?> EnsureTreeParentAsync(
                    Guid {{idParameter}},
                    Guid? parentId,
                    CancellationToken cancellationToken)
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
                    if (parent is null)
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
                        if (node is null || node.ParentId is null)
                        {
                            return null;
                        }

                        if (node.ParentId.Value == {{idParameter}})
                        {
                            return {{schema.ClrTypeName}}FeatureErrors.ParentCycle();
                        }

                        current = node.ParentId.Value;
                    }

                    return {{schema.ClrTypeName}}FeatureErrors.ParentCycle();
                }
                """);
        }

        if (UniqueColumns(schema).Count > 0)
        {
            var firstUnique = UniqueColumns(schema)[0];
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
                _ => "SqlDataScope.Unspecified",
            };
            var tenantBinding = schema.IsTenantScoped
                ? "SqlTenantBinding.Required"
                : "SqlTenantBinding.None";
            methods.Add(
                $$"""
                private async Task<Result<{{schema.ClrTypeName}}Response>?> EnsureUniqueAsync(
                    Guid {{idParameter}},
                    {{ClrType(firstUnique)}} value,
                    CancellationToken cancellationToken)
                {
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
            var principalTable =
                $"{schema.OwnerKey}_{schema.ModuleKey}_{relationship.PrincipalEntityKey}";
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
                _ => "SqlDataScope.Unspecified",
            };
            var tenantBinding = schema.IsTenantScoped
                ? "SqlTenantBinding.Required"
                : "SqlTenantBinding.None";
            methods.Add(
                $$"""
                private async Task<Result<{{schema.ClrTypeName}}Response>?> EnsurePrincipalExistsAsync(
                    Guid principalId,
                    CancellationToken cancellationToken)
                {
                    var found = await queryExecutor
                        .QuerySingleOrDefaultAsync<Guid?>(
                            new SqlStatement(
                                "{{schema.ModuleKey}}.find_{{relationship.PrincipalEntityKey}}_for_{{schema.EntityKey}}",
                                "SELECT Id FROM {{principalTable}} WHERE Id = @Id{{tenantSql}}",
                                {{scope}},
                                {{tenantBinding}}),
                            new
                            {
                                Id = principalId,{{tenantParam}}
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
                    var dependentTable =
                        $"{schema.OwnerKey}_{schema.ModuleKey}_{relationship.DependentEntityKey}";
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
                        _ => "SqlDataScope.Unspecified",
                    };
                    var tenantBinding = schema.IsTenantScoped
                        ? "SqlTenantBinding.Required"
                        : "SqlTenantBinding.None";
                    return $$"""
                    await commandExecutor.ExecuteAsync(
                            new SqlStatement(
                                "{{schema.ModuleKey}}.cascade_delete_{{relationship.DependentEntityKey}}",
                                "DELETE FROM {{dependentTable}} WHERE {{relationship.DependentColumnName}} = @Id{{tenantSql}}",
                                {{scope}},
                                {{tenantBinding}}),
                            new
                            {
                                Id = {{idParameter}},{{tenantParam}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    """;
                }));
            methods.Add(
                "                private async Task CascadeDeleteDependentsAsync(\n"
                + $"                    Guid {idParameter},\n"
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

        if (UniqueColumns(schema).Count > 0)
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

        if (UniqueColumns(schema).Count > 0)
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
