using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageHostConfigEntries;

/// <summary>Host 系统配置项分页列表与详情只读查询。</summary>
internal sealed class HostConfigEntryQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<ConfigEntryResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                ConfigEntrySql.CountHostConfigEntries,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ConfigEntrySql.ListHostConfigEntriesSqlServer,
            DatabaseProvider.MySql => ConfigEntrySql.ListHostConfigEntriesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<ConfigEntryRecord>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<ConfigEntryResponse>>.Success(
            new PagedResult<ConfigEntryResponse>(items, page, pageSize, total));
    }

    public async Task<Result<ConfigEntryResponse>> GetByIdAsync(
        Guid configEntryId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryRecord>(
                ConfigEntrySql.FindById,
                new { ConfigEntryId = configEntryId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<ConfigEntryResponse>.Success(Map(record));
    }

    public async Task<Result<ConfigEntryResponse>> GetByKeyAsync(
        string configKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = configKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedKey.Length == 0)
        {
            return NotFound();
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryRecord>(
                ConfigEntrySql.FindByKey,
                new { ConfigKey = normalizedKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<ConfigEntryResponse>.Success(Map(record));
    }

    internal static ConfigEntryResponse Map(ConfigEntryRecord record) =>
        new(
            record.Id,
            record.ConfigKey,
            record.DisplayName,
            record.Description,
            record.ValueKind,
            record.Value,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<ConfigEntryResponse> NotFound() =>
        Result<ConfigEntryResponse>.Failure(new Error(
            SettingsErrorCodes.ConfigEntryNotFound,
            "The system configuration entry was not found.",
            ErrorType.NotFound));
}

internal sealed record ConfigEntryRecord(
    Guid Id,
    string ConfigKey,
    string DisplayName,
    string? Description,
    string ValueKind,
    string Value,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>写入路径使用的轻量身份投影，避免把时间戳列误绑到 Dapper 参数。</summary>
internal sealed record ConfigEntryIdentityRecord(
    Guid Id,
    string ConfigKey,
    string DisplayName,
    string? Description,
    string ValueKind,
    string Value,
    int DisplayOrder,
    bool IsActive,
    int Version);
