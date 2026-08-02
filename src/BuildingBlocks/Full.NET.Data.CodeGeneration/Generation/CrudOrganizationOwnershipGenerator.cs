using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>为 OrganizationUnit 归属实体生成 Feature/Endpoint 片段。</summary>
internal static class CrudOrganizationOwnershipGenerator
{
    internal static bool IsOrganizationUnitOwned(FullNetCrudSchema schema) =>
        !schema.UsesLegacyEntityCapabilities
        && schema.EntityCapabilities.OwnershipMode
            == FullNetCrudOwnershipMode.OrganizationUnit;

    internal static string FeatureUsings() =>
        """
        using Full.NET.Modules.Organization.Contracts;
        """;

    internal static string EndpointUsings() =>
        """
        using Full.NET.Modules.Organization.Contracts;
        """;

    internal static string QueryServiceConstructorParameters() =>
        """
        IUserDataScopeResolver dataScopeResolver,
        IDataScopeSqlFilterBuilder dataScopeFilterBuilder
        """;

    internal static string ManagementConstructorParameters() =>
        "IOrganizationOwnedEntityWriteAuthorizer writeAuthorizer";

    internal static string DataScopeComposerClass() =>
        """

            private static class GeneratedTenantDataScopeComposer
            {
                private const string CountTenantWhereAnchor =
                    "WHERE TenantId = @TenantId";

                private const string ListTenantWhereAnchor =
                    "WHERE 1 = 1\n            AND TenantId = @TenantId";

                internal static SqlStatement ApplyDataScopeFilter(
                    SqlStatement statement,
                    DataScopeSqlFilter? filter,
                    string tenantWhereAnchor)
                {
                    if (filter is null)
                    {
                        return statement;
                    }

                    var text = InjectFilter(statement.Text, filter.Sql, tenantWhereAnchor);
                    return statement with
                    {
                        Name = statement.Name + ".data_scope",
                        Text = text,
                    };
                }

                internal static object? MergeParameters(
                    object? queryParameters,
                    DataScopeSqlFilter? filter)
                {
                    if (filter?.Parameters is null)
                    {
                        return queryParameters;
                    }

                    if (queryParameters is null)
                    {
                        return filter.Parameters;
                    }

                    var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
                    CopyProperties(queryParameters, merged);
                    CopyProperties(filter.Parameters, merged);
                    return merged;
                }

                private static string InjectFilter(
                    string sql,
                    string condition,
                    string tenantWhereAnchor)
                {
                    var index = sql.IndexOf(tenantWhereAnchor, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        throw new InvalidOperationException(
                            "Tenant-scoped SQL must contain the tenant boundary anchor.");
                    }

                    var insertAt = index + tenantWhereAnchor.Length;
                    return sql.Insert(insertAt, $" AND ({condition})");
                }

                private static void CopyProperties(
                    object source,
                    IDictionary<string, object?> target)
                {
                    if (source is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            target[pair.Key] = pair.Value;
                        }

                        return;
                    }

                    foreach (var property in source.GetType().GetProperties())
                    {
                        if (!property.CanRead || property.GetIndexParameters().Length != 0)
                        {
                            continue;
                        }

                        target[property.Name] = property.GetValue(source);
                    }
                }
            }
        """;

    internal static string QueryListMethod(FullNetCrudSchema schema) =>
        $$"""

                public async Task<Result<PagedResult<{{schema.ClrTypeName}}Response>>> ListAsync(
                    Guid currentUserId,
                    bool isSuperAdministrator,
                    int page,
                    int pageSize,
                    CancellationToken cancellationToken = default)
                {
                    page = Math.Max(page, 1);
                    pageSize = Math.Clamp(pageSize, 1, 100);
                    var offset = (long)(page - 1) * pageSize;
                    var scope = await dataScopeResolver.ResolveAsync(
                            currentUserId,
                            isSuperAdministrator,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var filter = dataScopeFilterBuilder.BuildOrganizationUnitFilter(
                        scope,
                        "OrganizationUnitId",
                        currentUserId);
                    var countStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                        {{schema.ClrTypeName}}Sql.CountStatement,
                        filter,
                        "WHERE TenantId = @TenantId");
                    var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                            countStatement,
                            GeneratedTenantDataScopeComposer.MergeParameters(
                                null,
                                filter),
                            cancellationToken)
                        .ConfigureAwait(false);
                    var baseStatement = databaseOptions.Value.Provider switch
                    {
                        DatabaseProvider.SqlServer =>
                            {{schema.ClrTypeName}}Sql.ListSqlServerStatement,
                        DatabaseProvider.MySql =>
                            {{schema.ClrTypeName}}Sql.ListMySqlStatement,
                        _ => throw new InvalidOperationException(
                            "The configured database provider is not supported."),
                    };
                    var listStatement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                        baseStatement,
                        filter,
                        "WHERE 1 = 1\n            AND TenantId = @TenantId");
                    var rows = await queryExecutor
                        .QueryAsync<{{schema.ClrTypeName}}Record>(
                            listStatement,
                            GeneratedTenantDataScopeComposer.MergeParameters(
                                new { Offset = offset, PageSize = pageSize },
                                filter),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Result<PagedResult<{{schema.ClrTypeName}}Response>>.Success(
                        new PagedResult<{{schema.ClrTypeName}}Response>(
                            rows.Select(Map).ToArray(),
                            page,
                            pageSize,
                            total));
                }
        """;

