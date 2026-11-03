using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>只保存解析调度元数据；所有状态写入沿同一文档根锁，完成时绑定原租约。</summary>
internal static class AiKnowledgeCorrectionPreparationSql
{
    private const string Table = "fn_ai_knowledge_correction_preparation";
    private const string Root = " INNER JOIN fn_ai_knowledge_version AS version ON version.Id=task.VersionId INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId";
    private const string Binding = " AND version.Id=@VersionId AND document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId";
    private const string Host = " AND knowledge.TenantId IS NULL";
    private const string Tenant = " AND knowledge.TenantId=@TenantId";
    private const string Current = AiKnowledgeOrdinaryDocumentSql.Visible + " AND " + "document.IsDeleted=0 AND knowledge.IsEnabled=1 AND document.Version=task.DocumentVersion AND knowledge.OwnerUserId=task.ActorUserId AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber)";
    private const string Columns = "task.Id,task.VersionId,task.DocumentVersion,task.RequestKey,task.RequestHash,task.ActorUserId,task.SessionId,task.SessionKind,task.SecurityStamp,task.ActorScope,task.EffectiveScope,task.StateKey,task.AttemptCount,task.LeaseToken,task.LeaseExpiresAtUtc,task.ParserRuntimeId,task.SourceId,task.FailureCode,task.CreatedAtUtc,task.UpdatedAtUtc,knowledge.Id AS KnowledgeBaseId,document.Id AS DocumentId,knowledge.TenantId,knowledge.OwnerUserId,version.ContentType,CASE WHEN " + Current + " THEN 1 ELSE 0 END AS IsCurrent";
    private const string Read = "SELECT " + Columns + " FROM " + Table + " AS task" + Root + " WHERE task.Id=@PreparationId" + Binding;
    internal static readonly SqlStatement ReadHost = new("ai.knowledge_correction_preparation.read_host", Read + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadTenant = new("ai.knowledge_correction_preparation.read_tenant", Read + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Key = "SELECT " + Columns + " FROM " + Table + " AS task" + Root + " WHERE task.RequestKey=@RequestKey" + Binding;
    internal static readonly SqlStatement KeyHost = new("ai.knowledge_correction_preparation.key_host", Key + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement KeyTenant = new("ai.knowledge_correction_preparation.key_tenant", Key + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Active = " FROM " + Table + " AS task" + Root + " WHERE task.StateKey IN ('queued','running')" + Binding;
    private static readonly SqlStatement ActiveSqlServerHost = new("ai.knowledge_correction_preparation.active_sqlserver_host", "SELECT TOP (33) " + Columns + Active + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement ActiveSqlServerTenant = new("ai.knowledge_correction_preparation.active_sqlserver_tenant", "SELECT TOP (33) " + Columns + Active + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ActiveMySqlHost = new("ai.knowledge_correction_preparation.active_mysql_host", "SELECT " + Columns + Active + Host + " LIMIT 33", SqlDataScope.HostOnly);
    private static readonly SqlStatement ActiveMySqlTenant = new("ai.knowledge_correction_preparation.active_mysql_tenant", "SELECT " + Columns + Active + Tenant + " LIMIT 33", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement ActiveFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? ActiveMySqlTenant : ActiveMySqlHost : scoped ? ActiveSqlServerTenant : ActiveSqlServerHost;
    private const string Latest = " FROM " + Table + " AS task" + Root + " WHERE 1=1" + Binding;
    private const string LatestOrder = " ORDER BY task.CreatedAtUtc DESC,task.Id DESC";
    private static readonly SqlStatement LatestSqlServerHost = new("ai.knowledge_correction_preparation.latest_sqlserver_host", "SELECT TOP (1) " + Columns + Latest + Host + LatestOrder, SqlDataScope.HostOnly);
    private static readonly SqlStatement LatestSqlServerTenant = new("ai.knowledge_correction_preparation.latest_sqlserver_tenant", "SELECT TOP (1) " + Columns + Latest + Tenant + LatestOrder, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LatestMySqlHost = new("ai.knowledge_correction_preparation.latest_mysql_host", "SELECT " + Columns + Latest + Host + LatestOrder + " LIMIT 1", SqlDataScope.HostOnly);
    private static readonly SqlStatement LatestMySqlTenant = new("ai.knowledge_correction_preparation.latest_mysql_tenant", "SELECT " + Columns + Latest + Tenant + LatestOrder + " LIMIT 1", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement LatestFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? LatestMySqlTenant : LatestMySqlHost : scoped ? LatestSqlServerTenant : LatestSqlServerHost;
    private const string Candidates = " FROM " + Table + " AS task" + Root + " WHERE task.StateKey='queued' OR (task.StateKey='running' AND task.LeaseExpiresAtUtc<=@Now) ORDER BY task.CreatedAtUtc,task.Id";
    private const string CandidateColumns = "task.Id,task.VersionId,knowledge.Id AS KnowledgeBaseId,document.Id AS DocumentId,knowledge.OwnerUserId,knowledge.TenantId,task.DocumentVersion,version.ContentType";
    // 全局扫描只引导可信 AI 作用域；失效资源也必须进入取消收尾，不能永久占据队列。
    internal static readonly SqlStatement ScanSqlServer = new("ai.knowledge_correction_preparation.scan_sqlserver", "SELECT TOP (@BatchSize) " + CandidateColumns + Candidates, SqlDataScope.Global);
    internal static readonly SqlStatement ScanMySql = new("ai.knowledge_correction_preparation.scan_mysql", "SELECT " + CandidateColumns + Candidates + " LIMIT @BatchSize", SqlDataScope.Global);
    private const string Insert = "INSERT INTO " + Table + " (Id,VersionId,DocumentVersion,RequestKey,RequestHash,ActorUserId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,StateKey,AttemptCount,LeaseToken,LeaseExpiresAtUtc,ParserRuntimeId,SourceId,FailureCode,CreatedAtUtc,UpdatedAtUtc) SELECT @PreparationId,version.Id,@DocumentVersion,@RequestKey,@RequestHash,@OwnerUserId,@SessionId,@SessionKind,@SecurityStamp,@ActorScope,@EffectiveScope,'queued',0,NULL,NULL,NULL,NULL,NULL,@Now,@Now FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE version.Id=@VersionId AND document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND document.Version=@DocumentVersion AND document.IsDeleted=0 AND knowledge.IsEnabled=1 AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber)" + " AND " + AiKnowledgeOrdinaryDocumentSql.Visible;
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_correction_preparation.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_correction_preparation.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Exists = " AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE version.Id=" + Table + ".VersionId" + Binding;
    private const string Claim = "UPDATE " + Table + " SET StateKey='running',AttemptCount=AttemptCount+1,LeaseToken=@LeaseToken,LeaseExpiresAtUtc=@LeaseExpiresAtUtc,ParserRuntimeId=@ParserRuntimeId,UpdatedAtUtc=@Now WHERE Id=@PreparationId AND AttemptCount<3 AND (ParserRuntimeId IS NULL OR ParserRuntimeId=@ParserRuntimeId) AND (StateKey='queued' OR (StateKey='running' AND LeaseExpiresAtUtc<=@Now))" + Exists;
    internal static readonly SqlStatement ClaimHost = new("ai.knowledge_correction_preparation.claim_host", Claim + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ClaimTenant = new("ai.knowledge_correction_preparation.claim_tenant", Claim + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Stop = "UPDATE " + Table + " SET StateKey=@StateKey,FailureCode=@FailureCode,LeaseToken=NULL,LeaseExpiresAtUtc=NULL,UpdatedAtUtc=@Now WHERE Id=@PreparationId AND (StateKey='queued' OR (StateKey='running' AND (LeaseExpiresAtUtc<=@Now OR @Stale=1)))" + Exists;
    internal static readonly SqlStatement StopHost = new("ai.knowledge_correction_preparation.stop_host", Stop + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement StopTenant = new("ai.knowledge_correction_preparation.stop_tenant", Stop + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string FailHeld = "UPDATE " + Table + " SET StateKey='failed',FailureCode='ai.knowledge.correction_preparation_unavailable',LeaseToken=NULL,LeaseExpiresAtUtc=NULL,UpdatedAtUtc=@Now WHERE Id=@PreparationId AND StateKey='running' AND LeaseToken=@LeaseToken AND LeaseExpiresAtUtc>@Now" + Exists;
    internal static readonly SqlStatement FailHost = new("ai.knowledge_correction_preparation.fail_host", FailHeld + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FailTenant = new("ai.knowledge_correction_preparation.fail_tenant", FailHeld + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Held = "SELECT COUNT(*) FROM " + Table + " AS task" + Root + " WHERE task.Id=@PreparationId AND task.StateKey='running' AND task.LeaseToken=@PreparationLease AND task.LeaseExpiresAtUtc>@LeaseNow AND task.RequestHash=@PreparationHash AND task.ParserRuntimeId=@ParserRuntimeId AND " + Current + Binding;
    internal static readonly SqlStatement HeldHost = new("ai.knowledge_correction_preparation.held_host", Held + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement HeldTenant = new("ai.knowledge_correction_preparation.held_tenant", Held + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    // 此更新由来源存储器在已验证 Held 的同一根锁事务内调用；失败必须回滚来源及首次头。
    private const string Complete = "UPDATE " + Table + " SET StateKey='completed',SourceId=@SourceId,LeaseToken=NULL,LeaseExpiresAtUtc=NULL,FailureCode=NULL,UpdatedAtUtc=@LeaseNow WHERE Id=@PreparationId AND StateKey='running' AND LeaseToken=@PreparationLease AND LeaseExpiresAtUtc>@LeaseNow AND RequestHash=@PreparationHash AND ParserRuntimeId=@ParserRuntimeId" + Exists;
    internal static readonly SqlStatement CompleteHost = new("ai.knowledge_correction_preparation.complete_host", Complete + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement CompleteTenant = new("ai.knowledge_correction_preparation.complete_tenant", Complete + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
