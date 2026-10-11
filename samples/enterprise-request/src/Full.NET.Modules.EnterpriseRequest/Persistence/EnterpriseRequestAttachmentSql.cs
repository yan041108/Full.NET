using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>申请拥有附件引用；Files 拥有实际对象。此处只访问本模块表。</summary>
internal static class EnterpriseRequestAttachmentSql
{
    internal static readonly SqlStatement List = new("enterprise_request.list_attachments", """
        SELECT Id, TenantId, RequestId, FileId, OriginalFileName, SizeBytes, CreatedAtUtc
        FROM demo_enterprise_request_request_attachment
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND StateKey = 'bound'
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Find = new("enterprise_request.find_attachment", """
        SELECT Id, TenantId, RequestId, FileId, OriginalFileName, SizeBytes, CreatedAtUtc
        FROM demo_enterprise_request_request_attachment
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND Id = @Id AND StateKey = 'bound'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Count = new("enterprise_request.count_attachments", """
        SELECT COUNT(*) FROM demo_enterprise_request_request_attachment
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND StateKey = 'bound'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Referenced = new("enterprise_request.attachment_referenced", """
        SELECT COUNT(*) FROM demo_enterprise_request_request_attachment a
        INNER JOIN demo_enterprise_request_enterprise_request r ON r.Id = a.RequestId AND r.TenantId = a.TenantId
        WHERE a.TenantId = @TenantId AND a.RequestId = @RequestId AND a.FileId = @FileId AND r.IsDeleted = 0
          AND (a.StateKey = 'bound' OR (a.StateKey = 'uploading' AND a.UploadExpiresAtUtc > @Now))
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement AdvanceParent = new("enterprise_request.attachment_advance_parent", """
        UPDATE demo_enterprise_request_enterprise_request
        SET Version = Version + 1, UpdatedAtUtc = @Now, UpdatedById = @ActorId
        WHERE TenantId = @TenantId AND Id = @RequestId AND OrganizationUnitId = @OrganizationUnitId
          AND IsDeleted = 0 AND Status = @ExpectedStatus AND Version = @Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Insert = new("enterprise_request.attachment_insert", """
        INSERT INTO demo_enterprise_request_request_attachment
            (Id, TenantId, RequestId, FileId, OriginalFileName, SizeBytes, CreatedAtUtc, CreatedById, StateKey, UploadExpiresAtUtc)
        VALUES (@Id, @TenantId, @RequestId, @FileId, @OriginalFileName, @SizeBytes, @Now, @ActorId, 'uploading', @ExpiresAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindUpload = new("enterprise_request.attachment_find_upload", """
        SELECT Id, TenantId, RequestId, FileId, OriginalFileName, SizeBytes, CreatedAtUtc
        FROM demo_enterprise_request_request_attachment
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND FileId = @FileId AND StateKey = 'uploading'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Bind = new("enterprise_request.attachment_bind", """
        UPDATE demo_enterprise_request_request_attachment SET StateKey = 'bound'
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND FileId = @FileId AND Id = @Id AND StateKey = 'uploading'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Expire = new("enterprise_request.attachment_expire_upload", """
        UPDATE demo_enterprise_request_request_attachment SET StateKey = 'expired'
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND FileId = @FileId AND StateKey = 'uploading' AND UploadExpiresAtUtc <= @Now
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Delete = new("enterprise_request.attachment_delete", """
        DELETE FROM demo_enterprise_request_request_attachment
        WHERE TenantId = @TenantId AND RequestId = @RequestId AND Id = @Id AND FileId = @FileId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>读取时保留租户和资源身份，不能仅凭客户端附件标识打开文件。</summary>
internal sealed record EnterpriseRequestAttachmentRow(Guid Id, Guid TenantId, Guid RequestId, Guid FileId,
    string OriginalFileName, long SizeBytes, DateTimeOffset CreatedAtUtc);