    internal static string QueryGetByIdMethod(FullNetCrudSchema schema, string idParameter) =>
        $$"""

                public async Task<Result<{{schema.ClrTypeName}}Response>> GetByIdAsync(
                    Guid {{idParameter}},
                    Guid currentUserId,
                    bool isSuperAdministrator,
                    CancellationToken cancellationToken = default)
                {
                    var scope = await dataScopeResolver.ResolveAsync(
                            currentUserId,
                            isSuperAdministrator,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var filter = dataScopeFilterBuilder.BuildOrganizationUnitFilter(
                        scope,
                        "OrganizationUnitId",
                        currentUserId);
                    var statement = GeneratedTenantDataScopeComposer.ApplyDataScopeFilter(
                        {{schema.ClrTypeName}}Sql.FindByIdStatement,
                        filter,
                        "AND TenantId = @TenantId");
                    var record = await queryExecutor
                        .QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                            statement,
                            GeneratedTenantDataScopeComposer.MergeParameters(
                                new { Id = {{idParameter}} },
                                filter),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return record is null
                        ? NotFound()
                        : Result<{{schema.ClrTypeName}}Response>.Success(Map(record));
                }
        """;

    internal static string CreateAuthorizationBlock(FullNetCrudSchema schema) =>
        $$"""

                    var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                            currentTenant.Id!.Value,
                            organizationUnitId,
                            actorUserId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!authorization.IsSuccess)
                    {
                        return Result<{{schema.ClrTypeName}}Response>.Failure(authorization.Error!);
                    }
        """;

    internal static string UpdateAuthorizationBlock(
        FullNetCrudSchema schema,
        string idParameter) =>
        $$"""

                    var existingForAuthorization = await queries.FindByIdAsync(
                            {{idParameter}},
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!existingForAuthorization.IsSuccess)
                    {
                        return existingForAuthorization;
                    }

                    var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                            currentTenant.Id!.Value,
                            existingForAuthorization.Value!.OrganizationUnitId,
                            actorUserId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!authorization.IsSuccess)
                    {
                        return Result<{{schema.ClrTypeName}}Response>.Failure(authorization.Error!);
                    }
        """;

    internal static string DeleteAuthorizationBlock(FullNetCrudSchema schema) =>
        $$"""

                    var authorization = await writeAuthorizer.EnsureCanWriteAsync(
                            currentTenant.Id!.Value,
                            existing.Value!.OrganizationUnitId,
                            actorUserId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!authorization.IsSuccess)
                    {
                        return Result<{{schema.ClrTypeName}}Response>.Failure(authorization.Error!);
                    }
        """;

    internal static string InternalFindByIdMethod(FullNetCrudSchema schema, string idParameter) =>
        $$"""

                internal async Task<Result<{{schema.ClrTypeName}}Response>> FindByIdAsync(
                    Guid {{idParameter}},
                    CancellationToken cancellationToken = default)
                {
                    var record = await queryExecutor
                        .QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                            {{schema.ClrTypeName}}Sql.FindByIdStatement,
                            new { Id = {{idParameter}} },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return record is null
                        ? NotFound()
                        : Result<{{schema.ClrTypeName}}Response>.Success(Map(record));
                }
        """;

    internal static string EndpointActorResolverMethods() =>
        """

                private static bool TryResolveActor(
                    ClaimsPrincipal principal,
                    out Guid actorUserId,
                    out bool isSuperAdministrator)
                {
                    actorUserId = default;
                    isSuperAdministrator = bool.TryParse(
                        principal.FindFirstValue(
                            FullNetIdentityClaimTypes.SuperAdministrator),
                        out var enabled)
                        && enabled;
                    return Guid.TryParse(
                        principal.FindFirstValue(
                            FullNetIdentityClaimTypes.Subject),
                        out actorUserId);
                }

                private static bool TryResolveOrganizationUnitId(
                    HttpContext httpContext,
                    out Guid organizationUnitId)
                {
                    organizationUnitId = default;
                    return Guid.TryParse(
                        httpContext.Request.Headers[
                            OrganizationRequestHeaders.OrganizationUnitId],
                        out organizationUnitId);
                }
        """;
}