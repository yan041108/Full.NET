using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>关联维护只返回候选键和显示名，复用同模块查询的租户、软删及组织数据范围。</summary>
internal static class CrudRelationshipLookupGenerator
{
    internal static bool Enabled(FullNetCrudSchema schema) => schema.Scene == FullNetCrudScene.ManyToMany;
    internal static string Side(int index) => index == 0 ? "Source" : "Target";
    internal static string Segment(int index) => index == 0 ? "source" : "target";
    internal static FullNetCrudSchema Principal(FullNetCrudSchema schema, int index)
    {
        var relation = schema.Relationships[index];
        var principal = FullNetCrudRelationshipResolver.Principal(schema, relation);
        var label = Label(principal);
        var columns = principal.Columns.Select(column => column.DatabaseName == relation.PrincipalColumnName || column.DatabaseName == "OrganizationUnitId" || column == label
            ? column with { Ui = column.ResolvedUi with { Queryable = true, QueryKind = column.ScalarType == FullNetScalarType.String ? FullNetColumnQueryKind.Contains : FullNetColumnQueryKind.Equals } }
            : column).ToArray();
        return FullNetCrudSchema.CreateProject(principal.OwnerKey, principal.ModuleKey, principal.EntityKey, principal.DatabaseTableName,
            schema.RootNamespace, schema.ClrTypeName + Side(index), principal.ApiResourceName, principal.PermissionResourceName,
            principal.DataScope, principal.EntityCapabilities, columns);
    }
    internal static FullNetColumn? Label(FullNetCrudSchema principal) => principal.Columns.FirstOrDefault(column => column.ScalarType == FullNetScalarType.String && column.ResolvedUi.ShowInList);

    internal static IEnumerable<GeneratedArtifact> Artifacts(FullNetCrudSchema schema)
    {
        if (!Enabled(schema)) return [];
        return Enumerable.Range(0, 2).SelectMany(index => CrudArtifactGenerator.Generate(Principal(schema, index))
            .Where(artifact => artifact.Kind == GeneratedArtifactKind.Backend && artifact.RelativePath.EndsWith(".g.cs", StringComparison.Ordinal)
                && !artifact.RelativePath.EndsWith("Endpoint.g.cs", StringComparison.Ordinal)))
            .Append(new($"backend/{schema.ClrTypeName}RelationLookup.g.cs", GeneratedArtifactKind.Backend, Service(schema)));
    }

    internal static string Contracts(FullNetCrudSchema schema) => !Enabled(schema) ? string.Empty : $$"""

        /// <summary>关联读取权限下的最小候选投影，不暴露主端完整 DTO。</summary>
        public sealed record {{schema.ClrTypeName}}RelationOption([property: JsonRequired] Guid Value, [property: JsonRequired] string Label);
        """;

    internal static string JsonTypes(FullNetCrudSchema schema) => !Enabled(schema) ? string.Empty
        : $"[JsonSerializable(typeof(PagedResult<{schema.ClrTypeName}RelationOption>))]\n";

    internal static string Registration(FullNetCrudSchema schema) => !Enabled(schema) ? string.Empty : $$"""
        services.TryAddScoped<{{schema.ClrTypeName}}RelationLookupService>();
        services.TryAddScoped<{{schema.ClrTypeName}}SourceQueryService>();
        services.TryAddScoped<{{schema.ClrTypeName}}TargetQueryService>();
        #if FULLNET_AOT_COMPILE
        {{schema.ClrTypeName}}SourceGeneratedMaterializer.Register();
        {{schema.ClrTypeName}}TargetGeneratedMaterializer.Register();
        #endif
        """;

