using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>主从写入通过同一模块的 Result 事务和静态从端服务执行，禁止把多次 HTTP CRUD 当作聚合事务。</summary>
internal static class CrudAggregateGenerator
{
    internal static bool Enabled(FullNetCrudSchema schema) => schema.Scene == FullNetCrudScene.MasterDetail;
    internal static FullNetCrudRelationship Relationship(FullNetCrudSchema schema) => schema.Relationships.Single();
    internal static FullNetCrudSchema Detail(FullNetCrudSchema schema)
    {
        var relation = Relationship(schema);
        if (relation.PrincipalEntityKey != schema.EntityKey || relation.PrincipalColumnName != "Id")
            throw new NotSupportedException("主从组合操作必须从当前主实体的 Id 关联从端。");
        var child = FullNetCrudRelationshipResolver.Dependent(schema, relation);
        if (!schema.HasVersion || !child.HasVersion || !schema.EntityCapabilities.CanUpdate || !child.EntityCapabilities.CanUpdate || !child.EntityCapabilities.CanDelete)
            throw new NotSupportedException("可编辑主从聚合要求主从端均有 Version，从端支持更新和删除。");
        if (CrudArtifactGenerator.FormRequestColumns(child).All(column => column.DatabaseName != relation.DependentColumnName))
            throw new NotSupportedException("主从外键必须是从端的业务 UUID 列，不能占用主键或系统归属列。");
        // 聚合专用类型避免与独立生成的从端 CRUD 类型冲突；物理表、列与生命周期仍来自冻结快照。
        return FullNetCrudSchema.CreateProject(child.OwnerKey, child.ModuleKey, child.EntityKey, child.DatabaseTableName,
            schema.RootNamespace, schema.ClrTypeName + "Detail", child.ApiResourceName, child.PermissionResourceName,
            child.DataScope, child.EntityCapabilities, child.Columns);
    }

    internal static IReadOnlyList<FullNetColumn> InputColumns(FullNetCrudSchema schema) => CrudArtifactGenerator.FormRequestColumns(Detail(schema))
        .Where(column => column.DatabaseName != Relationship(schema).DependentColumnName).ToArray();

    internal static IEnumerable<GeneratedArtifact> Artifacts(FullNetCrudSchema schema)
    {
        if (!Enabled(schema)) return [];
        var child = Detail(schema);
        return CrudArtifactGenerator.Generate(child).Where(artifact => artifact.Kind == GeneratedArtifactKind.Backend)
            .Append(new($"backend/{schema.ClrTypeName}Aggregate.g.cs", GeneratedArtifactKind.Backend, Service(schema)));
    }

    internal static string Contracts(FullNetCrudSchema schema)
    {
        if (!Enabled(schema)) return string.Empty;
        var root = schema.ClrTypeName;
        var fields = string.Join(",\n", InputColumns(schema).Select(column => $"    {CrudArtifactGenerator.CSharpSerializationAttributes(column)} {ClrType(column)} {column.ClrPropertyName}"));
        return $$"""

            /// <summary>外键由已授权主端赋值，不接受调用者提供其他主端标识。</summary>
            public sealed record {{root}}DetailInput(
            {{fields}});
            public sealed record {{root}}DetailEdit(
                [property: JsonRequired] Guid Id,
                [property: JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
                [property: JsonRequired] {{root}}DetailInput Input);
            public sealed record {{root}}DetailRemoval(
                [property: JsonRequired] Guid Id,
                [property: JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);
            public sealed record Create{{root}}AggregateRequest(
                [property: JsonRequired] Create{{root}}Request Master,
                [property: JsonRequired] System.Collections.Generic.IReadOnlyList<{{root}}DetailInput> Details);
            public sealed record Update{{root}}AggregateRequest(
                [property: JsonRequired] Update{{root}}Request Master,
                [property: JsonRequired] System.Collections.Generic.IReadOnlyList<{{root}}DetailInput> Added,
                [property: JsonRequired] System.Collections.Generic.IReadOnlyList<{{root}}DetailEdit> Updated,
                [property: JsonRequired] System.Collections.Generic.IReadOnlyList<{{root}}DetailRemoval> Removed);
            public sealed record {{root}}AggregateResponse(
                [property: JsonRequired] {{root}}Response Master,
                [property: JsonRequired] System.Collections.Generic.IReadOnlyList<{{root}}DetailResponse> Details);
            """;
    }

