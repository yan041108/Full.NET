using System.Text;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 为作用域已经明确为租户级的 CRUD Schema 生成可显式接入模块的运行时骨架。
/// </summary>
internal static class CrudBackendFeatureGenerator
{
    /// <summary>生成供 Dapper 直接投影的内部持久化记录。</summary>
    internal static string GenerateRecord(FullNetCrudSchema schema)
    {
        EnsureRuntimeScope(schema);
        return Normalize(
            $$"""
            #nullable enable

            using System;

            namespace {{schema.RootNamespace}}.Generated;

            internal sealed record {{schema.ClrTypeName}}Record(
            {{RenderRecordParameters(schema.Columns)}});
            """);
    }

    /// <summary>生成分页查询和事务写入服务。</summary>
    internal static string GenerateFeature(FullNetCrudSchema schema)
    {
        EnsureRuntimeScope(schema);
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitFeature(schema);
        }

        var entity = LowerFirst(schema.ClrTypeName);
        var idParameter = $"{entity}Id";
        var mutableColumns = MutableColumns(schema).ToArray();
        var stringColumns = mutableColumns
            .Where(column => column.ScalarType == FullNetScalarType.String)
            .ToArray();
        var validationCall = stringColumns.Length == 0
            ? string.Empty
            : $$"""
            var validationError = ValidateWriteRequest(
            {{IndentLines(
                string.Join(
                    ",\n",
                    stringColumns.Select(column =>
                        $"request.{column.ClrPropertyName}")),
                4)}});
            if (validationError is not null)
            {
                return validationError;
            }

            """ + "\n";
        var validationMethod = stringColumns.Length == 0
            ? string.Empty
            : GenerateValidationMethod(schema, stringColumns);
        var createValues = RenderCreateValues(schema, idParameter);
        var updateValues = RenderUpdateValues(schema, idParameter);
        var disableValues = RenderDisableValues(schema, idParameter);
        var disableParameters = schema.HasVersion
            ? $"""
              Guid {idParameter},
              Disable{schema.ClrTypeName}Request request,
              CancellationToken cancellationToken = default
              """
            : $"""
              Guid {idParameter},
              CancellationToken cancellationToken = default
              """;
        var disableCoreParameters = schema.HasVersion
            ? $"""
              Guid {idParameter},
              Disable{schema.ClrTypeName}Request request,
              CancellationToken cancellationToken
              """
            : $"""
              Guid {idParameter},
              CancellationToken cancellationToken
              """;
        var disableCoreArgument = schema.HasVersion ? ", request" : string.Empty;
        var managementConstructorParameters = string.Join(
            ",\n",
            new[]
            {
                "IQueryExecutor queryExecutor",
                "ICommandExecutor commandExecutor",
                "ICommandTransaction transaction",
                $"{schema.ClrTypeName}QueryService queries",
            }
            .Concat(schema.DataScope is FullNetCrudDataScope.TenantRequired
                or FullNetCrudDataScope.HostOnly
                ? ["ICurrentTenant currentTenant"]
                : [])
            .Concat(
            [
                "IClock clock",
                "IIdGenerator idGenerator",
            ]));
        var contextGuardLine = schema.DataScope switch
        {
            FullNetCrudDataScope.TenantRequired =>
                "        EnsureTenantContext();",
            FullNetCrudDataScope.HostOnly =>
                "        EnsureHostContext();",
            _ => string.Empty,
        };
        var contextGuardMethod = schema.DataScope switch
        {
            FullNetCrudDataScope.TenantRequired => $$"""
                private void EnsureTenantContext()
                {
                    if (!currentTenant.IsAvailable
                        || currentTenant.IsHost
                        || currentTenant.Id is null)
                    {
                        throw new TenantContextMissingException(
                            "{{schema.ModuleKey}}.tenant_context_required");
                    }
                }
            """,
            FullNetCrudDataScope.HostOnly => $$"""
                private void EnsureHostContext()
                {
                    if (!currentTenant.IsAvailable || !currentTenant.IsHost)
                    {
                        throw new HostContextRequiredException(
                            "{{schema.ModuleKey}}.host_context_required");
                    }
                }
            """,
            _ => string.Empty,
        };
        var updateFailure = schema.HasVersion
            ? $$"""
            return await ResolveWriteFailureAsync(
                    {{idParameter}},
                    cancellationToken)
                .ConfigureAwait(false);
            """
            : "return NotFound();";
        var versionConflictMethod = schema.HasVersion
            ? "\n" + $$"""

                private async Task<Result<{{schema.ClrTypeName}}Response>> ResolveWriteFailureAsync(
                    Guid {{idParameter}},
                    CancellationToken cancellationToken)
                {
                    var record = await queryExecutor
                        .QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                            {{schema.ClrTypeName}}Sql.FindByIdStatement,
                            new { Id = {{idParameter}} },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return record is null ? NotFound() : VersionConflict();
                }

                private static Result<{{schema.ClrTypeName}}Response> VersionConflict() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.VersionConflict,
                        "The resource was updated concurrently.",
                        ErrorType.Conflict));
            """
            : string.Empty;

