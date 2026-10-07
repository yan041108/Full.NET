using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>派生消息标记和正文/依赖写入均限定当前可信所有者及未到期生成租约。</summary>
internal static class AiKnowledgeAnswerSql
{
    private const string Lease = "session.Id=@SessionId AND session.OwnerUserId=@OwnerUserId AND session.IsGenerating=1 AND session.GenerationId=@MessageId AND session.GenerationExpiresAtUtc>@Now AND session.GenerationCancellationRequested=0 AND ";
    private static string Insert(bool tenant) => "INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) SELECT @MessageId,session.Id,'assistant','','streaming',@Now,1,NULL FROM fn_ai_chat_session AS session WHERE " + Lease + (tenant ? "session.TenantId=@TenantId" : "session.TenantId IS NULL");
    // 单条 UPDATE 原子落下正文及完整依赖；缺少依赖的占位绝不能被普通聊天更新误当成已完成答案。
    private static string Complete(bool tenant) => "UPDATE fn_ai_chat_message SET Content=@Content,KnowledgeLineage=@KnowledgeLineage,StatusKey='completed' WHERE Id=@MessageId AND SessionId=@SessionId AND RoleKey='assistant' AND IsKnowledgeAnswer=1 AND KnowledgeLineage IS NULL AND StatusKey='streaming' AND EXISTS (SELECT 1 FROM fn_ai_chat_session AS session WHERE " + Lease + (tenant ? "session.TenantId=@TenantId" : "session.TenantId IS NULL") + ")";
    private static readonly SqlStatement InsertHost = new("ai.knowledge_answer.insert_host", Insert(false), SqlDataScope.HostOnly);
    private static readonly SqlStatement InsertTenant = new("ai.knowledge_answer.insert_tenant", Insert(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CompleteHost = new("ai.knowledge_answer.complete_host", Complete(false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CompleteTenant = new("ai.knowledge_answer.complete_tenant", Complete(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement InsertFor(bool tenant) => tenant ? InsertTenant : InsertHost;
    internal static SqlStatement CompleteFor(bool tenant) => tenant ? CompleteTenant : CompleteHost;

    // 首先锁定本代会话；取得锁后的短事务末尾再查询数据库当前 UTC，不能沿用连接/锁等待前的 @Now。
    private static string Lock(bool mysql, bool tenant) => "SELECT session.GenerationId FROM fn_ai_chat_session AS session" + (mysql ? "" : " WITH (UPDLOCK,HOLDLOCK)")
        + " WHERE session.Id=@SessionId AND session.OwnerUserId=@OwnerUserId AND session.GenerationId=@MessageId AND session.IsGenerating=1 AND "
        + (tenant ? "session.TenantId=@TenantId" : "session.TenantId IS NULL") + (mysql ? " FOR UPDATE" : "");
    private static string Current(bool mysql, bool tenant) => "SELECT session.GenerationId FROM fn_ai_chat_session AS session WHERE " + Lease
        + (tenant ? "session.TenantId=@TenantId" : "session.TenantId IS NULL") + " AND session.GenerationExpiresAtUtc>" + (mysql ? "UTC_TIMESTAMP(6)" : "SYSUTCDATETIME()");
    private static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_answer.lock_sqlserver_host", Lock(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_answer.lock_sqlserver_tenant", Lock(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LockMySqlHost = new("ai.knowledge_answer.lock_mysql_host", Lock(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_answer.lock_mysql_tenant", Lock(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CurrentSqlServerHost = new("ai.knowledge_answer.current_sqlserver_host", Current(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CurrentSqlServerTenant = new("ai.knowledge_answer.current_sqlserver_tenant", Current(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CurrentMySqlHost = new("ai.knowledge_answer.current_mysql_host", Current(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CurrentMySqlTenant = new("ai.knowledge_answer.current_mysql_tenant", Current(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement LockFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? LockMySqlTenant : LockMySqlHost : tenant ? LockSqlServerTenant : LockSqlServerHost;
    internal static SqlStatement CurrentFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? CurrentMySqlTenant : CurrentMySqlHost : tenant ? CurrentSqlServerTenant : CurrentSqlServerHost;
}
