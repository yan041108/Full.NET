using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Domain;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.PublishedDefinitions;

/// <summary>Host 执行原有发布版本；租户执行必须逐版本获授，不切换租户上下文。</summary>
internal sealed class ReportingPublishedDefinitionResolver(
    IQueryExecutor queryExecutor, ReportingDefinitionQueryService definitions,
    ICurrentTenant tenant, IOptions<DatabaseOptions> database)
{
    /// <summary>默认使用最近获授版本，不能回退到未获授的全局最新版本。</summary>
    public async Task<Result<ReportingPublishedDefinition>> ResolveAsync(Guid definitionId, int? versionNumber,
        CancellationToken cancellationToken)
    {
        if (!tenant.IsAvailable) return Denied();
        if (tenant.IsHost)
        {
            var definition = await definitions.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
            if (!definition.IsSuccess) return Result<ReportingPublishedDefinition>.Failure(definition.Error!);
            if (definition.Value?.IsEnabled != true) return Result<ReportingPublishedDefinition>.Failure(new(
                ReportingErrorCodes.ExecutionParametersInvalid, "The reporting definition is disabled.", ErrorType.Validation));
            var selected = versionNumber ?? definition.Value.LatestPublishedVersionNumber;
            if (selected <= 0) return Result<ReportingPublishedDefinition>.Failure(new(
                ReportingErrorCodes.DefinitionNotPublished, "The reporting definition has no published version.", ErrorType.Validation));
            var version = await definitions.GetVersionAsync(definitionId,
                selected, cancellationToken).ConfigureAwait(false);
            return !version.IsSuccess || version.Value is null ? Result<ReportingPublishedDefinition>.Failure(version.Error!)
                : Result<ReportingPublishedDefinition>.Success(new(definition.Value, version.Value));
        }
        var parameters = ReportingSqlParameters.Create(("DefinitionId", definitionId), ("VersionNumber", versionNumber));
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(
            ReportingTenantGrantSql.ResolveVersion(database.Value.Provider), parameters, cancellationToken).ConfigureAwait(false);
        if (row is null) return Denied();
        var definitionRow = await queryExecutor.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
            ReportingTenantGrantSql.FindDefinition,
            ReportingSqlParameters.Create(("DefinitionId", definitionId), ("VersionNumber", row.VersionNumber)), cancellationToken).ConfigureAwait(false);
        return definitionRow is null || !definitionRow.IsEnabled ? Denied()
            : Result<ReportingPublishedDefinition>.Success(new(ReportingDefinitionMapper.MapDefinition(definitionRow), ReportingDefinitionMapper.MapVersion(row)));
    }

    /// <summary>外部连接凭据只在执行内部使用，且读取时再次确认版本授权。</summary>
    public Task<ReportingDataSourceRecord?> FindDataSourceAsync(ReportingPublishedDefinition definition, CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(
            tenant.IsHost ? ReportingDataSourceSql.FindById : ReportingTenantGrantSql.FindDataSource,
            ReportingSqlParameters.Create(("DataSourceId", definition.Version.DataSourceId),
                ("DefinitionId", definition.Definition.Id), ("VersionNumber", definition.Version.VersionNumber)), cancellationToken);

    /// <summary>一次查询列出全部获授版本，不读取草稿参数或数据源连接信息。</summary>
    public async Task<Result<IReadOnlyList<ReportingPublishedDefinitionResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!tenant.IsAvailable) return Result<IReadOnlyList<ReportingPublishedDefinitionResponse>>.Failure(DeniedError());
        var rows = await queryExecutor.QueryAsync<ReportingPublishedDefinitionRecord>(tenant.IsHost ? ReportingTenantGrantSql.ListPublishedHost : ReportingTenantGrantSql.ListPublished,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ReportingPublishedDefinitionResponse>>.Success(rows.Select(row =>
            new ReportingPublishedDefinitionResponse(row.DefinitionId, row.DefinitionKey, row.Name, row.VersionNumber,
                row.QueryPortKey, ReportingDefinitionJson.DeserializeParameterSchema(row.ParameterSchemaJson), row.LayoutConfigJson)).ToArray());
    }

    private static Result<ReportingPublishedDefinition> Denied() => Result<ReportingPublishedDefinition>.Failure(DeniedError());
    private static Error DeniedError() => new(CommonErrorCodes.PermissionDenied,
        "The published reporting version is unavailable in the current scope.", ErrorType.Forbidden);
}

/// <summary>本模块内部使用的发布配置，不作为租户 HTTP 响应。</summary>
internal sealed record ReportingPublishedDefinition(ReportingDefinitionResponse Definition, ReportingDefinitionVersionResponse Version);
