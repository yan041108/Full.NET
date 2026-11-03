using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>Host/Tenant 与两个提供程序均为显式静态声明，便于授权、架构扫描及 AOT 审核。</summary>
internal static class QuerySchemeSql
{
    internal static readonly SqlStatement HostSqlServerEnsureDefault = new(
        "settings.query_scheme.host.sqlserver.ensure_default",
        """
        IF NOT EXISTS (SELECT 1 FROM fn_settings_query_scheme_default WITH (UPDLOCK,HOLDLOCK) WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId)
            INSERT INTO fn_settings_query_scheme_default (Id,TenantId,ScopeKey,GridKey,UserId,DefaultSchemeId,Version,CreatedAtUtc)
            VALUES (@DefaultId,NULL,@ScopeKey,@GridKey,@UserId,NULL,0,@Now);
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerLockDefault = new(
        "settings.query_scheme.host.sqlserver.lock_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WITH (UPDLOCK,HOLDLOCK) WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerFindDefault = new(
        "settings.query_scheme.host.sqlserver.find_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerUpdateDefault = new(
        "settings.query_scheme.host.sqlserver.update_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=@SchemeId,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND Version=@Version
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerClearOwnDefault = new(
        "settings.query_scheme.host.sqlserver.clear_own_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=NULL,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND DefaultSchemeId=@Id
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerCountOwned = new(
        "settings.query_scheme.host.sqlserver.count_owned",
        """
        SELECT COUNT(1) FROM fn_settings_query_scheme WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerFindVisible = new(
        "settings.query_scheme.host.sqlserver.find_visible",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version,ConfigurationJson FROM fn_settings_query_scheme WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND Id=@Id AND (OwnerUserId=@UserId OR IsShared=1)
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerMineFirst = new(
        "settings.query_scheme.host.sqlserver.mine_first",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerMineAfter = new(
        "settings.query_scheme.host.sqlserver.mine_after",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerSharedFirst = new(
        "settings.query_scheme.host.sqlserver.shared_first",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerSharedAfter = new(
        "settings.query_scheme.host.sqlserver.shared_after",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerInsert = new(
        "settings.query_scheme.host.sqlserver.insert",
        """
        INSERT INTO fn_settings_query_scheme (Id,TenantId,ScopeKey,GridKey,OwnerUserId,Name,SchemaVersion,ConfigurationJson,IsShared,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,NULL,@ScopeKey,@GridKey,@UserId,@Name,@SchemaVersion,@ConfigurationJson,0,@Now,NULL,1)
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerUpdate = new(
        "settings.query_scheme.host.sqlserver.update",
        """
        UPDATE fn_settings_query_scheme SET Name=@Name,SchemaVersion=@SchemaVersion,ConfigurationJson=@ConfigurationJson,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerShare = new(
        "settings.query_scheme.host.sqlserver.share",
        """
        UPDATE fn_settings_query_scheme SET IsShared=@IsShared,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostSqlServerDelete = new(
        "settings.query_scheme.host.sqlserver.delete",
        """
        DELETE FROM fn_settings_query_scheme WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);

    private static readonly QuerySchemeStatements HostSqlServer = new(
        HostSqlServerEnsureDefault,
        HostSqlServerLockDefault,
        HostSqlServerFindDefault,
        HostSqlServerUpdateDefault,
        HostSqlServerClearOwnDefault,
        HostSqlServerCountOwned,
        HostSqlServerFindVisible,
        HostSqlServerMineFirst,
        HostSqlServerMineAfter,
        HostSqlServerSharedFirst,
        HostSqlServerSharedAfter,
        HostSqlServerInsert,
        HostSqlServerUpdate,
        HostSqlServerShare,
        HostSqlServerDelete);

    internal static readonly SqlStatement HostMySqlEnsureDefault = new(
        "settings.query_scheme.host.mysql.ensure_default",
        """
        INSERT INTO fn_settings_query_scheme_default (Id,TenantId,ScopeKey,GridKey,UserId,DefaultSchemeId,Version,CreatedAtUtc)
        VALUES (@DefaultId,NULL,@ScopeKey,@GridKey,@UserId,NULL,0,@Now)
        ON DUPLICATE KEY UPDATE Id=Id;
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlLockDefault = new(
        "settings.query_scheme.host.mysql.lock_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId FOR UPDATE
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlFindDefault = new(
        "settings.query_scheme.host.mysql.find_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlUpdateDefault = new(
        "settings.query_scheme.host.mysql.update_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=@SchemeId,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND Version=@Version
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlClearOwnDefault = new(
        "settings.query_scheme.host.mysql.clear_own_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=NULL,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND DefaultSchemeId=@Id
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlCountOwned = new(
        "settings.query_scheme.host.mysql.count_owned",
        """
        SELECT COUNT(1) FROM fn_settings_query_scheme WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlFindVisible = new(
        "settings.query_scheme.host.mysql.find_visible",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version,ConfigurationJson FROM fn_settings_query_scheme WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND Id=@Id AND (OwnerUserId=@UserId OR IsShared=1)
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlMineFirst = new(
        "settings.query_scheme.host.mysql.mine_first",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlMineAfter = new(
        "settings.query_scheme.host.mysql.mine_after",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlSharedFirst = new(
        "settings.query_scheme.host.mysql.shared_first",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlSharedAfter = new(
        "settings.query_scheme.host.mysql.shared_after",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlInsert = new(
        "settings.query_scheme.host.mysql.insert",
        """
        INSERT INTO fn_settings_query_scheme (Id,TenantId,ScopeKey,GridKey,OwnerUserId,Name,SchemaVersion,ConfigurationJson,IsShared,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,NULL,@ScopeKey,@GridKey,@UserId,@Name,@SchemaVersion,@ConfigurationJson,0,@Now,NULL,1)
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlUpdate = new(
        "settings.query_scheme.host.mysql.update",
        """
        UPDATE fn_settings_query_scheme SET Name=@Name,SchemaVersion=@SchemaVersion,ConfigurationJson=@ConfigurationJson,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlShare = new(
        "settings.query_scheme.host.mysql.share",
        """
        UPDATE fn_settings_query_scheme SET IsShared=@IsShared,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);

    internal static readonly SqlStatement HostMySqlDelete = new(
        "settings.query_scheme.host.mysql.delete",
        """
        DELETE FROM fn_settings_query_scheme WHERE TenantId IS NULL AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.HostOnly);

    private static readonly QuerySchemeStatements HostMySql = new(
        HostMySqlEnsureDefault,
        HostMySqlLockDefault,
        HostMySqlFindDefault,
        HostMySqlUpdateDefault,
        HostMySqlClearOwnDefault,
        HostMySqlCountOwned,
        HostMySqlFindVisible,
        HostMySqlMineFirst,
        HostMySqlMineAfter,
        HostMySqlSharedFirst,
        HostMySqlSharedAfter,
        HostMySqlInsert,
        HostMySqlUpdate,
        HostMySqlShare,
        HostMySqlDelete);

    internal static readonly SqlStatement TenantSqlServerEnsureDefault = new(
        "settings.query_scheme.tenant.sqlserver.ensure_default",
        """
        IF NOT EXISTS (SELECT 1 FROM fn_settings_query_scheme_default WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId)
            INSERT INTO fn_settings_query_scheme_default (Id,TenantId,ScopeKey,GridKey,UserId,DefaultSchemeId,Version,CreatedAtUtc)
            VALUES (@DefaultId,@TenantId,@ScopeKey,@GridKey,@UserId,NULL,0,@Now);
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerLockDefault = new(
        "settings.query_scheme.tenant.sqlserver.lock_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerFindDefault = new(
        "settings.query_scheme.tenant.sqlserver.find_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerUpdateDefault = new(
        "settings.query_scheme.tenant.sqlserver.update_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=@SchemeId,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerClearOwnDefault = new(
        "settings.query_scheme.tenant.sqlserver.clear_own_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=NULL,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND DefaultSchemeId=@Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerCountOwned = new(
        "settings.query_scheme.tenant.sqlserver.count_owned",
        """
        SELECT COUNT(1) FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerFindVisible = new(
        "settings.query_scheme.tenant.sqlserver.find_visible",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version,ConfigurationJson FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND Id=@Id AND (OwnerUserId=@UserId OR IsShared=1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerMineFirst = new(
        "settings.query_scheme.tenant.sqlserver.mine_first",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerMineAfter = new(
        "settings.query_scheme.tenant.sqlserver.mine_after",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerSharedFirst = new(
        "settings.query_scheme.tenant.sqlserver.shared_first",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerSharedAfter = new(
        "settings.query_scheme.tenant.sqlserver.shared_after",
        """
        SELECT TOP (@Take) Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerInsert = new(
        "settings.query_scheme.tenant.sqlserver.insert",
        """
        INSERT INTO fn_settings_query_scheme (Id,TenantId,ScopeKey,GridKey,OwnerUserId,Name,SchemaVersion,ConfigurationJson,IsShared,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,@TenantId,@ScopeKey,@GridKey,@UserId,@Name,@SchemaVersion,@ConfigurationJson,0,@Now,NULL,1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerUpdate = new(
        "settings.query_scheme.tenant.sqlserver.update",
        """
        UPDATE fn_settings_query_scheme SET Name=@Name,SchemaVersion=@SchemaVersion,ConfigurationJson=@ConfigurationJson,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerShare = new(
        "settings.query_scheme.tenant.sqlserver.share",
        """
        UPDATE fn_settings_query_scheme SET IsShared=@IsShared,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantSqlServerDelete = new(
        "settings.query_scheme.tenant.sqlserver.delete",
        """
        DELETE FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    private static readonly QuerySchemeStatements TenantSqlServer = new(
        TenantSqlServerEnsureDefault,
        TenantSqlServerLockDefault,
        TenantSqlServerFindDefault,
        TenantSqlServerUpdateDefault,
        TenantSqlServerClearOwnDefault,
        TenantSqlServerCountOwned,
        TenantSqlServerFindVisible,
        TenantSqlServerMineFirst,
        TenantSqlServerMineAfter,
        TenantSqlServerSharedFirst,
        TenantSqlServerSharedAfter,
        TenantSqlServerInsert,
        TenantSqlServerUpdate,
        TenantSqlServerShare,
        TenantSqlServerDelete);

    internal static readonly SqlStatement TenantMySqlEnsureDefault = new(
        "settings.query_scheme.tenant.mysql.ensure_default",
        """
        INSERT INTO fn_settings_query_scheme_default (Id,TenantId,ScopeKey,GridKey,UserId,DefaultSchemeId,Version,CreatedAtUtc)
        VALUES (@DefaultId,@TenantId,@ScopeKey,@GridKey,@UserId,NULL,0,@Now)
        ON DUPLICATE KEY UPDATE Id=Id;
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlLockDefault = new(
        "settings.query_scheme.tenant.mysql.lock_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId FOR UPDATE
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlFindDefault = new(
        "settings.query_scheme.tenant.mysql.find_default",
        """
        SELECT DefaultSchemeId,Version FROM fn_settings_query_scheme_default WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlUpdateDefault = new(
        "settings.query_scheme.tenant.mysql.update_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=@SchemeId,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlClearOwnDefault = new(
        "settings.query_scheme.tenant.mysql.clear_own_default",
        """
        UPDATE fn_settings_query_scheme_default SET DefaultSchemeId=NULL,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND UserId=@UserId AND DefaultSchemeId=@Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlCountOwned = new(
        "settings.query_scheme.tenant.mysql.count_owned",
        """
        SELECT COUNT(1) FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlFindVisible = new(
        "settings.query_scheme.tenant.mysql.find_visible",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version,ConfigurationJson FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND Id=@Id AND (OwnerUserId=@UserId OR IsShared=1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlMineFirst = new(
        "settings.query_scheme.tenant.mysql.mine_first",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlMineAfter = new(
        "settings.query_scheme.tenant.mysql.mine_after",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlSharedFirst = new(
        "settings.query_scheme.tenant.mysql.shared_first",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlSharedAfter = new(
        "settings.query_scheme.tenant.mysql.shared_after",
        """
        SELECT Id,OwnerUserId,Name,SchemaVersion,IsShared,CreatedAtUtc,UpdatedAtUtc,Version FROM fn_settings_query_scheme
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND IsShared=1 AND OwnerUserId<>@UserId
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlInsert = new(
        "settings.query_scheme.tenant.mysql.insert",
        """
        INSERT INTO fn_settings_query_scheme (Id,TenantId,ScopeKey,GridKey,OwnerUserId,Name,SchemaVersion,ConfigurationJson,IsShared,CreatedAtUtc,UpdatedAtUtc,Version)
        VALUES (@Id,@TenantId,@ScopeKey,@GridKey,@UserId,@Name,@SchemaVersion,@ConfigurationJson,0,@Now,NULL,1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlUpdate = new(
        "settings.query_scheme.tenant.mysql.update",
        """
        UPDATE fn_settings_query_scheme SET Name=@Name,SchemaVersion=@SchemaVersion,ConfigurationJson=@ConfigurationJson,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlShare = new(
        "settings.query_scheme.tenant.mysql.share",
        """
        UPDATE fn_settings_query_scheme SET IsShared=@IsShared,UpdatedAtUtc=@Now,Version=Version+1 WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement TenantMySqlDelete = new(
        "settings.query_scheme.tenant.mysql.delete",
        """
        DELETE FROM fn_settings_query_scheme WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND GridKey=@GridKey AND OwnerUserId=@UserId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    private static readonly QuerySchemeStatements TenantMySql = new(
        TenantMySqlEnsureDefault,
        TenantMySqlLockDefault,
        TenantMySqlFindDefault,
        TenantMySqlUpdateDefault,
        TenantMySqlClearOwnDefault,
        TenantMySqlCountOwned,
        TenantMySqlFindVisible,
        TenantMySqlMineFirst,
        TenantMySqlMineAfter,
        TenantMySqlSharedFirst,
        TenantMySqlSharedAfter,
        TenantMySqlInsert,
        TenantMySqlUpdate,
        TenantMySqlShare,
        TenantMySqlDelete);

    internal static QuerySchemeStatements For(bool host, DatabaseProvider provider) => (host, provider) switch
    {
        (true, DatabaseProvider.SqlServer) => HostSqlServer,
        (true, DatabaseProvider.MySql) => HostMySql,
        (false, DatabaseProvider.SqlServer) => TenantSqlServer,
        (false, DatabaseProvider.MySql) => TenantMySql,
        _ => throw new InvalidOperationException("查询方案不支持当前数据库提供程序。"),
    };
}

/// <summary>仅组合已声明语句，不构造 SQL；本人和共享使用不同查询以保留索引前缀。</summary>
internal sealed record QuerySchemeStatements(
    SqlStatement EnsureDefault,
    SqlStatement LockDefault,
    SqlStatement FindDefault,
    SqlStatement UpdateDefault,
    SqlStatement ClearOwnDefault,
    SqlStatement CountOwned,
    SqlStatement FindVisible,
    SqlStatement MineFirst,
    SqlStatement MineAfter,
    SqlStatement SharedFirst,
    SqlStatement SharedAfter,
    SqlStatement Insert,
    SqlStatement Update,
    SqlStatement Share,
    SqlStatement Delete);
