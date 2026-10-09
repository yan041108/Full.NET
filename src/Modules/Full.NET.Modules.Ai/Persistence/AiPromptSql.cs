using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>本模块作用域内的固定双库 SQL；目录锁保护版本追加和发布指针，不修改版本正文。</summary>
internal static class AiPromptSql
{
    private const string HeadColumns = "p.Id,p.ScopeKey,p.TenantId,p.OwnerUserId,p.ClientRequestId,p.CreateHash,p.Name,p.Revision,p.LastVersion,p.ActiveVersionId,p.Epoch,p.CreatedAtUtc";
    private const string VersionColumns = "v.Id,v.PromptId,v.ScopeKey,v.TenantId,v.Version,v.FormatVersion,v.BodyJson,v.ModelConfigVersion,v.ProviderKey,v.ModelId,v.BodyHash,v.WasPublished,v.CreatedAtUtc";
    private static string Scope(bool tenant, string alias) => alias + ".ScopeKey=@ScopeKey AND " + (tenant ? alias + ".TenantId=@TenantId" : alias + ".TenantId IS NULL");
    private static string Head(bool tenant) => "SELECT " + HeadColumns + " FROM fn_ai_prompt AS p WHERE p.Id=@PromptId AND " + Scope(tenant,"p");
    private static string Client(bool tenant) => "SELECT " + HeadColumns + " FROM fn_ai_prompt AS p WHERE p.OwnerUserId=@ActorUserId AND p.ClientRequestId=@ClientRequestId AND " + Scope(tenant,"p");
    private static string Version(bool tenant) => "SELECT " + VersionColumns + " FROM fn_ai_prompt_version AS v INNER JOIN fn_ai_prompt AS p ON p.Id=v.PromptId WHERE v.Id=@VersionId AND " + Scope(tenant,"p") + " AND " + Scope(tenant,"v");
    private static string Lock(bool mysql, bool tenant) => "SELECT p.Id FROM fn_ai_prompt AS p" + (mysql ? "" : " WITH (UPDLOCK,HOLDLOCK)") + " WHERE p.Id=@PromptId AND " + Scope(tenant,"p") + (mysql ? " FOR UPDATE" : "");
    private static string List(bool mysql, bool tenant, bool versions, bool count) =>
        (count ? "SELECT COUNT(*)" : "SELECT " + (versions ? VersionColumns : HeadColumns))
        + (versions ? " FROM fn_ai_prompt_version AS v INNER JOIN fn_ai_prompt AS p ON p.Id=v.PromptId WHERE p.Id=@PromptId AND " : " FROM fn_ai_prompt AS p WHERE ")
        + Scope(tenant,"p") + (versions ? " AND " + Scope(tenant,"v") : "")
        + (count ? "" : " ORDER BY " + (versions ? "v.Version DESC,v.Id" : "p.CreatedAtUtc DESC,p.Id") + (mysql ? " LIMIT @PageSize OFFSET @Offset" : " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY"));
    private static string InsertHead(bool tenant) => "INSERT INTO fn_ai_prompt (Id,ScopeKey,TenantId,OwnerUserId,ClientRequestId,CreateHash,Name,Revision,LastVersion,ActiveVersionId,Epoch,CreatedAtUtc) VALUES (@PromptId,@ScopeKey," + (tenant ? "@TenantId" : "NULL") + ",@ActorUserId,@ClientRequestId,@CreateHash,@Name,1,1,NULL,0,@Now)";
    private static string InsertVersion(bool tenant) => "INSERT INTO fn_ai_prompt_version (Id,PromptId,ScopeKey,TenantId,Version,FormatVersion,BodyJson,ModelConfigVersion,ProviderKey,ModelId,BodyHash,WasPublished,CreatedByUserId,CreatedAtUtc) SELECT @VersionId,p.Id,@ScopeKey," + (tenant ? "@TenantId" : "NULL") + ",@Number,1,@BodyJson,@ModelConfigVersion,@ProviderKey,@ModelId,@BodyHash,0,@ActorUserId,@Now FROM fn_ai_prompt AS p WHERE p.Id=@PromptId AND " + Scope(tenant,"p");
    private static string Append(bool tenant) => "UPDATE fn_ai_prompt SET LastVersion=LastVersion+1,Revision=Revision+1 WHERE Id=@PromptId AND ScopeKey=@ScopeKey AND " + (tenant ? "TenantId=@TenantId" : "TenantId IS NULL") + " AND Revision=@Revision AND LastVersion=@PreviousNumber";
    private static string Select(bool tenant, bool deactivate) => "UPDATE fn_ai_prompt SET ActiveVersionId=" + (deactivate ? "NULL,Epoch=Epoch+1" : "@VersionId") + ",Revision=Revision+1 WHERE Id=@PromptId AND ScopeKey=@ScopeKey AND " + (tenant ? "TenantId=@TenantId" : "TenantId IS NULL") + " AND Revision=@Revision";
    private static string Mark(bool tenant) => "UPDATE fn_ai_prompt_version SET WasPublished=1 WHERE Id=@VersionId AND PromptId=@PromptId AND ScopeKey=@ScopeKey AND " + (tenant ? "TenantId=@TenantId" : "TenantId IS NULL") + " AND WasPublished=0";

