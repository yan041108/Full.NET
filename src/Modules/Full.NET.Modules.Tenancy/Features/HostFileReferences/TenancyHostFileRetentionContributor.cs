using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.HostFileReferences;

/// <summary>阻止 Files 在租户仍引用 Logo 时物理删除对应文件。</summary>
internal sealed class TenancyHostFileRetentionContributor(IQueryExecutor queryExecutor)
    : IHostFileRetentionContributor
{
    public async Task<bool> IsFileReferencedAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var referenced = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                TenantSql.IsTenantLogoReferenced,
                TenancySqlParameters.Create(("FileId", fileId)),
                cancellationToken)
            .ConfigureAwait(false);
        return referenced == 1;
    }
}
