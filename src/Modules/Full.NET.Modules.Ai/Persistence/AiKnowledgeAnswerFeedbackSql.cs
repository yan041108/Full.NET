using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>会话锁串行化创建；所有命令再次绑定本人会话、实际消息及可信租户。</summary>
internal static class AiKnowledgeAnswerFeedbackSql
{
    private static string Scope(bool tenant, string alias) => tenant ? alias + ".TenantId=@TenantId" : alias + ".TenantId IS NULL";
    private static string Owner(bool tenant) => "session.Id=@SessionId AND session.OwnerUserId=@OwnerUserId AND " + Scope(tenant, "session");
    private static string Message(bool tenant) => "message.Id=@MessageId AND message.SessionId=session.Id AND message.RoleKey='assistant' AND message.StatusKey='completed' AND message.IsKnowledgeAnswer=1 AND " + Owner(tenant);
    private static string FindMessage(bool tenant) => "SELECT message.Id,message.SessionId,message.RoleKey,'' AS Content,message.StatusKey,message.IsKnowledgeAnswer,message.KnowledgeLineage,message.PromptTokens,message.CompletionTokens,message.CreatedAtUtc FROM fn_ai_chat_message AS message INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId WHERE " + Message(tenant);
    private static string Find(bool tenant) => "SELECT feedback.Id,feedback.MessageId,feedback.RatingKey,feedback.ReasonKey,feedback.CitationIdsJson,feedback.LineageHash,feedback.StatusKey,feedback.Version,feedback.UpdatedAtUtc FROM fn_ai_knowledge_answer_feedback AS feedback INNER JOIN fn_ai_chat_message AS message ON message.Id=feedback.MessageId INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId WHERE " + Message(tenant) + " AND feedback.SessionId=session.Id AND feedback.ActorUserId=@OwnerUserId AND " + Scope(tenant, "feedback");
    private static string Lock(bool mysql, bool tenant) => "SELECT session.Id FROM fn_ai_chat_session AS session" + (mysql ? "" : " WITH (UPDLOCK,HOLDLOCK)") + " WHERE " + Owner(tenant) + (mysql ? " FOR UPDATE" : "");
    private static string Insert(bool tenant) => "INSERT INTO fn_ai_knowledge_answer_feedback (Id,TenantId,SessionId,MessageId,ActorUserId,RatingKey,ReasonKey,CitationIdsJson,LineageHash,StatusKey,Version,CreatedAtUtc,UpdatedAtUtc) SELECT @Id," + (tenant ? "@TenantId" : "NULL") + ",session.Id,message.Id,@OwnerUserId,@RatingKey,@ReasonKey,@CitationIdsJson,@LineageHash,'pending_review',1,@Now,@Now FROM fn_ai_chat_message AS message INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId WHERE " + Message(tenant) + " AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_answer_feedback WHERE MessageId=@MessageId)";
    private static string Update(bool tenant, bool withdraw) => "UPDATE fn_ai_knowledge_answer_feedback SET RatingKey=" + (withdraw ? "NULL" : "@RatingKey") + ",ReasonKey=" + (withdraw ? "NULL" : "@ReasonKey") + ",CitationIdsJson=" + (withdraw ? "'[]'" : "@CitationIdsJson") + ",LineageHash=" + (withdraw ? "NULL" : "@LineageHash") + ",StatusKey='" + (withdraw ? "withdrawn" : "pending_review") + "',Version=Version+1,UpdatedAtUtc=@Now WHERE MessageId=@MessageId AND SessionId=@SessionId AND ActorUserId=@OwnerUserId AND Version=@Version AND " + Scope(tenant, "fn_ai_knowledge_answer_feedback") + " AND EXISTS (SELECT 1 FROM fn_ai_chat_message AS message INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId WHERE " + Message(tenant) + ")";
    private static readonly SqlStatement MessageHost = new("ai.knowledge_answer_feedback.message_host", FindMessage(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement MessageTenant = new("ai.knowledge_answer_feedback.message_tenant", FindMessage(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FindHost = new("ai.knowledge_answer_feedback.find_host", Find(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement FindTenant = new("ai.knowledge_answer_feedback.find_tenant", Find(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement InsertHost = new("ai.knowledge_answer_feedback.insert_host", Insert(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement InsertTenant = new("ai.knowledge_answer_feedback.insert_tenant", Insert(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement UpdateHost = new("ai.knowledge_answer_feedback.update_host", Update(false, false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement UpdateTenant = new("ai.knowledge_answer_feedback.update_tenant", Update(true, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement WithdrawHost = new("ai.knowledge_answer_feedback.withdraw_host", Update(false, true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement WithdrawTenant = new("ai.knowledge_answer_feedback.withdraw_tenant", Update(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_answer_feedback.lock_sqlserver_host", Lock(false, false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_answer_feedback.lock_sqlserver_tenant", Lock(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LockMySqlHost = new("ai.knowledge_answer_feedback.lock_mysql_host", Lock(true, false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_answer_feedback.lock_mysql_tenant", Lock(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement MessageFor(bool tenant) => tenant ? MessageTenant : MessageHost;
    internal static SqlStatement FindFor(bool tenant) => tenant ? FindTenant : FindHost;
    internal static SqlStatement InsertFor(bool tenant) => tenant ? InsertTenant : InsertHost;
    internal static SqlStatement UpdateFor(bool tenant, bool withdraw) => withdraw ? tenant ? WithdrawTenant : WithdrawHost : tenant ? UpdateTenant : UpdateHost;
    internal static SqlStatement LockFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql ? tenant ? LockMySqlTenant : LockMySqlHost : tenant ? LockSqlServerTenant : LockSqlServerHost;
}
