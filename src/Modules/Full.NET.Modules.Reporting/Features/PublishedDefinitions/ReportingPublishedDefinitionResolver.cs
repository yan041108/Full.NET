using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.PublishedDefinitions;

/// <summary>逐版本目录复用统一执行目录的发布权威，不读取草稿或复制连接信息。</summary>
internal sealed class ReportingPublishedDefinitionResolver(ReportingExecutionCatalog catalog,
    IQueryExecutor queryExecutor, ICurrentTenant tenant)
{
    public async Task<Result<ReportingPublishedDefinition>> ResolveAsync(Guid definitionId, int? versionNumber, CancellationToken cancellationToken)
    {
        var definition = await catalog.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
        if (!definition.IsSuccess) return Result<ReportingPublishedDefinition>.Failure(definition.Error!);
        if (!definition.Value!.IsEnabled) return Result<ReportingPublishedDefinition>.Failure(new(CommonErrorCodes.PermissionDenied,
            "The published definition is disabled.", ErrorType.Forbidden));
        var version = await catalog.GetVersionAsync(definitionId, versionNumber ?? definition.Value!.LatestPublishedVersionNumber, cancellationToken).ConfigureAwait(false);
        return version.IsSuccess ? Result<ReportingPublishedDefinition>.Success(new(definition.Value!, version.Value!))
            : Result<ReportingPublishedDefinition>.Failure(version.Error!);
    }

    public Task<ReportingDataSourceRecord?> FindDataSourceAsync(ReportingPublishedDefinition definition, CancellationToken cancellationToken) =>
        catalog.GetDataSourceAsync(definition.Definition.Id, definition.Version.VersionNumber, definition.Version.DataSourceId, cancellationToken);

    public async Task<Result<IReadOnlyList<ReportingPublishedVersionResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!tenant.IsAvailable) return Result<IReadOnlyList<ReportingPublishedVersionResponse>>.Failure(new(CommonErrorCodes.PermissionDenied,
            "A trusted reporting scope is required.", ErrorType.Forbidden));
        var rows = await queryExecutor.QueryAsync<ReportingPublishedDefinitionRecord>(tenant.IsHost ? ReportingTenantGrantSql.ListPublishedHost
            : ReportingTenantGrantSql.ListPublished, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ReportingPublishedVersionResponse>>.Success(rows.Select(row => new ReportingPublishedVersionResponse(
            row.DefinitionId, row.DefinitionKey, row.Name, row.VersionNumber, row.QueryPortKey,
            Domain.ReportingDefinitionJson.DeserializeParameterSchema(row.ParameterSchemaJson), row.LayoutConfigJson)).ToArray());
    }
}

/// <summary>仅供模块执行内部使用的不可变发布快照。</summary>
internal sealed record ReportingPublishedDefinition(ReportingDefinitionResponse Definition, ReportingDefinitionVersionResponse Version);
