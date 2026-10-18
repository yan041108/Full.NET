using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.EnterpriseRequest.Features.ManageAttachments;

/// <summary>后台仅保留活动申请实际引用的精确文件；租户由对账基础设施设置。</summary>
internal sealed class EnterpriseRequestResourceFileOwner(IQueryExecutor queries, ICommandExecutor commands, IClock clock, IIdGenerator ids) : ITenantResourceFileUploadOwner
{
    public string OwnerModuleKey => "enterprise_request";
    public async Task BeginUploadAsync(TenantResourceFileUploadIntent intent, CancellationToken cancellationToken = default)
    {
        if (await commands.ExecuteAsync(EnterpriseRequestAttachmentSql.Insert, new Dictionary<string, object?> {
            ["Id"] = ids.NewId(), ["RequestId"] = intent.ResourceId, ["FileId"] = intent.FileId,
            ["OriginalFileName"] = intent.OriginalFileName, ["SizeBytes"] = intent.SizeBytes,
            ["Now"] = intent.CreatedAtUtc, ["ActorId"] = intent.ActorUserId, ["ExpiresAtUtc"] = clock.UtcNow.AddHours(1) }, cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The upload owner intent must be persisted before storage access.");
    }
    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        if (resourceId == Guid.Empty || fileId == Guid.Empty) return false;
        var parameters = new Dictionary<string, object?> { ["RequestId"] = resourceId, ["FileId"] = fileId, ["Now"] = clock.UtcNow };
        // 过期探测先 CAS 撤销 uploading；若绑定事务先获锁提交，此更新为零，后续读仍确认 bound。
        await commands.ExecuteAsync(EnterpriseRequestAttachmentSql.Expire, parameters, cancellationToken).ConfigureAwait(false);
        return await queries.QuerySingleOrDefaultAsync<int>(EnterpriseRequestAttachmentSql.Referenced, parameters, cancellationToken).ConfigureAwait(false) == 1;
    }
}
