using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>应用拥有的附件及报表恢复日记；所有业务访问显式绑定当前可信租户。</summary>
internal static class EnterpriseRequestDeliverySql
{
    internal const string Columns = "Id,TenantId,RequestId,RequestKey,Kind,RequestedVersion,ActorUserId,SessionId,SecurityStamp,ActorScope,EffectiveScope,SessionKind,SourceDigest,IncludesAmounts,LocaleCode,FileId,FileName,ContentType,SizeBytes,ContentHash,Phase,ErrorCode,LeaseId,LeaseUntilUtc,Attempt,DueAtUtc,ExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc,Version";
    private const string Work = "(Phase IN ('Uploading','Queued','Running','Deleting','FailedCleanup','CancelledCleanup','ExpiredCleanup','DeletedCleanup') OR (Phase='Ready' AND ((Kind='Report' AND ExpiresAtUtc<=@Now) OR NOT EXISTS (SELECT 1 FROM demo_enterprise_request_enterprise_request parent WHERE parent.Id=demo_enterprise_request_delivery.RequestId AND parent.TenantId=demo_enterprise_request_delivery.TenantId AND parent.IsDeleted=0))))";
    internal static readonly SqlStatement Find = new("enterprise_request.delivery.find",
        $"SELECT {Columns} FROM demo_enterprise_request_delivery WHERE Id=@Id AND TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindKey = new("enterprise_request.delivery.find_key",
        $"SELECT {Columns} FROM demo_enterprise_request_delivery WHERE TenantId=@TenantId AND RequestId=@RequestId AND RequestKey=@RequestKey AND Kind=@Kind AND ActorUserId=@ActorUserId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Visible = "TenantId=@TenantId AND RequestId=@RequestId AND Kind=@Kind AND Phase<>'Deleted' AND (ActorUserId=@ActorUserId OR (Kind='Attachment' AND Phase='Ready'))";
    internal static readonly SqlStatement ListRequestSqlServer = new("enterprise_request.delivery.list_request.sql_server",
        $"SELECT {Columns} FROM demo_enterprise_request_delivery WHERE {Visible} ORDER BY CreatedAtUtc DESC,Id DESC OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListRequestMySql = new("enterprise_request.delivery.list_request.my_sql",
        $"SELECT {Columns} FROM demo_enterprise_request_delivery WHERE {Visible} ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountRequest = new("enterprise_request.delivery.count_request",
        $"SELECT COUNT(1) FROM demo_enterprise_request_delivery WHERE {Visible}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountAttachments = new("enterprise_request.delivery.count_attachments",
        "SELECT COUNT(1) FROM demo_enterprise_request_delivery WHERE TenantId=@TenantId AND RequestId=@RequestId AND Kind='Attachment' AND Phase IN ('Uploading','Ready')", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountReports = new("enterprise_request.delivery.count_reports",
        "SELECT COUNT(1) FROM demo_enterprise_request_delivery WHERE TenantId=@TenantId AND RequestId=@RequestId AND Kind='Report' AND Phase IN ('Queued','Running','Ready') AND ExpiresAtUtc>@Now", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Insert = new("enterprise_request.delivery.insert", $"""
        INSERT INTO demo_enterprise_request_delivery ({Columns}) VALUES
          (@Id,@TenantId,@RequestId,@RequestKey,@Kind,@RequestedVersion,@ActorUserId,@SessionId,@SecurityStamp,@ActorScope,
           @EffectiveScope,@SessionKind,@SourceDigest,@IncludesAmounts,@LocaleCode,NULL,@FileName,@ContentType,@SizeBytes,@ContentHash,
           @Phase,NULL,NULL,NULL,0,@Now,@ExpiresAtUtc,@Now,@Now,1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockParentSqlServer = new("enterprise_request.delivery.lock_parent.sql_server",
        "SELECT Version FROM demo_enterprise_request_enterprise_request WITH (UPDLOCK,HOLDLOCK) WHERE Id=@RequestId AND TenantId=@TenantId AND Version=@RequestedVersion AND IsDeleted=0", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockParentMySql = new("enterprise_request.delivery.lock_parent.my_sql",
        "SELECT Version FROM demo_enterprise_request_enterprise_request WHERE Id=@RequestId AND TenantId=@TenantId AND Version=@RequestedVersion AND IsDeleted=0 FOR UPDATE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement TouchParent = new("enterprise_request.delivery.touch_parent", """
        UPDATE demo_enterprise_request_enterprise_request SET Version=Version+1,UpdatedAtUtc=@Now,UpdatedById=@ActorUserId
        WHERE Id=@RequestId AND TenantId=@TenantId AND Version=@RequestedVersion AND IsDeleted=0 AND Status='Draft'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ParentExists = new("enterprise_request.delivery.parent_exists",
        "SELECT COUNT(1) FROM demo_enterprise_request_enterprise_request WHERE Id=@RequestId AND TenantId=@TenantId AND IsDeleted=0", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Acquire = new("enterprise_request.delivery.acquire", $"""
        UPDATE demo_enterprise_request_delivery SET LeaseId=@LeaseId,LeaseUntilUtc=@Until,Attempt=Attempt+1,
          Phase=CASE WHEN Phase='Queued' THEN 'Running' ELSE Phase END,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now) AND {Work}
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Release = new("enterprise_request.delivery.release", """
        UPDATE demo_enterprise_request_delivery SET LeaseId=NULL,LeaseUntilUtc=NULL,DueAtUtc=@DueAtUtc,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND LeaseId=@LeaseId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement StoreContent = new("enterprise_request.delivery.store_content", """
        UPDATE demo_enterprise_request_delivery SET ContentHash=@ContentHash,SizeBytes=@SizeBytes,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND LeaseId=@LeaseId AND Phase='Running' AND Kind='Report'
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement BindFile = new("enterprise_request.delivery.bind_file", """
        UPDATE demo_enterprise_request_delivery SET FileId=@FileId,Phase='Ready',ErrorCode=NULL,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND LeaseId=@LeaseId AND Phase=@ExpectedPhase AND ContentHash=@ContentHash
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement SavePhase = new("enterprise_request.delivery.save_phase", """
        UPDATE demo_enterprise_request_delivery SET Phase=@Phase,ErrorCode=@ErrorCode,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND LeaseId=@LeaseId AND Phase=@ExpectedPhase
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FinishCleanup = new("enterprise_request.delivery.finish_cleanup", """
        UPDATE demo_enterprise_request_delivery SET Phase=@Phase,FileId=NULL,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND LeaseId=@LeaseId AND Phase=@ExpectedPhase
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteAttachment = new("enterprise_request.delivery.delete_attachment", """
        UPDATE demo_enterprise_request_delivery SET Phase='Deleting',DueAtUtc=@Now,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND RequestId=@RequestId AND Kind='Attachment'
          AND (Phase='Ready' OR (Phase='Uploading' AND ActorUserId=@ActorUserId)) AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CancelReport = new("enterprise_request.delivery.cancel_report", """
        UPDATE demo_enterprise_request_delivery SET Phase='CancelledCleanup',DueAtUtc=@Now,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND RequestId=@RequestId AND Kind='Report' AND ActorUserId=@ActorUserId
          AND Phase IN ('Queued','Running','Ready') AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DueSqlServer = new("enterprise_request.delivery.due.sql_server",
        $"SELECT TOP (10) Id FROM demo_enterprise_request_delivery WHERE TenantId=@TenantId AND {Work} AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now) ORDER BY DueAtUtc,Id", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DueMySql = new("enterprise_request.delivery.due.my_sql",
        $"SELECT Id FROM demo_enterprise_request_delivery WHERE TenantId=@TenantId AND {Work} AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now) ORDER BY DueAtUtc,Id LIMIT 10", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    // 扫描只返回本应用持久任务中的租户身份；处理前必须通过活动租户解析建立新作用域。
    internal static readonly SqlStatement TenantScanSqlServer = new("enterprise_request.delivery.tenant_scan.sql_server",
        $"SELECT DISTINCT TOP (20) TenantId FROM demo_enterprise_request_delivery WHERE {Work} AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now) AND (@AfterTenantId IS NULL OR TenantId>@AfterTenantId) ORDER BY TenantId", SqlDataScope.Global);
    internal static readonly SqlStatement TenantScanMySql = new("enterprise_request.delivery.tenant_scan.my_sql",
        $"SELECT DISTINCT TenantId FROM demo_enterprise_request_delivery WHERE {Work} AND DueAtUtc<=@Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<=@Now) AND (@AfterTenantId IS NULL OR TenantId>@AfterTenantId) ORDER BY TenantId LIMIT 20", SqlDataScope.Global);
    internal static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
