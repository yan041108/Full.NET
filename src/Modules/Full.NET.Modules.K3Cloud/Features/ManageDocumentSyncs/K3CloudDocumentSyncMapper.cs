using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.K3Cloud.Persistence;

namespace Full.NET.Modules.K3Cloud.Features.ManageDocumentSyncs;

/// <summary>K3Cloud 单据同步 DTO 映射。</summary>
internal static class K3CloudDocumentSyncMapper
{
    public static K3CloudDocumentSyncResponse Map(K3CloudDocumentSyncRecord record) =>
        new(
            record.Id,
            record.ConnectionConfigId,
            record.DocumentTypeKey,
            record.BusinessKey,
            record.StatusKey,
            record.LastStepKey,
            record.ExternalBillId,
            record.ExternalBillNo,
            record.LastErrorCode,
            record.LastErrorMessage,
            record.SubmittedAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.CreatedByUserId,
            record.Version);
}
