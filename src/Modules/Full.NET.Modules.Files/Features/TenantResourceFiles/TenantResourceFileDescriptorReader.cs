using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;

namespace Full.NET.Modules.Files.Features.TenantResourceFiles;

/// <summary>精确资源描述读取保持租户作用域，禁止嵌套外部模块事务或回退 Host 文件。</summary>
internal sealed class TenantResourceFileDescriptorReader(IQueryExecutor query, ICurrentTenant tenant,
    IDataTransactionState transaction) : ITenantResourceFileDescriptorReader
{
    public async Task<TenantResourceFileDescriptor?> GetReadyDescriptorAsync(string ownerModuleKey, Guid resourceId,
        Guid fileId, CancellationToken cancellationToken = default)
    {
        if (transaction.HasTransaction) throw new InvalidOperationException("Resource file reads cannot run inside another module transaction.");
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is not { } tenantId)
            throw new TenantContextMissingException("files.tenant_context_required");
        if (string.IsNullOrWhiteSpace(ownerModuleKey) || resourceId == Guid.Empty || fileId == Guid.Empty) return null;
        var row = await query.QuerySingleOrDefaultAsync<TenantResourceFileDescriptorRecord>(TenantResourceFileSql.FindReadyDescriptor,
            new Dictionary<string, object?> { ["TenantId"] = tenantId, ["OwnerModuleKey"] = ownerModuleKey,
                ["ResourceId"] = resourceId, ["Id"] = fileId }, cancellationToken).ConfigureAwait(false);
        return row is null ? null : new(row.Id, row.OriginalFileName, row.ContentType, row.SizeBytes, row.ContentHash, row.CreatedByUserId);
    }
}
