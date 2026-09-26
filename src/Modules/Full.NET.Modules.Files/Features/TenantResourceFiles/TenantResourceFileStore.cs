using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Files.Features;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Features.TenantResourceFiles;

/// <summary>租户资源的显式文件所有权边界；独立持久化上传状态，不借用 Host 权限。</summary>
/// <param name="queries">受租户守卫保护的读执行器。</param>
/// <param name="commands">受租户守卫保护的写执行器。</param>
/// <param name="tenant">当前可信租户。</param>
/// <param name="transactionState">外部操作前检查事务边界。</param>
/// <param name="providers">受信存储路由。</param>
/// <param name="resourceOwners">注册模块的持久化引用确认端口。</param>
/// <param name="clock">时钟。</param>
/// <param name="ids">UUID 生成器。</param>
/// <param name="options">上传预算。</param>
/// <param name="storageQuotaPort">租户文件存储配额端口；由 Tenancy 在 SaaS 组合中替换默认空实现。</param>
/// <param name="activeTenants">活动租户目录；暂停租户拒绝上传。</param>
internal sealed class TenantResourceFileStore(IQueryExecutor queries, ICommandExecutor commands,
    ICurrentTenantContextWriter tenant, IDataTransactionState transactionState, FileStorageProviderRegistry providers,
    IEnumerable<ITenantResourceFileOwner> resourceOwners, IClock clock, IIdGenerator ids,
    IOptions<LocalFileStorageOptions> options, ITenantFileStorageQuotaPort storageQuotaPort,
    IIdentityActiveTenantDirectory activeTenants) : ITenantResourceFileStore
{
    /// <inheritdoc />
    public async Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId,
        Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireScope(ownerModuleKey, resourceId);
        RequireNoTransaction();
        if (!await activeTenants.IsActiveTenantAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return Failure<TenantResourceFileReference>(FilesErrorCodes.TenantInactive, ErrorType.Validation);
        }

        var fileName = Path.GetFileName((originalFileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (fileName.Length is < 1 or > 255 || fileName.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || contentType.Any(char.IsControl)
            || actorUserId == Guid.Empty || contentLength <= 0)
        {
            return Failure<TenantResourceFileReference>(FilesErrorCodes.InvalidUpload, ErrorType.Validation);
        }

        var limit = options.Value.MaxUploadBytes;
        if (contentLength > limit)
        {
            return Failure<TenantResourceFileReference>(FilesErrorCodes.FileTooLarge, ErrorType.Validation);
        }

        // 实际流长度不受调用方声明信任；每次写入前检查，避免先完整缓冲再拒绝。
        using var buffered = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffered.Length > limit - read)
            {
                return Failure<TenantResourceFileReference>(FilesErrorCodes.FileTooLarge, ErrorType.Validation);
            }
            buffered.Write(buffer, 0, read);
        }
        if (buffered.Length == 0)
        {
            return Failure<TenantResourceFileReference>(FilesErrorCodes.InvalidUpload, ErrorType.Validation);
        }

        buffered.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(buffered, cancellationToken).ConfigureAwait(false));
        buffered.Position = 0;
        var fileId = ids.NewId();
        var operationId = fileId.ToString("N");
        var reserveResult = await FilesHostExecutionScope.RunAsync(
                tenant,
                () => storageQuotaPort.TryReserveAsync(
                    tenantId,
                    operationId,
                    buffered.Length,
                    cancellationToken))
            .ConfigureAwait(false);
        if (!reserveResult.IsSuccess)
        {
            return MapQuotaFailure<TenantResourceFileReference>(reserveResult.Error!);
        }

        var provider = providers.DefaultProvider;
        var storageKey = $"tenant-resources/{tenantId:N}/{fileId:N}";
        var parameters = Parameters(ownerModuleKey, resourceId, fileId);
        parameters["OriginalFileName"] = fileName;
        parameters["ContentType"] = contentType.Trim();
        parameters["SizeBytes"] = buffered.Length;
        parameters["ContentHash"] = digest;
        parameters["ProviderKey"] = provider.ProviderKey;
        parameters["StorageKey"] = storageKey;
        parameters["CreatedByUserId"] = actorUserId;
        parameters["CreatedAtUtc"] = clock.UtcNow;
        if (await commands.ExecuteAsync(TenantResourceFileSql.Insert, parameters, cancellationToken).ConfigureAwait(false) != 1)
        {
            await ReleaseQuotaReservationAsync(tenantId, operationId, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Tenant resource upload intent was not persisted.");
        }

        try
        {
            await provider.SaveAsync(storageKey, buffered, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseQuotaReservationAsync(tenantId, operationId, cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (await commands.ExecuteAsync(TenantResourceFileSql.MarkReady,
            Parameters(ownerModuleKey, resourceId, fileId), cancellationToken).ConfigureAwait(false) != 1)
        {
            await ReleaseQuotaReservationAsync(tenantId, operationId, cancellationToken).ConfigureAwait(false);
            var current = await queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned,
                Parameters(ownerModuleKey, resourceId, fileId), cancellationToken).ConfigureAwait(false);
            if (current?.StatusKey == "released")
            {
                await provider.DeleteAsync(storageKey, cancellationToken).ConfigureAwait(false);
            }

            return Failure<TenantResourceFileReference>(FilesErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        var confirmResult = await FilesHostExecutionScope.RunAsync(
                tenant,
                () => storageQuotaPort.ConfirmAsync(tenantId, operationId, cancellationToken))
            .ConfigureAwait(false);
        if (!confirmResult.IsSuccess)
        {
            await ReleaseQuotaReservationAsync(tenantId, operationId, cancellationToken).ConfigureAwait(false);
            return MapQuotaFailure<TenantResourceFileReference>(confirmResult.Error!);
        }

        return Result<TenantResourceFileReference>.Success(new(fileId, buffered.Length, digest));
    }

    private async Task ReleaseQuotaReservationAsync(
        Guid tenantId,
        string operationId,
        CancellationToken cancellationToken)
    {
        await FilesHostExecutionScope.RunAsync(
                tenant,
                () => storageQuotaPort.ReleaseAsync(tenantId, operationId, cancellationToken))
            .ConfigureAwait(false);
    }

    private static Result<T> MapQuotaFailure<T>(Error error) =>
        error.Code.StartsWith("tenancy.", StringComparison.Ordinal)
            ? Failure<T>(FilesErrorCodes.StorageQuotaExceeded, ErrorType.Conflict)
            : Result<T>.Failure(error);

    /// <inheritdoc />
    public async Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey,
        Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        RequireScope(ownerModuleKey, resourceId);
        RequireNoTransaction();
        var record = await queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned,
            Parameters(ownerModuleKey, resourceId, fileId), cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            // 兼容旧任务：授权凭据来自所属模块对当前租户持久化资源的查询，绝不临时切换 Host。
            var owner = resourceOwners.SingleOrDefault(item => item.OwnerModuleKey == ownerModuleKey);
            if (owner is not null && await owner.IsReferencedAsync(resourceId, fileId, cancellationToken).ConfigureAwait(false))
            {
                record = await queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
                    TenantResourceFileSql.FindAuthorizedLegacyReference,
                    new Dictionary<string, object?> { ["Id"] = fileId }, cancellationToken).ConfigureAwait(false);
            }
        }
        if (record is null || record.StatusKey != "ready")
        {
            return Failure<TenantResourceFileContent>(FilesErrorCodes.FileNotFound, ErrorType.NotFound);
        }
        var stream = await providers.Resolve(record.ProviderKey).OpenReadAsync(record.StorageKey, cancellationToken)
            .ConfigureAwait(false);
        return Result<TenantResourceFileContent>.Success(new(stream, record.ContentType, record.OriginalFileName));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey,
        Guid resourceId, CancellationToken cancellationToken = default)
    {
        RequireScope(ownerModuleKey, resourceId);
        var rows = await queries.QueryAsync<TenantResourceFileReadyRecord>(
                TenantResourceFileSql.ListReady,
                new Dictionary<string, object?> { ["OwnerModuleKey"] = ownerModuleKey, ["ResourceId"] = resourceId },
                cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => new TenantResourceFileReadyItem(row.Id, row.OriginalFileName, row.CreatedAtUtc))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
        CancellationToken cancellationToken = default)
    {
        RequireScope(ownerModuleKey, resourceId);
        RequireNoTransaction();
        var parameters = Parameters(ownerModuleKey, resourceId, fileId);
        var record = await queries.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(TenantResourceFileSql.FindOwned,
            parameters, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }
        await commands.ExecuteAsync(TenantResourceFileSql.Release, parameters, cancellationToken).ConfigureAwait(false);
        await providers.Resolve(record.ProviderKey).DeleteAsync(record.StorageKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>调用方只能提供所属资源键，租户必须来自当前已验证上下文。</summary>
    /// <param name="ownerModuleKey">所属模块键。</param>
    /// <param name="resourceId">所属资源。</param>
    private Guid RequireScope(string ownerModuleKey, Guid resourceId)
    {
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is not { } tenantId)
        {
            throw new TenantContextMissingException("files.tenant_resource_file");
        }
        if (resourceId == Guid.Empty || string.IsNullOrWhiteSpace(ownerModuleKey) || ownerModuleKey.Length > 64
            || !ownerModuleKey.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
        {
            throw new ArgumentException("A stable resource owner is required.");
        }
        return tenantId;
    }

    /// <summary>禁止文件 I/O 借用调用模块的本地事务，保证已保存意图独立可恢复。</summary>
    private void RequireNoTransaction()
    {
        if (transactionState.HasTransaction)
        {
            throw new InvalidOperationException("Tenant file operations require an independent transaction boundary.");
        }
    }

    /// <summary>固定查询所有权参数，不接受调用方指定 TenantId。</summary>
    /// <param name="ownerModuleKey">所属模块。</param>
    /// <param name="resourceId">所属资源。</param>
    /// <param name="fileId">文件标识。</param>
    private static Dictionary<string, object?> Parameters(string ownerModuleKey, Guid resourceId, Guid fileId) =>
        new(StringComparer.Ordinal) { ["Id"] = fileId, ["OwnerModuleKey"] = ownerModuleKey, ["ResourceId"] = resourceId };

    /// <summary>返回既有 Files 稳定错误码，不泄露其他所有者的文件存在性。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="code">稳定错误码。</param>
    /// <param name="type">错误类别。</param>
    private static Result<T> Failure<T>(string code, ErrorType type) =>
        Result<T>.Failure(new Error(code, "The tenant resource file operation could not be completed.", type));
}