    private static readonly SqlStatement HeadHost = new("ai.prompts.head_host", Head(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement HeadTenant = new("ai.prompts.head_tenant", Head(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ClientHost = new("ai.prompts.client_host", Client(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ClientTenant = new("ai.prompts.client_tenant", Client(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement VersionHost = new("ai.prompts.version_host", Version(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement VersionTenant = new("ai.prompts.version_tenant", Version(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement InsertHeadHost = new("ai.prompts.insert_head_host", InsertHead(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement InsertHeadTenant = new("ai.prompts.insert_head_tenant", InsertHead(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement InsertVersionHost = new("ai.prompts.insert_version_host", InsertVersion(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement InsertVersionTenant = new("ai.prompts.insert_version_tenant", InsertVersion(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement AppendHost = new("ai.prompts.append_host", Append(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement AppendTenant = new("ai.prompts.append_tenant", Append(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement PublishHost = new("ai.prompts.publish_host", Select(false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement PublishTenant = new("ai.prompts.publish_tenant", Select(true,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement DeactivateHost = new("ai.prompts.deactivate_host", Select(false,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement DeactivateTenant = new("ai.prompts.deactivate_tenant", Select(true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement MarkHost = new("ai.prompts.mark_host", Mark(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement MarkTenant = new("ai.prompts.mark_tenant", Mark(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ListHeadsSqlServerHost = new("ai.prompts.list_heads_sql_server_host", List(false,false,false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ListHeadsSqlServerTenant = new("ai.prompts.list_heads_sql_server_tenant", List(false,true,false,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CountHeadsSqlServerHost = new("ai.prompts.count_heads_sql_server_host", List(false,false,false,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement CountHeadsSqlServerTenant = new("ai.prompts.count_heads_sql_server_tenant", List(false,true,false,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ListVersionsSqlServerHost = new("ai.prompts.list_versions_sql_server_host", List(false,false,true,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ListVersionsSqlServerTenant = new("ai.prompts.list_versions_sql_server_tenant", List(false,true,true,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CountVersionsSqlServerHost = new("ai.prompts.count_versions_sql_server_host", List(false,false,true,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement CountVersionsSqlServerTenant = new("ai.prompts.count_versions_sql_server_tenant", List(false,true,true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ListHeadsMySqlHost = new("ai.prompts.list_heads_my_sql_host", List(true,false,false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ListHeadsMySqlTenant = new("ai.prompts.list_heads_my_sql_tenant", List(true,true,false,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CountHeadsMySqlHost = new("ai.prompts.count_heads_my_sql_host", List(true,false,false,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement CountHeadsMySqlTenant = new("ai.prompts.count_heads_my_sql_tenant", List(true,true,false,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ListVersionsMySqlHost = new("ai.prompts.list_versions_my_sql_host", List(true,false,true,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ListVersionsMySqlTenant = new("ai.prompts.list_versions_my_sql_tenant", List(true,true,true,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CountVersionsMySqlHost = new("ai.prompts.count_versions_my_sql_host", List(true,false,true,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement CountVersionsMySqlTenant = new("ai.prompts.count_versions_my_sql_tenant", List(true,true,true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LockSqlServerHost = new("ai.prompts.lock_sql_server_host", Lock(false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement LockSqlServerTenant = new("ai.prompts.lock_sql_server_tenant", Lock(false,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LockMySqlHost = new("ai.prompts.lock_my_sql_host", Lock(true,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement LockMySqlTenant = new("ai.prompts.lock_my_sql_tenant", Lock(true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement HeadFor(bool tenant) => tenant ? HeadTenant : HeadHost;
    internal static SqlStatement ClientFor(bool tenant) => tenant ? ClientTenant : ClientHost;
    internal static SqlStatement VersionFor(bool tenant) => tenant ? VersionTenant : VersionHost;
    internal static SqlStatement InsertHeadFor(bool tenant) => tenant ? InsertHeadTenant : InsertHeadHost;
    internal static SqlStatement InsertVersionFor(bool tenant) => tenant ? InsertVersionTenant : InsertVersionHost;
    internal static SqlStatement AppendFor(bool tenant) => tenant ? AppendTenant : AppendHost;
    internal static SqlStatement PublishFor(bool tenant) => tenant ? PublishTenant : PublishHost;
    internal static SqlStatement DeactivateFor(bool tenant) => tenant ? DeactivateTenant : DeactivateHost;
    internal static SqlStatement MarkFor(bool tenant) => tenant ? MarkTenant : MarkHost;
    internal static SqlStatement LockFor(DatabaseProvider provider,bool tenant) => provider==DatabaseProvider.MySql ? tenant ? LockMySqlTenant : LockMySqlHost : tenant ? LockSqlServerTenant : LockSqlServerHost;
    internal static SqlStatement ListFor(DatabaseProvider provider,bool tenant,bool versions,bool count) => (provider,tenant,versions,count) switch
    {
        (DatabaseProvider.SqlServer,false,false,false) => ListHeadsSqlServerHost,
        (DatabaseProvider.SqlServer,false,false,true) => CountHeadsSqlServerHost,
        (DatabaseProvider.SqlServer,false,true,false) => ListVersionsSqlServerHost,
        (DatabaseProvider.SqlServer,false,true,true) => CountVersionsSqlServerHost,
        (DatabaseProvider.SqlServer,true,false,false) => ListHeadsSqlServerTenant,
        (DatabaseProvider.SqlServer,true,false,true) => CountHeadsSqlServerTenant,
        (DatabaseProvider.SqlServer,true,true,false) => ListVersionsSqlServerTenant,
        (DatabaseProvider.SqlServer,true,true,true) => CountVersionsSqlServerTenant,
        (DatabaseProvider.MySql,false,false,false) => ListHeadsMySqlHost,
        (DatabaseProvider.MySql,false,false,true) => CountHeadsMySqlHost,
        (DatabaseProvider.MySql,false,true,false) => ListVersionsMySqlHost,
        (DatabaseProvider.MySql,false,true,true) => CountVersionsMySqlHost,
        (DatabaseProvider.MySql,true,false,false) => ListHeadsMySqlTenant,
        (DatabaseProvider.MySql,true,false,true) => CountHeadsMySqlTenant,
        (DatabaseProvider.MySql,true,true,false) => ListVersionsMySqlTenant,
        (DatabaseProvider.MySql,true,true,true) => CountVersionsMySqlTenant,
        _ => throw new InvalidOperationException("Unsupported database provider.")
    };
}