    internal static string Endpoint(FullNetCrudSchema schema)
    {
        if (!Enabled(schema)) return string.Empty;
        var root = schema.ClrTypeName;
        var owned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var prefix = string.Concat(schema.ModuleKey.Split('_').Select((part, index) => index == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
        return string.Join("\n", Enumerable.Range(0, 2).Select(index => $$"""
                    group.MapGet("/relationship-options/{{Segment(index)}}", async (int? page, int? pageSize, string? search, Guid? selected{{(owned ? ", Guid? organizationUnitId" : string.Empty)}}, ClaimsPrincipal principal,
                        {{root}}RelationLookupService service, IApiResultMapper mapper, HttpContext httpContext, CancellationToken token) =>
                    {
                        if (httpContext.Request.Query.Any(pair => pair.Value.Count != 1 || !new[] { "page", "pageSize", "search", "selected"{{(owned ? ", \"organizationUnitId\"" : string.Empty)}} }.Contains(pair.Key, StringComparer.Ordinal)))
                            return mapper.Map(Result<PagedResult<{{root}}RelationOption>>.Failure(new Error("{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.invalid_query", "The relation query is invalid.", ErrorType.Validation)), httpContext);
                        {{(owned ? "if (!TryResolveActor(principal, out var actorUserId, out var isSuperAdministrator)) return Results.Unauthorized();" : string.Empty)}}
                        return mapper.Map(await service.List{{Side(index)}}Async(page ?? 1, pageSize ?? 20, search, selected{{(owned ? ", actorUserId, isSuperAdministrator, organizationUnitId" : string.Empty)}}, token), httpContext);
                    }).WithName("{{prefix}}List{{root}}{{Side(index)}}Options").Produces<PagedResult<{{root}}RelationOption>>(StatusCodes.Status200OK)
                      .ProducesProblem(StatusCodes.Status400BadRequest).RequireAuthorization(FullNetPermissionPolicies.For({{root}}Permissions.Read));
            """));
    }

    private static string Service(FullNetCrudSchema schema)
    {
        var root = schema.ClrTypeName;
        var owned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var methods = string.Join("\n", Enumerable.Range(0, 2).Select(index =>
        {
            var principal = Principal(schema, index);
            var reference = principal.Columns.Single(column => column.DatabaseName == schema.Relationships[index].PrincipalColumnName);
            var label = Label(principal);
            return $$"""
                public async Task<Result<PagedResult<{{root}}RelationOption>>> List{{Side(index)}}Async(int page, int pageSize, string? search{{(owned ? string.Empty : " = null")}}, Guid? selected{{(owned ? string.Empty : " = null")}}{{(owned ? ", Guid actorUserId, bool isSuperAdministrator, Guid? organizationUnitId" : string.Empty)}}, CancellationToken token = default)
                {
                    {{(label is null ? $"if (selected is null && !string.IsNullOrEmpty(search)) {{ if (!Guid.TryParse(search, out var key)) return Result<PagedResult<{root}RelationOption>>.Failure(new Error(\"{schema.ModuleKey}.{schema.PermissionResourceName}.invalid_query\", \"Search requires a complete UUID.\", ErrorType.Validation)); selected = key; }}" : string.Empty)}}
                    var query = new {{principal.ClrTypeName}}ListQuery { Filter{{reference.ClrPropertyName}} = selected{{(label is null ? string.Empty : $", Filter{label.ClrPropertyName} = selected is not null || string.IsNullOrEmpty(search) ? null : search")}}{{(owned ? ", FilterOrganizationUnitId = organizationUnitId" : string.Empty)}} };
                    var result = await {{Segment(index)}}.ListAsync({{(owned ? "actorUserId, isSuperAdministrator, " : string.Empty)}}page, pageSize, token, query).ConfigureAwait(false);
                    if (!result.IsSuccess) return Result<PagedResult<{{root}}RelationOption>>.Failure(result.Error!);
                    var value = result.Value!;
                    return Result<PagedResult<{{root}}RelationOption>>.Success(new(value.Items.Select(item => new {{root}}RelationOption(item.{{reference.ClrPropertyName}},
                        {{(label is null ? $"item.{reference.ClrPropertyName}.ToString(\"D\")" : $"string.IsNullOrWhiteSpace(item.{label.ClrPropertyName}) ? item.{reference.ClrPropertyName}.ToString(\"D\") : item.{label.ClrPropertyName}")}})).ToArray(), value.Page, value.PageSize, value.Total));
                }
                """;
        }));
        return $$"""
            #nullable enable
            using Full.NET.Abstractions.Results;
            namespace {{schema.RootNamespace}}.Generated;
            internal sealed class {{root}}RelationLookupService({{root}}SourceQueryService source, {{root}}TargetQueryService target)
            {
            {{methods}}
            }
            """;
    }
}
