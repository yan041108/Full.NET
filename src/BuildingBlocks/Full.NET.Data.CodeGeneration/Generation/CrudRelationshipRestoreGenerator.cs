using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>关联重新添加时恢复原软删记录，唯一键继续覆盖历史记录，避免删除后永久无法重建关联。</summary>
internal static class CrudRelationshipRestoreGenerator
{
    private static bool Enabled(FullNetCrudSchema schema) => schema.Scene == FullNetCrudScene.ManyToMany
        && schema.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete;

    internal static string Read(FullNetCrudSchema schema, string id)
    {
        if (!Enabled(schema)) return string.Empty;
        var projection = string.Join(", ", schema.Columns.Select(column => column.DatabaseName == column.ClrPropertyName
            ? column.DatabaseName : $"{column.DatabaseName} AS {column.ClrPropertyName}"));
        var conditions = schema.Relationships.Select(relation => $"{relation.DependentColumnName} = @{relation.DependentColumnName}").ToList();
        var parameters = schema.Relationships.Select(relation => $"[\"{relation.DependentColumnName}\"] = request.{schema.Columns.Single(column => column.DatabaseName == relation.DependentColumnName).ClrPropertyName}").ToList();
        if (schema.IsTenantScoped) { conditions.Add("TenantId = @TenantId"); parameters.Add("[\"TenantId\"] = currentTenant.Id!.Value"); }
        if (CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema)) { conditions.Add("OrganizationUnitId = @OrganizationUnitId"); parameters.Add("[\"OrganizationUnitId\"] = organizationUnitId"); }
        conditions.Add("IsDeleted = 1");
        return $$"""
            var removedPair = await queryExecutor.QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                new SqlStatement("{{schema.ModuleKey}}.{{schema.EntityKey}}.removed_pair", "SELECT {{projection}} FROM {{schema.DatabaseTableName}} WHERE {{string.Join(" AND ", conditions)}}", {{Scope(schema)}}, {{Binding(schema)}}),
                new Dictionary<string, object?> { {{string.Join(", ", parameters)}} }, cancellationToken).ConfigureAwait(false);
            if (removedPair is not null) {{id}} = removedPair.Id;

            """;
    }

    internal static string Restore(FullNetCrudSchema schema, string id)
    {
        if (!Enabled(schema)) return string.Empty;
        var conditions = new List<string> { "Id = @Id", "IsDeleted = 1" };
        var parameters = new List<string> { $"[\"Id\"] = {id}" };
        if (schema.IsTenantScoped) { conditions.Add("TenantId = @TenantId"); parameters.Add("[\"TenantId\"] = currentTenant.Id!.Value"); }
        if (CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema)) { conditions.Add("OrganizationUnitId = @OrganizationUnitId"); parameters.Add("[\"OrganizationUnitId\"] = organizationUnitId"); }
        var assignments = new List<string> { "IsDeleted = 0" };
        if (schema.HasVersion) { conditions.Add("Version = @Version"); parameters.Add("[\"Version\"] = removedPair.Version"); assignments.Add("Version = Version + 1"); }
        if (schema.EntityCapabilities.HasDeletedAudit) assignments.AddRange(["DeletedAtUtc = NULL", "DeletedById = NULL"]);
        if (schema.EntityCapabilities.HasUpdatedAudit) { assignments.AddRange(["UpdatedAtUtc = @UpdatedAtUtc", "UpdatedById = @UpdatedById"]); parameters.AddRange(["[\"UpdatedAtUtc\"] = clock.UtcNow", "[\"UpdatedById\"] = actorUserId"]); }
        // 普通业务列随本次创建更新，系统列保持服务端所有权；关联键已由前置查询精确匹配。
        foreach (var column in schema.Columns.Where(column => !new[] { "Id", "TenantId", "OrganizationUnitId", "Version", "IsDeleted", "CreatedAtUtc", "CreatedById", "UpdatedAtUtc", "UpdatedById", "DeletedAtUtc", "DeletedById" }.Contains(column.DatabaseName)))
        {
            assignments.Add($"{column.DatabaseName} = @{column.DatabaseName}");
            parameters.Add($"[\"{column.DatabaseName}\"] = request.{column.ClrPropertyName}" + (column.ScalarType == FullNetScalarType.DateTimeUtc ? (column.IsNullable ? "?.ToUniversalTime()" : ".ToUniversalTime()") : string.Empty));
        }
        return $$"""
            if (removedPair is not null)
            {
                var restored = await commandExecutor.ExecuteAsync(new SqlStatement("{{schema.ModuleKey}}.{{schema.EntityKey}}.restore_pair",
                    "UPDATE {{schema.DatabaseTableName}} SET {{string.Join(", ", assignments)}} WHERE {{string.Join(" AND ", conditions)}}", {{Scope(schema)}}, {{Binding(schema)}}),
                    new Dictionary<string, object?> { {{string.Join(", ", parameters)}} }, cancellationToken).ConfigureAwait(false);
                if (restored != 1) return {{schema.ClrTypeName}}FeatureErrors.UniqueConflict();
                return await queries.FindByIdAsync({{id}}, cancellationToken).ConfigureAwait(false);
            }

            """;
    }

    private static string Scope(FullNetCrudSchema schema) => "SqlDataScope." + schema.DataScope;
    private static string Binding(FullNetCrudSchema schema) => schema.IsTenantScoped ? "SqlTenantBinding.CurrentTenantId" : "SqlTenantBinding.None";
}
