using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.HostFileReferences;

/// <summary>阻止 Files 在用户资料仍引用头像或签名时物理删除对应文件。</summary>
internal sealed class IdentityHostFileRetentionContributor(IQueryExecutor queryExecutor)
    : IHostFileRetentionContributor
{
    public async Task<bool> IsFileReferencedAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var referenced = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                IdentitySql.IsHostUserProfileFileReferenced,
                IdentitySqlParameters.Create(("FileId", fileId)),
                cancellationToken)
            .ConfigureAwait(false);
        return referenced == 1;
    }
}
