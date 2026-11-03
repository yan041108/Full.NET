using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>Files 保留策略只能接受真实就绪引用，不接受浏览器提供的单据或文件声明。</summary>
internal sealed class EnterpriseRequestAttachmentFileOwner(IQueryExecutor queries) : ITenantResourceFileOwner
{
    public string OwnerModuleKey => "enterprise_request_attachment";
    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var row = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestDeliveryRecord>(EnterpriseRequestDeliverySql.Find,
            EnterpriseRequestDeliverySql.Parameters(("Id", resourceId)), cancellationToken).ConfigureAwait(false);
        return row is { Phase: "Ready", Kind: "Attachment" } && row.FileId == fileId;
    }
}

/// <summary>报表资源独立于附件命名空间，不能通过相同幂等键读写另一种资源。</summary>
internal sealed class EnterpriseRequestReportFileOwner(IQueryExecutor queries) : ITenantResourceFileOwner
{
    public string OwnerModuleKey => "enterprise_request_report";
    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var row = await queries.QuerySingleOrDefaultAsync<EnterpriseRequestDeliveryRecord>(EnterpriseRequestDeliverySql.Find,
            EnterpriseRequestDeliverySql.Parameters(("Id", resourceId)), cancellationToken).ConfigureAwait(false);
        return row is { Phase: "Ready", Kind: "Report" } && row.FileId == fileId;
    }
}