        return Normalize(
            $$"""
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Full.NET.Abstractions.Ids;
            using Full.NET.Abstractions.Messaging;
            using Full.NET.Abstractions.Results;
            using Full.NET.Abstractions.Tenancy;
            using Full.NET.Abstractions.Time;
            using Full.NET.Data.Abstractions;
            using Microsoft.Extensions.Options;

            namespace {{schema.RootNamespace}}.Generated;

            internal sealed class {{schema.ClrTypeName}}QueryService(
                IQueryExecutor queryExecutor,
                IMultiResultQueryExecutor multiResultQueryExecutor,
                IOptions<DatabaseOptions> databaseOptions)
            {
                public async Task<Result<PagedResult<{{schema.ClrTypeName}}Response>>> ListAsync(
                    int page,
                    int pageSize,
                    CancellationToken cancellationToken = default)
                {
                    page = Math.Max(page, 1);
                    pageSize = Math.Clamp(pageSize, 1, 100);
                    var offset = (long)(page - 1) * pageSize;
                    var statement = databaseOptions.Value.Provider switch
                    {
                        DatabaseProvider.SqlServer =>
                            {{schema.ClrTypeName}}Sql.PageSqlServerStatement,
                        DatabaseProvider.MySql =>
                            {{schema.ClrTypeName}}Sql.PageMySqlStatement,
                        _ => throw new InvalidOperationException(
                            "The configured database provider is not supported."),
                    };
                    var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                            statement,
                            new { Offset = offset, PageSize = pageSize },
                            async (reader, _) =>
                            {
                                var total = await reader.ReadSingleOrDefaultAsync<long>()
                                    .ConfigureAwait(false);
                                var rows = await reader
                                    .ReadAsync<{{schema.ClrTypeName}}Record>()
                                    .ConfigureAwait(false);
                                return (Total: total, Rows: rows);
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Result<PagedResult<{{schema.ClrTypeName}}Response>>.Success(
                        new PagedResult<{{schema.ClrTypeName}}Response>(
                            pageResult.Rows.Select(Map).ToArray(),
                            page,
                            pageSize,
                            pageResult.Total));
                }

                public async Task<Result<{{schema.ClrTypeName}}Response>> GetByIdAsync(
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

                internal Task<Result<{{schema.ClrTypeName}}Response>> FindByIdAsync(
                    Guid {{idParameter}},
                    CancellationToken cancellationToken = default) =>
                    GetByIdAsync({{idParameter}}, cancellationToken);

                private static {{schema.ClrTypeName}}Response Map(
                    {{schema.ClrTypeName}}Record record) =>
                    new(
            {{IndentLines(
                string.Join(
                    ",\n",
                    schema.Columns.Select(column =>
                        $"record.{column.ClrPropertyName}")),
                12)}});

                private static Result<{{schema.ClrTypeName}}Response> NotFound() =>
                    {{schema.ClrTypeName}}FeatureErrors.NotFound();
            }

            internal sealed class {{schema.ClrTypeName}}ManagementService(
            {{IndentLines(managementConstructorParameters, 4)}})
            {
                public Task<Result<{{schema.ClrTypeName}}Response>> CreateAsync(
                    Create{{schema.ClrTypeName}}Request request,
                    CancellationToken cancellationToken = default) =>
                    transaction.ExecuteAsync(
                        token => CreateCoreAsync(request, token),
                        cancellationToken);

                public Task<Result<{{schema.ClrTypeName}}Response>> UpdateAsync(
                    Guid {{idParameter}},
                    Update{{schema.ClrTypeName}}Request request,
                    CancellationToken cancellationToken = default) =>
                    transaction.ExecuteAsync(
                        token => UpdateCoreAsync({{idParameter}}, request, token),
                        cancellationToken);

                public Task<Result<{{schema.ClrTypeName}}Response>> DisableAsync(
            {{IndentLines(disableParameters, 8)}}) =>
                    transaction.ExecuteAsync(
                        token => DisableCoreAsync(
                            {{idParameter}}{{disableCoreArgument}},
                            token),
                        cancellationToken);

                private async Task<Result<{{schema.ClrTypeName}}Response>> CreateCoreAsync(
                    Create{{schema.ClrTypeName}}Request request,
                    CancellationToken cancellationToken)
                {
            {{contextGuardLine}}
            {{IndentLines(validationCall, 8)}}        var {{idParameter}} = idGenerator.NewId();
                    var affectedRows = await commandExecutor.ExecuteAsync(
                            {{schema.ClrTypeName}}Sql.InsertStatement,
                            new
                            {
            {{IndentLines(createValues, 20)}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (affectedRows != 1)
                    {
                        throw new InvalidOperationException(
                            "The generated insert must affect exactly one row.");
                    }

                    return await queries.FindByIdAsync(
                            {{idParameter}},
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                private async Task<Result<{{schema.ClrTypeName}}Response>> UpdateCoreAsync(
                    Guid {{idParameter}},
                    Update{{schema.ClrTypeName}}Request request,
                    CancellationToken cancellationToken)
                {
            {{contextGuardLine}}
            {{IndentLines(validationCall, 8)}}        var affectedRows = await commandExecutor.ExecuteAsync(
                            {{schema.ClrTypeName}}Sql.UpdateStatement,
                            new
                            {
            {{IndentLines(updateValues, 20)}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (affectedRows != 1)
                    {
            {{IndentLines(updateFailure, 12)}}
                    }

                    return await queries.FindByIdAsync(
                            {{idParameter}},
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                private async Task<Result<{{schema.ClrTypeName}}Response>> DisableCoreAsync(
            {{IndentLines(disableCoreParameters, 8)}})
                {
            {{contextGuardLine}}
                    var affectedRows = await commandExecutor.ExecuteAsync(
                            {{schema.ClrTypeName}}Sql.DisableStatement,
                            new
                            {
            {{IndentLines(disableValues, 20)}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (affectedRows != 1)
                    {
            {{IndentLines(updateFailure, 12)}}
                    }

                    return await queries.FindByIdAsync(
                            {{idParameter}},
                            cancellationToken)
                        .ConfigureAwait(false);
                }

            {{contextGuardMethod}}{{validationMethod}}{{versionConflictMethod}}

                private static Result<{{schema.ClrTypeName}}Response> NotFound() =>
                    {{schema.ClrTypeName}}FeatureErrors.NotFound();
            }

            internal static class {{schema.ClrTypeName}}FeatureErrors
            {
                internal static Result<{{schema.ClrTypeName}}Response> NotFound() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.NotFound,
                        "The resource was not found.",
                        ErrorType.NotFound));
            }
            """);
    }

    /// <summary>生成 Minimal API 映射、显式 DI 接入点与 JSON 源生成上下文。</summary>
    internal static string GenerateEndpoint(FullNetCrudSchema schema)
    {
        EnsureRuntimeScope(schema);
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitEndpoint(schema);
        }

        var entity = LowerFirst(schema.ClrTypeName);
        var idParameter = $"{entity}Id";
        var moduleTag = UpperFirst(schema.ModuleKey);
        var apiPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var itemRoute = $"/{{{idParameter}:guid}}";
        var disableParameters = schema.HasVersion
            ? $"""
              Guid {idParameter},
              Disable{schema.ClrTypeName}Request request,
              {schema.ClrTypeName}ManagementService service,
              IApiResultMapper mapper,
              HttpContext httpContext,
              CancellationToken cancellationToken
              """
            : $"""
              Guid {idParameter},
              {schema.ClrTypeName}ManagementService service,
              IApiResultMapper mapper,
              HttpContext httpContext,
              CancellationToken cancellationToken
              """;
        var disableArgument = schema.HasVersion ? ", request" : string.Empty;
        var disableJson = schema.HasVersion
            ? $"[JsonSerializable(typeof(Disable{schema.ClrTypeName}Request))]\n"
            : string.Empty;

        return Normalize(
            $$"""
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Threading;
            using Full.NET.Abstractions.Ids;
            using Full.NET.Abstractions.Results;
            using Full.NET.Abstractions.Time;
            using Full.NET.Hosting.Api;
            using Full.NET.Modules.Identity.Contracts;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            namespace {{schema.RootNamespace}}.Generated;

            internal static class {{schema.ClrTypeName}}Endpoint
            {
                internal static void Map(IEndpointRouteBuilder endpoints)
                {
                    var group = endpoints.MapGroup("{{apiPath}}")
                        .WithTags("{{moduleTag}}");

                    group.MapGet("/", async (
                        int? page,
                        int? pageSize,
                        {{schema.ClrTypeName}}QueryService queries,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await queries.ListAsync(
                                page ?? 1,
                                pageSize ?? 20,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                    .Produces<PagedResult<{{schema.ClrTypeName}}Response>>(
                        StatusCodes.Status200OK)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Read));

                    group.MapGet("{{itemRoute}}", async (
                        Guid {{idParameter}},
                        {{schema.ClrTypeName}}QueryService queries,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await queries.GetByIdAsync(
                                {{idParameter}},
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                    .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status200OK)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Read));

                    group.MapPost("/", async (
                        Create{{schema.ClrTypeName}}Request request,
                        {{schema.ClrTypeName}}ManagementService service,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await service.CreateAsync(request, cancellationToken)
                            .ConfigureAwait(false);
                        if (!result.IsSuccess)
                        {
                            return mapper.Map(result, httpContext);
                        }

                        return Results.Created(
                            $"{{apiPath}}/{result.Value!.Id:D}",
                            result.Value);
                    })
                    .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status201Created)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Write));

                    group.MapPut("{{itemRoute}}", async (
                        Guid {{idParameter}},
                        Update{{schema.ClrTypeName}}Request request,
                        {{schema.ClrTypeName}}ManagementService service,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await service.UpdateAsync(
                                {{idParameter}},
                                request,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                    .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status200OK)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Write));

                    group.MapPost("{{itemRoute}}/disable", async (
            {{IndentLines(disableParameters, 12)}}) =>
                    {
                        var result = await service.DisableAsync(
                                {{idParameter}}{{disableArgument}},
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                    .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status200OK)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Write));
                }
            }

            public static class {{schema.ClrTypeName}}GeneratedFeatureExtensions
            {
                public static IServiceCollection AddGenerated{{schema.ClrTypeName}}Feature(
                    this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);
                    services.TryAddSingleton<IClock, SystemClock>();
                    services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
                    services.TryAddScoped<{{schema.ClrTypeName}}QueryService>();
                    services.TryAddScoped<{{schema.ClrTypeName}}ManagementService>();
                    services.ConfigureHttpJsonOptions(options =>
                        options.SerializerOptions.TypeInfoResolverChain.Insert(
                            0,
                            {{schema.ClrTypeName}}JsonSerializerContext.Default));
                    return services;
                }

                public static IEndpointRouteBuilder MapGenerated{{schema.ClrTypeName}}Feature(
                    this IEndpointRouteBuilder endpoints)
                {
                    ArgumentNullException.ThrowIfNull(endpoints);
                    {{schema.ClrTypeName}}Endpoint.Map(endpoints);
                    return endpoints;
                }
            }

            [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
            [JsonSerializable(typeof(Create{{schema.ClrTypeName}}Request))]
            [JsonSerializable(typeof(Update{{schema.ClrTypeName}}Request))]
            {{disableJson}}[JsonSerializable(typeof({{schema.ClrTypeName}}Response))]
            [JsonSerializable(typeof(PagedResult<{{schema.ClrTypeName}}Response>))]
            internal partial class {{schema.ClrTypeName}}JsonSerializerContext
                : JsonSerializerContext;
            """);
    }

    /// <summary>生成附加到现有 SQL 文本类中的租户作用域执行声明。</summary>
    internal static string GenerateSqlStatementMembers(FullNetCrudSchema schema)
    {
        if (schema.DataScope == FullNetCrudDataScope.Unspecified)
        {
            return string.Empty;
        }

        var module = schema.ModuleKey;
        var entity = schema.EntityKey;
        var resources = schema.PermissionResourceName;
        var statementScope = schema.DataScope switch
        {
            FullNetCrudDataScope.TenantRequired =>
                "SqlDataScope.TenantRequired",
            FullNetCrudDataScope.HostOnly =>
                "SqlDataScope.HostOnly",
            FullNetCrudDataScope.Global =>
                "SqlDataScope.Global",
            _ => throw new ArgumentOutOfRangeException(
                nameof(schema),
                schema.DataScope,
                "不支持的 CRUD 数据作用域。"),
        };
        var tenantBinding = schema.IsTenantScoped
            ? "SqlTenantBinding.CurrentTenantId"
            : "SqlTenantBinding.None";
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitSqlStatementMembers(
                schema,
                statementScope,
                tenantBinding);
        }

        return "\n\n" + IndentLines(
            $$"""
            public static readonly SqlStatement FindByIdStatement = new(
                "{{module}}.find_{{entity}}_by_id",
                FindById,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement PageSqlServerStatement = new(
                "{{module}}.list_{{resources}}.sql_server",
                Count + "\n" + ListSqlServer,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement PageMySqlStatement = new(
                "{{module}}.list_{{resources}}.my_sql",
                Count + "\n" + ListMySql,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement InsertStatement = new(
                "{{module}}.insert_{{entity}}",
                Insert,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement UpdateStatement = new(
                "{{module}}.update_{{entity}}",
                Update,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement DisableStatement = new(
                "{{module}}.disable_{{entity}}",
                Disable,
                {{statementScope}},
                {{tenantBinding}});
            """,
            4);
    }

    /// <summary>生成租户运行时骨架使用的稳定错误码。</summary>
    internal static string GenerateErrorCodes(FullNetCrudSchema schema)
    {
        if (schema.DataScope == FullNetCrudDataScope.Unspecified)
        {
            return string.Empty;
        }

        var members = new List<string>
        {
            $$"""
            public const string NotFound =
                "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.not_found";
            """
        };
        if (schema.HasVersion)
        {
            members.Add(
                $$"""
                public const string VersionConflict =
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.version_conflict";
                """);
        }

        return "\n\n" + $$"""
        public static class {{schema.ClrTypeName}}ErrorCodes
        {
        {{IndentLines(string.Join("\n\n", members), 4)}}
        }
        """;
    }

    private static string GenerateValidationMethod(
        FullNetCrudSchema schema,
        IReadOnlyList<FullNetColumn> columns)
    {
        var parameters = string.Join(
            ",\n",
            columns.Select(column =>
                $"string? {LowerFirst(column.ClrPropertyName)}"));
        var checks = new List<string>();
        foreach (var column in columns)
        {
            var parameter = LowerFirst(column.ClrPropertyName);
            var invalidCondition = column.IsNullable
                ? $"{parameter} is {{ Length: > {column.MaxLength} }}"
                : $"{parameter} is null || {parameter}.Length > {column.MaxLength}";
            checks.Add(
                $$"""
                if ({{invalidCondition}})
                {
                    return ValidationFailure("{{column.ClrPropertyName}}");
                }
                """);
        }

        return "\n" + $$"""

            private static Result<{{schema.ClrTypeName}}Response>? ValidateWriteRequest(
        {{IndentLines(parameters, 8)}})
            {
        {{IndentLines(string.Join("\n\n", checks), 8)}}
                return null;
            }

            private static Result<{{schema.ClrTypeName}}Response> ValidationFailure(
                string field) =>
                Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                    ValidationErrorCodes.Failed,
                    "One or more generated field constraints were not satisfied.",
                    ErrorType.Validation,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        [field] = ["The field value is invalid."],
                    }));
        """;
    }

    private static string GenerateExplicitFeature(FullNetCrudSchema schema)
    {
        var entity = LowerFirst(schema.ClrTypeName);
        var idParameter = $"{entity}Id";
        var writableColumns = WritableColumns(schema).ToArray();
        var stringColumns = writableColumns
            .Where(column => column.ScalarType == FullNetScalarType.String)
            .ToArray();
        var validationCall = stringColumns.Length == 0
            ? string.Empty
            : $$"""
            var validationError = ValidateWriteRequest(
            {{IndentLines(
                string.Join(
                    ",\n",
                    stringColumns.Select(column =>
                        $"request.{column.ClrPropertyName}")),
                4)}});
            if (validationError is not null)
            {
                return validationError;
            }

            """ + "\n";
        var validationMethod = stringColumns.Length == 0
            ? string.Empty
            : GenerateValidationMethod(schema, stringColumns);
        var contextGuardLine = schema.DataScope switch
        {
            FullNetCrudDataScope.TenantRequired =>
                "        EnsureTenantContext();",
            FullNetCrudDataScope.HostOnly =>
                "        EnsureHostContext();",
            _ => string.Empty,
        };
        var contextGuardMethod = schema.DataScope switch
        {
            FullNetCrudDataScope.TenantRequired => $$"""
                private void EnsureTenantContext()
                {
                    if (!currentTenant.IsAvailable
                        || currentTenant.IsHost
                        || currentTenant.Id is null)
                    {
                        throw new TenantContextMissingException(
                            "{{schema.ModuleKey}}.tenant_context_required");
                    }
                }
            """,
            FullNetCrudDataScope.HostOnly => $$"""
                private void EnsureHostContext()
                {
                    if (!currentTenant.IsAvailable || !currentTenant.IsHost)
                    {
                        throw new HostContextRequiredException(
                            "{{schema.ModuleKey}}.host_context_required");
                    }
                }
            """,
            _ => string.Empty,
        };
        var conflictMethods = schema.HasVersion
            ? GenerateExplicitConflictMethods(schema, idParameter)
            : string.Empty;
        var isOrganizationOwned =
            CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var organizationFeatureUsings = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.FeatureUsings()
            : string.Empty;
        var organizationDataScopeComposer = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.DataScopeComposerClass()
            : string.Empty;
        var listMethod = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.QueryListMethod(schema)
            : GenerateDefaultQueryListMethod(schema);
        var getByIdMethod = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.QueryGetByIdMethod(schema, idParameter)
            : GenerateDefaultQueryGetByIdMethod(schema, idParameter);
        var findByIdMethod = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.InternalFindByIdMethod(schema, idParameter)
            : $$"""

                internal Task<Result<{{schema.ClrTypeName}}Response>> FindByIdAsync(
                    Guid {{idParameter}},
                    CancellationToken cancellationToken = default) =>
                    GetByIdAsync({{idParameter}}, cancellationToken);
            """;
        var queryServiceConstructorParameters = string.Join(
            ",\n",
            new[]
            {
                "IQueryExecutor queryExecutor",
                "IMultiResultQueryExecutor multiResultQueryExecutor",
                "IOptions<DatabaseOptions> databaseOptions",
            }
            .Concat(isOrganizationOwned
                ? CrudOrganizationOwnershipGenerator
                    .QueryServiceConstructorParameters()
                    .Split(",\n", StringSplitOptions.TrimEntries)
                : []));
        var managementConstructorParameters = string.Join(
            ",\n",
            new[]
            {
                "IQueryExecutor queryExecutor",
                "ICommandExecutor commandExecutor",
                "ICommandTransaction transaction",
                $"{schema.ClrTypeName}QueryService queries",
            }
            .Concat(schema.DataScope is FullNetCrudDataScope.TenantRequired
                or FullNetCrudDataScope.HostOnly
                ? ["ICurrentTenant currentTenant"]
                : [])
            .Concat(
            [
                "IClock clock",
                "IIdGenerator idGenerator",
            ])
            .Concat(isOrganizationOwned
                ? [CrudOrganizationOwnershipGenerator.ManagementConstructorParameters()]
                : []));
        var createAsyncParameters = isOrganizationOwned
            ? $"""
              Create{schema.ClrTypeName}Request request,
              Guid actorUserId,
              Guid organizationUnitId,
              CancellationToken cancellationToken = default
              """
            : $"""
              Create{schema.ClrTypeName}Request request,
              Guid actorUserId,
              CancellationToken cancellationToken = default
              """;
        var createCoreParameters = createAsyncParameters;
        var createCoreAuthorization = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.CreateAuthorizationBlock(schema)
            : string.Empty;
        var updateMethods = schema.EntityCapabilities.CanUpdate
            ? GenerateExplicitUpdateMethods(
                schema,
                idParameter,
                contextGuardLine,
                validationCall,
                isOrganizationOwned
                    ? CrudOrganizationOwnershipGenerator.UpdateAuthorizationBlock(
                        schema,
                        idParameter)
                    : string.Empty)
            : string.Empty;
        var deleteMethods = schema.EntityCapabilities.CanDelete
            ? GenerateExplicitDeleteMethods(
                schema,
                idParameter,
                contextGuardLine,
                isOrganizationOwned
                    ? CrudOrganizationOwnershipGenerator.DeleteAuthorizationBlock(schema)
                    : string.Empty)
            : string.Empty;

        return Normalize(
            $$"""
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Full.NET.Abstractions.Ids;
            using Full.NET.Abstractions.Messaging;
            using Full.NET.Abstractions.Results;
            using Full.NET.Abstractions.Tenancy;
            using Full.NET.Abstractions.Time;
            using Full.NET.Data.Abstractions;
            using Full.NET.Modules.Identity.Contracts;
            using Microsoft.Extensions.Options;
            {{organizationFeatureUsings}}
            namespace {{schema.RootNamespace}}.Generated;

            internal sealed class {{schema.ClrTypeName}}QueryService(
            {{IndentLines(queryServiceConstructorParameters, 4)}})
            {
            {{IndentLines(listMethod, 4)}}
            {{IndentLines(getByIdMethod, 4)}}
            {{IndentLines(findByIdMethod, 4)}}

                private static {{schema.ClrTypeName}}Response Map(
                    {{schema.ClrTypeName}}Record record) =>
                    new(
            {{IndentLines(
                string.Join(
                    ",\n",
                    schema.Columns.Select(column =>
                        $"record.{column.ClrPropertyName}")),
                12)}});

                private static Result<{{schema.ClrTypeName}}Response> NotFound() =>
                    {{schema.ClrTypeName}}FeatureErrors.NotFound();
            {{organizationDataScopeComposer}}
            }

            internal sealed class {{schema.ClrTypeName}}ManagementService(
            {{IndentLines(managementConstructorParameters, 4)}})
            {
                public Task<Result<{{schema.ClrTypeName}}Response>> CreateAsync(
                    {{createAsyncParameters}}) =>
                    transaction.ExecuteAsync(
                        token => CreateCoreAsync(
                            request,
                            actorUserId,
                            {{(isOrganizationOwned ? "organizationUnitId," : string.Empty)}}
                            token),
                        cancellationToken);

                private async Task<Result<{{schema.ClrTypeName}}Response>> CreateCoreAsync(
                    {{createCoreParameters}})
                {
            {{contextGuardLine}}
            {{IndentLines(createCoreAuthorization, 8)}}{{IndentLines(validationCall, 8)}}        var {{idParameter}} = idGenerator.NewId();
                    var affectedRows = await commandExecutor.ExecuteAsync(
                            {{schema.ClrTypeName}}Sql.InsertStatement,
                            new
                            {
            {{IndentLines(RenderExplicitCreateValues(
                schema,
                idParameter), 20)}}
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (affectedRows != 1)
                    {
                        throw new InvalidOperationException(
                            "The generated insert must affect exactly one row.");
                    }

                    return await queries.FindByIdAsync(
                            {{idParameter}},
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            {{updateMethods}}{{deleteMethods}}
            {{contextGuardMethod}}{{validationMethod}}{{conflictMethods}}

                private static Result<{{schema.ClrTypeName}}Response> NotFound() =>
                    {{schema.ClrTypeName}}FeatureErrors.NotFound();
            }

            internal static class {{schema.ClrTypeName}}FeatureErrors
            {
                internal static Result<{{schema.ClrTypeName}}Response> NotFound() =>
                    Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                        {{schema.ClrTypeName}}ErrorCodes.NotFound,
                        "The resource was not found.",
                        ErrorType.NotFound));
            }
            """);
    }

    private static string GenerateDefaultQueryListMethod(FullNetCrudSchema schema) =>
        $$"""

                public async Task<Result<PagedResult<{{schema.ClrTypeName}}Response>>> ListAsync(
                    int page,
                    int pageSize,
                    CancellationToken cancellationToken = default)
                {
                    page = Math.Max(page, 1);
                    pageSize = Math.Clamp(pageSize, 1, 100);
                    var offset = (long)(page - 1) * pageSize;
                    var statement = databaseOptions.Value.Provider switch
                    {
                        DatabaseProvider.SqlServer =>
                            {{schema.ClrTypeName}}Sql.PageSqlServerStatement,
                        DatabaseProvider.MySql =>
                            {{schema.ClrTypeName}}Sql.PageMySqlStatement,
                        _ => throw new InvalidOperationException(
                            "The configured database provider is not supported."),
                    };
                    var pageResult = await multiResultQueryExecutor.QueryMultipleAsync(
                            statement,
                            new { Offset = offset, PageSize = pageSize },
                            async (reader, _) =>
                            {
                                var total = await reader.ReadSingleOrDefaultAsync<long>()
                                    .ConfigureAwait(false);
                                var rows = await reader
                                    .ReadAsync<{{schema.ClrTypeName}}Record>()
                                    .ConfigureAwait(false);
                                return (Total: total, Rows: rows);
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Result<PagedResult<{{schema.ClrTypeName}}Response>>.Success(
                        new PagedResult<{{schema.ClrTypeName}}Response>(
                            pageResult.Rows.Select(Map).ToArray(),
                            page,
                            pageSize,
                            pageResult.Total));
                }
        """;

    private static string GenerateDefaultQueryGetByIdMethod(
        FullNetCrudSchema schema,
        string idParameter) =>
        $$"""

                public async Task<Result<{{schema.ClrTypeName}}Response>> GetByIdAsync(
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

    private static string GenerateExplicitUpdateMethods(
        FullNetCrudSchema schema,
        string idParameter,
        string contextGuardLine,
        string validationCall,
        string authorizationBlock = "")
    {
        var updateFailure = schema.HasVersion
            ? $$"""
            return await ResolveWriteFailureAsync(
                    {{idParameter}},
                    cancellationToken)
                .ConfigureAwait(false);
            """
            : "return NotFound();";
        return "\n" + $$"""

            public Task<Result<{{schema.ClrTypeName}}Response>> UpdateAsync(
                Guid {{idParameter}},
                Update{{schema.ClrTypeName}}Request request,
                Guid actorUserId,
                CancellationToken cancellationToken = default) =>
                transaction.ExecuteAsync(
                    token => UpdateCoreAsync(
                        {{idParameter}},
                        request,
                        actorUserId,
                        token),
                    cancellationToken);

            private async Task<Result<{{schema.ClrTypeName}}Response>> UpdateCoreAsync(
                Guid {{idParameter}},
                Update{{schema.ClrTypeName}}Request request,
                Guid actorUserId,
                CancellationToken cancellationToken)
            {
        {{contextGuardLine}}
        {{IndentLines(authorizationBlock, 4)}}{{IndentLines(validationCall, 4)}}        var affectedRows = await commandExecutor.ExecuteAsync(
                        {{schema.ClrTypeName}}Sql.UpdateStatement,
                        new
                        {
        {{IndentLines(RenderExplicitUpdateValues(
            schema,
            idParameter), 20)}}
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affectedRows != 1)
                {
        {{IndentLines(updateFailure, 12)}}
                }

                return await queries.FindByIdAsync(
                        {{idParameter}},
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        """;
    }

    private static string GenerateExplicitDeleteMethods(
        FullNetCrudSchema schema,
        string idParameter,
        string contextGuardLine,
        string authorizationBlock = "")
    {
        var requestParameter = schema.HasVersion
            ? $",\n            Delete{schema.ClrTypeName}Request request"
            : string.Empty;
        var requestArgument = schema.HasVersion ? ", request" : string.Empty;
        var deleteFailure = schema.HasVersion
            ? $$"""
            return await ResolveWriteFailureAsync(
                    {{idParameter}},
                    cancellationToken)
                .ConfigureAwait(false);
            """
            : "return NotFound();";
        return "\n" + $$"""

            public Task<Result<{{schema.ClrTypeName}}Response>> DeleteAsync(
                Guid {{idParameter}}{{requestParameter}},
                Guid actorUserId,
                CancellationToken cancellationToken = default) =>
                transaction.ExecuteAsync(
                    token => DeleteCoreAsync(
                        {{idParameter}}{{requestArgument}},
                        actorUserId,
                        token),
                    cancellationToken);

            private async Task<Result<{{schema.ClrTypeName}}Response>> DeleteCoreAsync(
                Guid {{idParameter}}{{requestParameter}},
                Guid actorUserId,
                CancellationToken cancellationToken)
            {
        {{contextGuardLine}}
                var existing = await queries.FindByIdAsync(
                        {{idParameter}},
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!existing.IsSuccess)
                {
                    return existing;
                }

        {{IndentLines(authorizationBlock, 8)}}
                var affectedRows = await commandExecutor.ExecuteAsync(
                        {{schema.ClrTypeName}}Sql.DeleteStatement,
                        new
                        {
        {{IndentLines(RenderExplicitDeleteValues(
            schema,
            idParameter), 20)}}
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affectedRows != 1)
                {
        {{IndentLines(deleteFailure, 12)}}
                }

                return existing;
            }
        """;
    }

    private static string GenerateExplicitConflictMethods(
        FullNetCrudSchema schema,
        string idParameter) =>
        "\n" + $$"""

            private async Task<Result<{{schema.ClrTypeName}}Response>> ResolveWriteFailureAsync(
                Guid {{idParameter}},
                CancellationToken cancellationToken)
            {
                var record = await queryExecutor
                    .QuerySingleOrDefaultAsync<{{schema.ClrTypeName}}Record>(
                        {{schema.ClrTypeName}}Sql.FindByIdStatement,
                        new { Id = {{idParameter}} },
                        cancellationToken)
                    .ConfigureAwait(false);
                return record is null ? NotFound() : VersionConflict();
            }

            private static Result<{{schema.ClrTypeName}}Response> VersionConflict() =>
                Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                    {{schema.ClrTypeName}}ErrorCodes.VersionConflict,
                    "The resource was updated concurrently.",
                    ErrorType.Conflict));
        """;

    private static string GenerateExplicitEndpoint(FullNetCrudSchema schema)
    {
        var entity = LowerFirst(schema.ClrTypeName);
        var idParameter = $"{entity}Id";
        var moduleTag = UpperFirst(schema.ModuleKey);
        var apiPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var itemRoute = $"/{{{idParameter}:guid}}";
        var updateEndpoint = schema.EntityCapabilities.CanUpdate
            ? GenerateExplicitUpdateEndpoint(schema, idParameter, itemRoute)
            : string.Empty;
        var deleteEndpoint = schema.EntityCapabilities.CanDelete
            ? GenerateExplicitDeleteEndpoint(schema, idParameter, itemRoute)
            : string.Empty;
        var updateJson = schema.EntityCapabilities.CanUpdate
            ? $"[JsonSerializable(typeof(Update{schema.ClrTypeName}Request))]\n"
            : string.Empty;
        var deleteJson = schema.EntityCapabilities.CanDelete
            && schema.HasVersion
            ? $"[JsonSerializable(typeof(Delete{schema.ClrTypeName}Request))]\n"
            : string.Empty;
        var isOrganizationOwned =
            CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var organizationEndpointUsings = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.EndpointUsings()
            : string.Empty;
        var listEndpointHandler = isOrganizationOwned
            ? $$"""
                    group.MapGet("/", async (
                        ClaimsPrincipal principal,
                        int? page,
                        int? pageSize,
                        {{schema.ClrTypeName}}QueryService queries,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        if (!TryResolveActor(
                                principal,
                                out var actorUserId,
                                out var isSuperAdministrator))
                        {
                            return Results.Unauthorized();
                        }

                        var result = await queries.ListAsync(
                                actorUserId,
                                isSuperAdministrator,
                                page ?? 1,
                                pageSize ?? 20,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                """
            : $$"""
                    group.MapGet("/", async (
                        int? page,
                        int? pageSize,
                        {{schema.ClrTypeName}}QueryService queries,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await queries.ListAsync(
                                page ?? 1,
                                pageSize ?? 20,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                """;
        var getByIdEndpointHandler = isOrganizationOwned
            ? $$"""
                    group.MapGet("{{itemRoute}}", async (
                        Guid {{idParameter}},
                        ClaimsPrincipal principal,
                        {{schema.ClrTypeName}}QueryService queries,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        if (!TryResolveActor(
                                principal,
                                out var actorUserId,
                                out var isSuperAdministrator))
                        {
                            return Results.Unauthorized();
                        }

                        var result = await queries.GetByIdAsync(
                                {{idParameter}},
                                actorUserId,
                                isSuperAdministrator,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                """
            : $$"""
                    group.MapGet("{{itemRoute}}", async (
                        Guid {{idParameter}},
                        {{schema.ClrTypeName}}QueryService queries,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        var result = await queries.GetByIdAsync(
                                {{idParameter}},
                                cancellationToken)
                            .ConfigureAwait(false);
                        return mapper.Map(result, httpContext);
                    })
                """;
        var createEndpointHandler = isOrganizationOwned
            ? $$"""
                    group.MapPost("/", async (
                        Create{{schema.ClrTypeName}}Request request,
                        ClaimsPrincipal principal,
                        {{schema.ClrTypeName}}ManagementService service,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        if (!TryResolveActorUserId(principal, out var actorUserId))
                        {
                            return Results.Unauthorized();
                        }

                        if (!TryResolveOrganizationUnitId(
                                httpContext,
                                out var organizationUnitId))
                        {
                            return Results.BadRequest();
                        }

                        var result = await service.CreateAsync(
                                request,
                                actorUserId,
                                organizationUnitId,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!result.IsSuccess)
                        {
                            return mapper.Map(result, httpContext);
                        }

                        return Results.Created(
                            $"{{apiPath}}/{result.Value!.Id:D}",
                            result.Value);
                    })
                """
            : $$"""
                    group.MapPost("/", async (
                        Create{{schema.ClrTypeName}}Request request,
                        ClaimsPrincipal principal,
                        {{schema.ClrTypeName}}ManagementService service,
                        IApiResultMapper mapper,
                        HttpContext httpContext,
                        CancellationToken cancellationToken) =>
                    {
                        if (!TryResolveActorUserId(principal, out var actorUserId))
                        {
                            return Results.Unauthorized();
                        }

                        var result = await service.CreateAsync(
                                request,
                                actorUserId,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!result.IsSuccess)
                        {
                            return mapper.Map(result, httpContext);
                        }

                        return Results.Created(
                            $"{{apiPath}}/{result.Value!.Id:D}",
                            result.Value);
                    })
                """;
        var endpointActorResolverMethods = isOrganizationOwned
            ? CrudOrganizationOwnershipGenerator.EndpointActorResolverMethods()
            : string.Empty;

        return Normalize(
            $$"""
            #nullable enable

            using System;
            using System.Security.Claims;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Threading;
            using Full.NET.Abstractions.Ids;
            using Full.NET.Abstractions.Results;
            using Full.NET.Abstractions.Time;
            using Full.NET.Hosting.Api;
            using Full.NET.Modules.Identity.Contracts;
            using Microsoft.AspNetCore.Builder;
            {{organizationEndpointUsings}}
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            namespace {{schema.RootNamespace}}.Generated;

            internal static class {{schema.ClrTypeName}}Endpoint
            {
                internal static void Map(IEndpointRouteBuilder endpoints)
                {
                    var group = endpoints.MapGroup("{{apiPath}}")
                        .WithTags("{{moduleTag}}");

            {{IndentLines(listEndpointHandler, 8)}}
                    .Produces<PagedResult<{{schema.ClrTypeName}}Response>>(
                        StatusCodes.Status200OK)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Read));

            {{IndentLines(getByIdEndpointHandler, 8)}}
                    .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status200OK)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Read));

            {{IndentLines(createEndpointHandler, 8)}}
                    .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status201Created)
                    .RequireAuthorization(FullNetPermissionPolicies.For(
                        {{schema.ClrTypeName}}Permissions.Write));
            {{updateEndpoint}}{{deleteEndpoint}}
                }

                private static bool TryResolveActorUserId(
                    ClaimsPrincipal principal,
                    out Guid actorUserId) =>
                    Guid.TryParse(
                        principal.FindFirstValue(
                            FullNetIdentityClaimTypes.Subject),
                        out actorUserId);
            {{endpointActorResolverMethods}}
            }

            public static class {{schema.ClrTypeName}}GeneratedFeatureExtensions
            {
                public static IServiceCollection AddGenerated{{schema.ClrTypeName}}Feature(
                    this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);
                    services.TryAddSingleton<IClock, SystemClock>();
                    services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
                    services.TryAddScoped<{{schema.ClrTypeName}}QueryService>();
                    services.TryAddScoped<{{schema.ClrTypeName}}ManagementService>();
                    services.ConfigureHttpJsonOptions(options =>
                        options.SerializerOptions.TypeInfoResolverChain.Insert(
                            0,
                            {{schema.ClrTypeName}}JsonSerializerContext.Default));
                    return services;
                }

                public static IEndpointRouteBuilder MapGenerated{{schema.ClrTypeName}}Feature(
                    this IEndpointRouteBuilder endpoints)
                {
                    ArgumentNullException.ThrowIfNull(endpoints);
                    {{schema.ClrTypeName}}Endpoint.Map(endpoints);
                    return endpoints;
                }
            }

            [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
            [JsonSerializable(typeof(Create{{schema.ClrTypeName}}Request))]
            {{updateJson}}{{deleteJson}}[JsonSerializable(typeof({{schema.ClrTypeName}}Response))]
            [JsonSerializable(typeof(PagedResult<{{schema.ClrTypeName}}Response>))]
            internal partial class {{schema.ClrTypeName}}JsonSerializerContext
                : JsonSerializerContext;
            """);
    }

    private static string GenerateExplicitUpdateEndpoint(
        FullNetCrudSchema schema,
        string idParameter,
        string itemRoute) =>
        "\n" + IndentLines(
            $$"""

            group.MapPut("{{itemRoute}}", async (
                Guid {{idParameter}},
                Update{{schema.ClrTypeName}}Request request,
                ClaimsPrincipal principal,
                {{schema.ClrTypeName}}ManagementService service,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryResolveActorUserId(principal, out var actorUserId))
                {
                    return Results.Unauthorized();
                }

                var result = await service.UpdateAsync(
                        {{idParameter}},
                        request,
                        actorUserId,
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
            .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                {{schema.ClrTypeName}}Permissions.Write));
            """,
            8);

    private static string GenerateExplicitDeleteEndpoint(
        FullNetCrudSchema schema,
        string idParameter,
        string itemRoute)
    {
        var requestParameter = schema.HasVersion
            ? $"\n    Delete{schema.ClrTypeName}Request request,"
            : string.Empty;
        var requestArgument = schema.HasVersion ? ", request" : string.Empty;
        return "\n" + IndentLines(
            $$"""

            group.MapPost("{{itemRoute}}/delete", async (
                Guid {{idParameter}},{{requestParameter}}
                ClaimsPrincipal principal,
                {{schema.ClrTypeName}}ManagementService service,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryResolveActorUserId(principal, out var actorUserId))
                {
                    return Results.Unauthorized();
                }

                var result = await service.DeleteAsync(
                        {{idParameter}}{{requestArgument}},
                        actorUserId,
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
            .Produces<{{schema.ClrTypeName}}Response>(StatusCodes.Status200OK)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                {{schema.ClrTypeName}}Permissions.Write));
            """,
            8);
    }

    private static string GenerateExplicitSqlStatementMembers(
        FullNetCrudSchema schema,
        string statementScope,
        string tenantBinding)
    {
        var members = new List<string>
        {
            $$"""
            public static readonly SqlStatement FindByIdStatement = new(
                "{{schema.ModuleKey}}.find_{{schema.EntityKey}}_by_id",
                FindById,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement CountStatement = new(
                "{{schema.ModuleKey}}.count_{{schema.PermissionResourceName}}",
                Count,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement ListSqlServerStatement = new(
                "{{schema.ModuleKey}}.list_{{schema.PermissionResourceName}}.sql_server.rows",
                ListSqlServer,
                {{statementScope}},
                {{tenantBinding}});

            public static readonly SqlStatement ListMySqlStatement = new(
                "{{schema.ModuleKey}}.list_{{schema.PermissionResourceName}}.my_sql.rows",
                ListMySql,
                {{statementScope}},
                {{tenantBinding}});
            """,
            $$"""
            public static readonly SqlStatement PageSqlServerStatement = new(
                "{{schema.ModuleKey}}.list_{{schema.PermissionResourceName}}.sql_server",
                Count + "\n" + ListSqlServer,
                {{statementScope}},
                {{tenantBinding}});
            """,
            $$"""
            public static readonly SqlStatement PageMySqlStatement = new(
                "{{schema.ModuleKey}}.list_{{schema.PermissionResourceName}}.my_sql",
                Count + "\n" + ListMySql,
                {{statementScope}},
                {{tenantBinding}});
            """,
            $$"""
            public static readonly SqlStatement InsertStatement = new(
                "{{schema.ModuleKey}}.insert_{{schema.EntityKey}}",
                Insert,
                {{statementScope}},
                {{tenantBinding}});
            """,
        };
        if (schema.EntityCapabilities.CanUpdate)
        {
            members.Add(
                $$"""
                public static readonly SqlStatement UpdateStatement = new(
                    "{{schema.ModuleKey}}.update_{{schema.EntityKey}}",
                    Update,
                    {{statementScope}},
                    {{tenantBinding}});
                """);
        }

        if (schema.EntityCapabilities.CanDelete)
        {
            members.Add(
                $$"""
                public static readonly SqlStatement DeleteStatement = new(
                    "{{schema.ModuleKey}}.delete_{{schema.EntityKey}}",
                    Delete,
                    {{statementScope}},
                    {{tenantBinding}});
                """);
        }

        return "\n\n" + IndentLines(
            string.Join("\n\n", members),
            4);
    }

    private static string RenderExplicitCreateValues(
        FullNetCrudSchema schema,
        string idParameter) =>
        string.Join(
            ",\n",
            schema.Columns
                .Where(column => column.DatabaseName != "TenantId")
                .Select(column => column.DatabaseName switch
                {
                    "Id" => $"Id = {idParameter}",
                    "CreatedAtUtc" => "CreatedAtUtc = clock.UtcNow",
                    "CreatedById" => "CreatedById = actorUserId",
                    "UpdatedAtUtc" => "UpdatedAtUtc = (DateTimeOffset?)null",
                    "UpdatedById" => "UpdatedById = (Guid?)null",
                    "IsDeleted" => "IsDeleted = false",
                    "DeletedAtUtc" => "DeletedAtUtc = (DateTimeOffset?)null",
                    "DeletedById" => "DeletedById = (Guid?)null",
                    "OrganizationUnitId" => "OrganizationUnitId = organizationUnitId",
                    "Version" => $"Version = {InitialValue(column)}",
                    _ => $"request.{column.ClrPropertyName}",
                }));

    private static string RenderExplicitUpdateValues(
        FullNetCrudSchema schema,
        string idParameter) =>
        string.Join(
            ",\n",
            new[] { $"Id = {idParameter}" }
                .Concat(WritableColumns(schema).Select(column =>
                    $"request.{column.ClrPropertyName}"))
                .Concat(schema.EntityCapabilities.HasUpdatedAudit
                    ?
                    [
                        "UpdatedAtUtc = clock.UtcNow",
                        "UpdatedById = actorUserId",
                    ]
                    : [])
                .Concat(schema.HasVersion ? ["request.Version"] : []));

    private static string RenderExplicitDeleteValues(
        FullNetCrudSchema schema,
        string idParameter) =>
        string.Join(
            ",\n",
            new[] { $"Id = {idParameter}" }
                .Concat(schema.EntityCapabilities.HasDeletedAudit
                    ?
                    [
                        "DeletedAtUtc = clock.UtcNow",
                        "DeletedById = actorUserId",
                    ]
                    : [])
                .Concat(schema.HasVersion ? ["request.Version"] : []));

    private static string RenderCreateValues(
        FullNetCrudSchema schema,
        string idParameter) =>
        string.Join(
            ",\n",
            schema.Columns
                .Where(column => column.DatabaseName != "TenantId")
                .Select(column => column.DatabaseName switch
                {
                    "Id" => $"Id = {idParameter}",
                    "CreatedAtUtc" => "CreatedAtUtc = clock.UtcNow",
                    "Version" => $"Version = {InitialValue(column)}",
                    _ => $"request.{column.ClrPropertyName}",
                }));

    private static string RenderUpdateValues(
        FullNetCrudSchema schema,
        string idParameter) =>
        string.Join(
            ",\n",
            new[] { $"Id = {idParameter}" }
                .Concat(MutableColumns(schema).Select(column =>
                    $"request.{column.ClrPropertyName}"))
                .Concat(schema.HasVersion ? ["request.Version"] : []));

    private static string RenderDisableValues(
        FullNetCrudSchema schema,
        string idParameter) =>
        string.Join(
            ",\n",
            new[] { $"Id = {idParameter}" }
                .Concat(schema.HasVersion ? ["request.Version"] : []));

    private static string InitialValue(FullNetColumn column) =>
        column.ScalarType switch
        {
            FullNetScalarType.Int64 => "1L",
            FullNetScalarType.Int32 => "1",
            _ => throw new ArgumentException(
                "Version 字段必须使用整数标量类型。",
                nameof(column)),
        };

    private static string RenderRecordParameters(
        IEnumerable<FullNetColumn> columns) =>
        string.Join(
            ",\n",
            columns.Select(column =>
                $"    {CSharpType(column)} {column.ClrPropertyName}"));

    private static IEnumerable<FullNetColumn> MutableColumns(
        FullNetCrudSchema schema) =>
        schema.Columns.Where(column =>
            column.DatabaseName is not "Id"
            and not "TenantId"
            and not "Version"
            and not "CreatedAtUtc");

    private static IEnumerable<FullNetColumn> WritableColumns(
        FullNetCrudSchema schema) =>
        schema.Columns.Where(column =>
            column.DatabaseName is not "Id"
            and not "TenantId"
            and not "Version"
            and not "CreatedAtUtc"
            and not "CreatedById"
            and not "UpdatedAtUtc"
            and not "UpdatedById"
            and not "IsDeleted"
            and not "DeletedAtUtc"
            and not "DeletedById"
            and not "OrganizationUnitId");

    private static string CSharpType(FullNetColumn column)
    {
        var type = column.ScalarType switch
        {
            FullNetScalarType.Uuid => "Guid",
            FullNetScalarType.String => "string",
            FullNetScalarType.Int32 => "int",
            FullNetScalarType.Int64 => "long",
            FullNetScalarType.Boolean => "bool",
            FullNetScalarType.DateTimeUtc => "DateTimeOffset",
            FullNetScalarType.Decimal => "decimal",
            _ => throw new ArgumentOutOfRangeException(
                nameof(column),
                column.ScalarType,
                "不支持的 CRUD 标量类型。"),
        };
        return column.IsNullable ? $"{type}?" : type;
    }

    private static void EnsureRuntimeScope(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.DataScope == FullNetCrudDataScope.Unspecified)
        {
            throw new ArgumentException(
                "运行时功能骨架只接受作用域明确的 CRUD Schema。",
                nameof(schema));
        }
    }

    private static string UpperFirst(string value) =>
        string.Concat(char.ToUpperInvariant(value[0]), value[1..]);

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static string IndentLines(string content, int spaces)
    {
        var indentation = new string(' ', spaces);
        return string.Join(
            "\n",
            content.Split('\n').Select(line =>
                line.Length == 0 ? string.Empty : indentation + line));
    }

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }
}
