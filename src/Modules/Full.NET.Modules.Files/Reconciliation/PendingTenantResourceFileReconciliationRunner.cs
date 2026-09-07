using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>租户资源文件对账结果。</summary>
/// <param name="Scanned">扫描条数。</param>
/// <param name="Promoted">pending 提升为 ready 的条数。</param>
/// <param name="Purged">缺失对象的 pending 删除条数。</param>
/// <param name="Released">无主 ready 释放条数。</param>
/// <param name="Skipped">因租户停用或探测失败而跳过的条数。</param>
/// <param name="BatchesExecuted">批次数。</param>
internal sealed record PendingTenantResourceFileReconciliationResult(
    int Scanned,
    int Promoted,
    int Purged,
    int Released,
    int Skipped,
    int BatchesExecuted)
{
    /// <summary>空结果。</summary>
    public static PendingTenantResourceFileReconciliationResult Empty { get; } =
        new(0, 0, 0, 0, 0, 0);
}

/// <summary>对账陈旧 pending/ready 租户资源文件；pending 按对象存在性提升或删除，ready 仅在所属模块确认不再引用后释放。</summary>
/// <param name="queryExecutor">查询执行器。</param>
/// <param name="commandExecutor">命令执行器。</param>
/// <param name="storageProviders">对象存储。</param>
/// <param name="resourceOwners">所属模块引用确认端口。</param>
/// <param name="tenantResolver">活动租户解析。</param>
/// <param name="currentTenant">租户上下文写入器。</param>
/// <param name="databaseOptions">数据库提供程序。</param>
/// <param name="clock">时钟。</param>
/// <param name="cursor">Worker 跨轮共享游标；直接调用未提供时仅在本实例内保留。</param>
internal sealed class PendingTenantResourceFileReconciliationRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    FileStorageProviderRegistry storageProviders,
    IEnumerable<ITenantResourceFileOwner> resourceOwners,
    IActiveTenantContextResolver tenantResolver,
    ICurrentTenantContextWriter currentTenant,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    TenantResourceFileReconciliationCursor? cursor = null)
{
    private readonly TenantResourceFileReconciliationCursor _cursor = cursor ?? new();
    private readonly DatabaseProvider _provider = databaseOptions.Value.Provider;

    /// <summary>扫描一批陈旧文件并按状态推进或回收。</summary>
    /// <param name="options">对账选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PendingTenantResourceFileReconciliationResult> RunOnceAsync(
        PendingTenantResourceFileReconciliationOptions options,
        CancellationToken cancellationToken)
    {
        await _cursor.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCoreAsync(options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cursor.Gate.Release();
        }
    }

    private async Task<PendingTenantResourceFileReconciliationResult> RunCoreAsync(
        PendingTenantResourceFileReconciliationOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return PendingTenantResourceFileReconciliationResult.Empty;
        }

        var createdBeforeUtc = clock.UtcNow.AddSeconds(-options.MinimumAgeSeconds);
        var scanned = 0;
        var promoted = 0;
        var purged = 0;
        var released = 0;
        var skipped = 0;
        var batches = 0;
        var hasCursor = _cursor.Id is not null;
        var afterCreatedAtUtc = _cursor.CreatedAtUtc;
        Guid? afterId = _cursor.Id;

        while (batches < options.MaxBatchesPerRun)
        {
            var records = await queryExecutor.QueryAsync<TenantResourceFileReconciliationRecord>(
                    SelectStatement(),
                    new Dictionary<string, object?>
                    {
                        ["CreatedBeforeUtc"] = createdBeforeUtc,
                        ["HasCursor"] = hasCursor ? 1 : 0,
                        ["AfterCreatedAtUtc"] = afterCreatedAtUtc,
                        ["AfterId"] = afterId,
                        ["BatchSize"] = options.BatchSize,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            batches++;
            if (records.Count == 0)
            {
                _cursor.Reset();
                break;
            }

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                if (!await TrySetTenantAsync(record.TenantId, cancellationToken).ConfigureAwait(false))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    if (string.Equals(record.StatusKey, "pending", StringComparison.Ordinal))
                    {
                        var pendingOutcome = await ReconcilePendingAsync(record, cancellationToken)
                            .ConfigureAwait(false);
                        if (pendingOutcome == PendingOutcome.Promoted)
                        {
                            promoted++;
                        }
                        else if (pendingOutcome == PendingOutcome.Purged)
                        {
                            purged++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    else if (record.StatusKey == "released"
                        || await ShouldReleaseReadyAsync(record, cancellationToken).ConfigureAwait(false))
                    {
                        var parameters = OwnedParameters(record);
                        var current = await queryExecutor
                            .QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
                                TenantResourceFileSql.FindOwned,
                                parameters,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await commandExecutor.ExecuteAsync(
                                TenantResourceFileSql.Release,
                                parameters,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (current is not null)
                        {
                            await storageProviders.Resolve(current.ProviderKey)
                                .DeleteAsync(current.StorageKey, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        // 只有对象删除成功才移除墓碑；异常/崩溃后 released 仍会被后续扫描重试。
                        await commandExecutor.ExecuteAsync(TenantResourceFileSql.PurgeReleased,
                            parameters, cancellationToken).ConfigureAwait(false);
                        released++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // 单个存储或所属模块故障不能阻塞后续文件；状态保留到下一次环扫重试。
                    skipped++;
                }
                finally
                {
                    currentTenant.Clear();
                }
            }

            var last = records[^1];
            hasCursor = true;
            afterCreatedAtUtc = last.CreatedAtUtc;
            afterId = last.Id;
            _cursor.CreatedAtUtc = afterCreatedAtUtc;
            _cursor.Id = afterId;
            if (records.Count < options.BatchSize)
            {
                _cursor.Reset();
                break;
            }
        }

        return new PendingTenantResourceFileReconciliationResult(
            scanned, promoted, purged, released, skipped, batches);
    }

    /// <summary>pending：对象存在则提升，不存在则删除元数据；探测失败必须保留。</summary>
    /// <param name="record">扫描行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<PendingOutcome> ReconcilePendingAsync(
        TenantResourceFileReconciliationRecord record,
        CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            exists = await storageProviders.Resolve(record.ProviderKey)
                .ExistsAsync(record.StorageKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return PendingOutcome.Skipped;
        }

        var statement = exists ? TenantResourceFileSql.PromotePending : TenantResourceFileSql.PurgePending;
        var affected = await commandExecutor.ExecuteAsync(
                statement,
                OwnedParameters(record),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return PendingOutcome.Skipped;
        }

        return exists ? PendingOutcome.Promoted : PendingOutcome.Purged;
    }

    /// <summary>ready 只有所属模块确认当前租户资源不再引用时才能释放。</summary>
    /// <param name="record">扫描行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<bool> ShouldReleaseReadyAsync(
        TenantResourceFileReconciliationRecord record,
        CancellationToken cancellationToken)
    {
        var owner = resourceOwners.SingleOrDefault(item => item.OwnerModuleKey == record.OwnerModuleKey);
        if (owner is null)
        {
            return false;
        }

        return !await owner.IsReferencedAsync(record.ResourceId, record.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>对账写入必须进入文件所属的活动租户。</summary>
    /// <param name="tenantId">文件租户。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<bool> TrySetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return false;
        }

        currentTenant.SetTenant(tenant);
        return true;
    }

    /// <summary>固定所有权参数，不接受调用方覆盖租户。</summary>
    /// <param name="record">扫描行。</param>
    private static Dictionary<string, object?> OwnedParameters(TenantResourceFileReconciliationRecord record) =>
        new(StringComparer.Ordinal)
        {
            ["Id"] = record.Id,
            ["OwnerModuleKey"] = record.OwnerModuleKey,
            ["ResourceId"] = record.ResourceId,
        };

    private SqlStatement SelectStatement() =>
        _provider switch
        {
            DatabaseProvider.SqlServer => TenantResourceFileSql.SelectStaleSqlServer,
            DatabaseProvider.MySql => TenantResourceFileSql.SelectStaleMySql,
            _ => throw new NotSupportedException($"Unsupported database provider '{_provider}'."),
        };

    private enum PendingOutcome
    {
        Promoted,
        Purged,
        Skipped,
    }
}