    internal static string Registration(FullNetCrudSchema schema) => !Enabled(schema) ? string.Empty
        : $"services.AddGenerated{schema.ClrTypeName}DetailFeature();\n        services.TryAddScoped<{schema.ClrTypeName}AggregateService>();";

    internal static string JsonTypes(FullNetCrudSchema schema) => !Enabled(schema) ? string.Empty
        : $"[JsonSerializable(typeof(Create{schema.ClrTypeName}AggregateRequest))]\n[JsonSerializable(typeof(Update{schema.ClrTypeName}AggregateRequest))]\n[JsonSerializable(typeof({schema.ClrTypeName}AggregateResponse))]\n";

    internal static string Endpoint(FullNetCrudSchema schema, string idParameter)
    {
        if (!Enabled(schema)) return string.Empty;
        var root = schema.ClrTypeName;
        var owned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var prefix = schema.ModuleKey.Split('_').First() + string.Concat(schema.ModuleKey.Split('_').Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        var readActor = owned ? "if (!TryResolveActor(principal, out var actorUserId, out var isSuperAdministrator)) return Results.Unauthorized();" : string.Empty;
        var organization = owned ? "if (!TryResolveOrganizationUnitId(httpContext, out var organizationUnitId)) return mapper.Map(Result<" + root + "AggregateResponse>.Failure(" + root + "AggregateService.Invalid()), httpContext);" : string.Empty;
        var itemRoute = "/{" + idParameter + ":guid}/aggregate";
        return $$"""
                    group.MapGet("{{itemRoute}}", async (Guid {{idParameter}}, ClaimsPrincipal principal, {{root}}AggregateService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
                    {
                        {{readActor}}
                        return mapper.Map(await service.GetAsync({{idParameter}}{{(owned ? ", actorUserId, isSuperAdministrator" : string.Empty)}}, token), httpContext);
                    }).WithName("{{prefix}}Get{{root}}Aggregate").Produces<{{root}}AggregateResponse>(StatusCodes.Status200OK)
                      .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(
                        FullNetPermissionPolicies.For({{root}}Permissions.Read), FullNetPermissionPolicies.For({{root}}DetailPermissions.Read));
                    group.MapPost("/aggregate", async (Create{{root}}AggregateRequest request, ClaimsPrincipal principal, {{root}}AggregateService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
                    {
                        if (!TryResolveActorUserId(principal, out var actorUserId)) return Results.Unauthorized();
                        {{organization}}
                        return mapper.Map(await service.CreateAsync(request, actorUserId{{(owned ? ", organizationUnitId" : string.Empty)}}, token), httpContext);
                    }).WithName("{{prefix}}Create{{root}}Aggregate").Produces<{{root}}AggregateResponse>(StatusCodes.Status200OK)
                      .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(FullNetPermissionPolicies.For({{root}}Permissions.Create));
                    group.MapPut("{{itemRoute}}", async (Guid {{idParameter}}, Update{{root}}AggregateRequest request, ClaimsPrincipal principal, {{root}}AggregateService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
                    {
                        if (!TryResolveActorUserId(principal, out var actorUserId)) return Results.Unauthorized();
                        return mapper.Map(await service.UpdateAsync({{idParameter}}, request, actorUserId, token), httpContext);
                    }).WithName("{{prefix}}Update{{root}}Aggregate").Produces<{{root}}AggregateResponse>(StatusCodes.Status200OK)
                      .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(
                        FullNetPermissionPolicies.For({{root}}Permissions.Update), FullNetPermissionPolicies.For({{root}}DetailPermissions.Read));
            """;
    }

    private static string Service(FullNetCrudSchema schema)
    {
        var child = Detail(schema);
        var root = schema.ClrTypeName;
        var fk = child.Columns.Single(column => column.DatabaseName == Relationship(schema).DependentColumnName);
        var id = schema.Columns.Single(column => column.DatabaseName == "Id").ClrPropertyName;
        var childId = child.Columns.Single(column => column.DatabaseName == "Id").ClrPropertyName;
        var owned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var scoped = schema.IsTenantScoped;
        var scope = schema.DataScope switch { FullNetCrudDataScope.TenantRequired => "SqlDataScope.TenantRequired", FullNetCrudDataScope.HostOnly => "SqlDataScope.HostOnly", _ => "SqlDataScope.Global" };
        var binding = scoped ? "SqlTenantBinding.CurrentTenantId" : "SqlTenantBinding.None";
        var projection = string.Join(", ", child.Columns.Select(column => column.DatabaseName == column.ClrPropertyName ? column.DatabaseName : column.DatabaseName + " AS " + column.ClrPropertyName));
        var condition = fk.DatabaseName + " = @ParentId" + (scoped ? " AND TenantId = @TenantId" : string.Empty)
            + (owned ? " AND OrganizationUnitId = @OrganizationUnitId" : string.Empty)
            + (child.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete ? " AND IsDeleted = 0" : string.Empty);
        var values = string.Join(", ", CrudArtifactGenerator.FormRequestColumns(child).Select(column => column.DatabaseName == fk.DatabaseName ? "master." + id : "input." + column.ClrPropertyName));
        return $$"""
            #nullable enable
            using Full.NET.Abstractions.Results;
            using Full.NET.Abstractions.Tenancy;
            using Full.NET.Abstractions.Messaging;
            using Full.NET.Data.Abstractions;
            using Microsoft.Extensions.Options;
            namespace {{schema.RootNamespace}}.Generated;

            internal sealed class {{root}}AggregateService(
                {{root}}ManagementService management, {{root}}QueryService queries,
                {{root}}DetailManagementService details,
                IQueryExecutor queryExecutor, ICommandTransaction transaction, IOptions<DatabaseOptions> databaseOptions{{(scoped ? ", ICurrentTenant currentTenant" : string.Empty)}})
            {
                internal static Error Invalid() => new("{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.invalid_aggregate", "The aggregate request is invalid or exceeds 128 detail rows.", ErrorType.Validation);
                private static Result<{{root}}AggregateResponse> Failure(Error error) => Result<{{root}}AggregateResponse>.Failure(error);
                private static {{root}}DetailResponse Map({{root}}DetailRecord record) => new({{string.Join(", ", child.Columns.Select(column => "record." + column.ClrPropertyName))}});
                private static Create{{root}}DetailRequest CreateDetail({{root}}DetailInput input, {{root}}Response master) => new({{values}});
                private static Update{{root}}DetailRequest UpdateDetail({{root}}DetailInput input, {{root}}Response master, long version) => new({{values}}, version);

                public async Task<Result<{{root}}AggregateResponse>> GetAsync(Guid id{{(owned ? ", Guid actorUserId, bool isSuperAdministrator" : string.Empty)}}, CancellationToken token = default)
                {
                    var master = await queries.GetByIdAsync(id{{(owned ? ", actorUserId, isSuperAdministrator" : string.Empty)}}, token).ConfigureAwait(false);
                    if (!master.IsSuccess) return Failure(master.Error!);
                    var rows = await ReadDetailsAsync(master.Value!, token).ConfigureAwait(false);
                    return rows.Count > 128 ? Failure(Invalid()) : Result<{{root}}AggregateResponse>.Success(new(master.Value!, rows));
                }

                public Task<Result<{{root}}AggregateResponse>> CreateAsync(Create{{root}}AggregateRequest request, Guid actorUserId{{(owned ? ", Guid organizationUnitId" : string.Empty)}}, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
                {
                    if (request is null || request.Master is null || request.Details is null || request.Details.Count > 128 || request.Details.Any(item => item is null)) return Failure(Invalid());
                    var master = await management.CreateAsync(request.Master, actorUserId{{(owned ? ", organizationUnitId" : string.Empty)}}, inner).ConfigureAwait(false);
                    if (!master.IsSuccess) return Failure(master.Error!);
                    var saved = new List<{{root}}DetailResponse>();
                    foreach (var input in request.Details)
                    {
                        var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId{{(owned ? ", master.Value!.OrganizationUnitId" : string.Empty)}}, inner).ConfigureAwait(false);
                        if (!result.IsSuccess) return Failure(result.Error!);
                        saved.Add(result.Value!);
                    }
                    return Result<{{root}}AggregateResponse>.Success(new(master.Value!, saved));
                }, token);

                public Task<Result<{{root}}AggregateResponse>> UpdateAsync(Guid id, Update{{root}}AggregateRequest request, Guid actorUserId, CancellationToken token = default) => transaction.ExecuteResultAsync(async inner =>
                {
                    if (request is null || request.Master is null || request.Added is null || request.Updated is null || request.Removed is null
                        || request.Added.Count + request.Updated.Count + request.Removed.Count > 256
                        || request.Added.Any(item => item is null) || request.Updated.Any(item => item is null || item.Input is null) || request.Removed.Any(item => item is null)) return Failure(Invalid());
                    // 主端 Version 更新先取得行锁；从端任何失败会把整个 Result 事务回滚。
                    var master = await management.UpdateAsync(id, request.Master, actorUserId, inner).ConfigureAwait(false);
                    if (!master.IsSuccess) return Failure(master.Error!);
                    var existing = await ReadDetailsAsync(master.Value!, inner).ConfigureAwait(false);
                    var touched = request.Updated.Select(item => item.Id).Concat(request.Removed.Select(item => item.Id)).ToArray();
                    if (existing.Count > 128 || existing.Count + request.Added.Count - request.Removed.Count > 128
                        || touched.Distinct().Count() != touched.Length || touched.Any(key => existing.All(item => item.{{childId}} != key))) return Failure(Invalid());
                    foreach (var edit in request.Updated)
                    {
                        var result = await details.UpdateAsync(edit.Id, UpdateDetail(edit.Input, master.Value!, edit.Version), actorUserId, inner).ConfigureAwait(false);
                        if (!result.IsSuccess) return Failure(result.Error!);
                    }
                    foreach (var removal in request.Removed)
                    {
                        var result = await details.DeleteAsync(removal.Id, new Delete{{root}}DetailRequest(removal.Version), actorUserId, inner).ConfigureAwait(false);
                        if (!result.IsSuccess) return Failure(result.Error!);
                    }
                    foreach (var input in request.Added)
                    {
                        var result = await details.CreateAsync(CreateDetail(input, master.Value!), actorUserId{{(owned ? ", master.Value!.OrganizationUnitId" : string.Empty)}}, inner).ConfigureAwait(false);
                        if (!result.IsSuccess) return Failure(result.Error!);
                    }
                    return Result<{{root}}AggregateResponse>.Success(new(master.Value!, await ReadDetailsAsync(master.Value!, inner).ConfigureAwait(false)));
                }, token);

                private async Task<IReadOnlyList<{{root}}DetailResponse>> ReadDetailsAsync({{root}}Response master, CancellationToken token)
                {
                    var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
                        ? "SELECT {{projection}} FROM {{child.DatabaseTableName}} WHERE {{condition}} ORDER BY Id LIMIT 129"
                        : "SELECT TOP (129) {{projection}} FROM {{child.DatabaseTableName}} WHERE {{condition}} ORDER BY Id";
                    var rows = await queryExecutor.QueryAsync<{{root}}DetailRecord>(new SqlStatement("{{schema.ModuleKey}}.{{schema.EntityKey}}.aggregate_details", sql, {{scope}}, {{binding}}),
                        new Dictionary<string, object?> { ["ParentId"] = master.{{id}}{{(scoped ? ", [\"TenantId\"] = currentTenant.Id!.Value" : string.Empty)}}{{(owned ? ", [\"OrganizationUnitId\"] = master.OrganizationUnitId" : string.Empty)}} }, token).ConfigureAwait(false);
                    return rows.Select(Map).ToArray();
                }
            }
            """;
    }

    private static string ClrType(FullNetColumn column) => (column.ScalarType switch
    {
        FullNetScalarType.Uuid => "Guid", FullNetScalarType.Int32 => "int", FullNetScalarType.Int64 => "long",
        FullNetScalarType.Decimal => "decimal", FullNetScalarType.Boolean => "bool", FullNetScalarType.DateTimeUtc => "DateTimeOffset", _ => "string",
    }) + (column.IsNullable ? "?" : string.Empty);
}
