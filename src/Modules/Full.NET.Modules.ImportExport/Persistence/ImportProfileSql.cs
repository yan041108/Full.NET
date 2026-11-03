using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>只支持可信租户本人配置；所有语句显式绑定租户、所有者和静态来源。</summary>
internal static class ImportProfileSql
{
    internal static readonly SqlStatement EnsureScopeSqlServer = new("import_export.profile.ensure_scope.sql_server", """
        IF NOT EXISTS (SELECT 1 FROM fn_import_export_profile_scope WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND UserId=@UserId)
            INSERT INTO fn_import_export_profile_scope (Id,TenantId,UserId,CreatedAtUtc) VALUES (@ScopeId,@TenantId,@UserId,@Now)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockScopeSqlServer = new("import_export.profile.lock_scope.sql_server", """
        SELECT Id FROM fn_import_export_profile_scope WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND UserId=@UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServer = new("import_export.profile.list.sql_server", """
        SELECT TOP (32) Id,Name,HeaderFingerprint,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindSqlServer = new("import_export.profile.find.sql_server", """
        SELECT Id,Name,HeaderFingerprint,CreatedAtUtc,UpdatedAtUtc,Version,ConfigurationJson FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey AND Id=@Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountSqlServer = new("import_export.profile.count.sql_server", """
        SELECT COUNT(1) FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertSqlServer = new("import_export.profile.insert.sql_server", """
        INSERT INTO fn_import_export_profile (Id,TenantId,OwnerUserId,SchemaKey,WorksheetKey,Name,HeaderFingerprint,ConfigurationJson,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,@TenantId,@UserId,@SchemaKey,@WorksheetKey,@Name,@Fingerprint,@ConfigurationJson,@Now,NULL,1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateSqlServer = new("import_export.profile.update.sql_server", """
        UPDATE fn_import_export_profile SET Name=@Name,HeaderFingerprint=@Fingerprint,ConfigurationJson=@ConfigurationJson,UpdatedAtUtc=@Now,Version=Version+1
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteSqlServer = new("import_export.profile.delete.sql_server", """
        DELETE FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement EnsureScopeMySql = new("import_export.profile.ensure_scope.my_sql", """
        INSERT INTO fn_import_export_profile_scope (Id,TenantId,UserId,CreatedAtUtc) VALUES (@ScopeId,@TenantId,@UserId,@Now)
        ON DUPLICATE KEY UPDATE UserId=UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockScopeMySql = new("import_export.profile.lock_scope.my_sql", """
        SELECT Id FROM fn_import_export_profile_scope WHERE TenantId=@TenantId AND UserId=@UserId FOR UPDATE
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySql = new("import_export.profile.list.my_sql", """
        SELECT Id,Name,HeaderFingerprint,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT 32
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindMySql = new("import_export.profile.find.my_sql", """
        SELECT Id,Name,HeaderFingerprint,CreatedAtUtc,UpdatedAtUtc,Version,ConfigurationJson FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey AND Id=@Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountMySql = new("import_export.profile.count.my_sql", """
        SELECT COUNT(1) FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertMySql = new("import_export.profile.insert.my_sql", """
        INSERT INTO fn_import_export_profile (Id,TenantId,OwnerUserId,SchemaKey,WorksheetKey,Name,HeaderFingerprint,ConfigurationJson,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,@TenantId,@UserId,@SchemaKey,@WorksheetKey,@Name,@Fingerprint,@ConfigurationJson,@Now,NULL,1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateMySql = new("import_export.profile.update.my_sql", """
        UPDATE fn_import_export_profile SET Name=@Name,HeaderFingerprint=@Fingerprint,ConfigurationJson=@ConfigurationJson,UpdatedAtUtc=@Now,Version=Version+1
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement DeleteMySql = new("import_export.profile.delete.my_sql", """
        DELETE FROM fn_import_export_profile
        WHERE TenantId=@TenantId AND OwnerUserId=@UserId AND SchemaKey=@SchemaKey AND WorksheetKey=@WorksheetKey AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    private static readonly ImportProfileStatements SqlServer = new(EnsureScopeSqlServer,LockScopeSqlServer,ListSqlServer,FindSqlServer,CountSqlServer,InsertSqlServer,UpdateSqlServer,DeleteSqlServer);
    private static readonly ImportProfileStatements MySql = new(EnsureScopeMySql,LockScopeMySql,ListMySql,FindMySql,CountMySql,InsertMySql,UpdateMySql,DeleteMySql);
    internal static ImportProfileStatements For(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => SqlServer, DatabaseProvider.MySql => MySql,
        _ => throw new InvalidOperationException("个人导入配置不支持当前数据库提供程序。"),
    };
}

/// <summary>只组合已静态声明的双库语句，不在请求中构造 SQL。</summary>
internal sealed record ImportProfileStatements(SqlStatement EnsureScope, SqlStatement LockScope, SqlStatement List,
    SqlStatement Find, SqlStatement Count, SqlStatement Insert, SqlStatement Update, SqlStatement Delete);
