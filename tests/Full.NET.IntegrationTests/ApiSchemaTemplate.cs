using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests;

/// <summary>
/// 将空 API 测试库替换为进程内只读 schema 模板的独立克隆。
/// 模板只包含 DbUp 迁移后的结构和 journal，不含租户、管理员或导航业务行。
/// 每个测试仍使用独立数据库并自行引导；禁止把可变业务库借给下一个用例。
/// 复用容器上的模板若 journal 与当前嵌入迁移不一致，或残留旧版引导数据，必须丢弃重建。
/// </summary>
internal static class ApiSchemaTemplate
{
    private const string SqlServerTemplateDatabase = "fullnet_it_schema_sql";
    private const string MySqlTemplateDatabase = "fullnet_it_schema_mysql";
    private const string MySqlTemplateBootstrapLockName = "fullnet_it_schema_mysql_bootstrap";
    private const string SqlServerBackupPath =
        "/var/opt/mssql/data/fullnet_it_schema_sql.bak";
    private const int CommandTimeoutSeconds = 180;

    private static readonly SemaphoreSlim SqlServerTemplateLock = new(1, 1);
    private static readonly SemaphoreSlim MySqlTemplateLock = new(1, 1);
    private static bool _sqlServerTemplateReady;
    private static bool _mySqlTemplateReady;

    internal static bool IsEnabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("FULLNET_API_SCHEMA_TEMPLATE"),
            "0",
            StringComparison.Ordinal);

    /// <summary>
    /// 仅无用户对象的空库可用已迁移模板覆盖；没有 Journal 的历史库也必须执行真实迁移。
    /// </summary>
    public static async Task<bool> TryHydrateEmptyDatabaseAsync(
        DatabaseProvider provider,
        string targetConnectionString,
        Func<string, CancellationToken, Task> materializeTemplate,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (!await IsEmptyAsync(provider, targetConnectionString, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        if (provider == DatabaseProvider.SqlServer)
        {
            await EnsureSqlServerTemplateAsync(materializeTemplate, cancellationToken)
                .ConfigureAwait(false);
            await CloneSqlServerTemplateAsync(targetConnectionString, cancellationToken)
                .ConfigureAwait(false);
            SqlConnection.ClearAllPools();
            return true;
        }

        await EnsureMySqlTemplateAsync(materializeTemplate, cancellationToken)
            .ConfigureAwait(false);
        await CloneMySqlTemplateAsync(targetConnectionString, cancellationToken)
            .ConfigureAwait(false);
        MySqlConnection.ClearAllPools();
        return true;
    }

    private static async Task EnsureSqlServerTemplateAsync(
        Func<string, CancellationToken, Task> materializeTemplate,
        CancellationToken cancellationToken)
    {
        if (_sqlServerTemplateReady)
        {
            return;
        }

        await SqlServerTemplateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sqlServerTemplateReady)
            {
                return;
            }

            var master = await SharedDatabaseFixture
                .GetSqlServerMasterConnectionStringAsync()
                .ConfigureAwait(false);
            await using var admin = new SqlConnection(master);
            await admin.OpenAsync(cancellationToken).ConfigureAwait(false);
            var templateCs = BuildSqlServerConnectionString(master, SqlServerTemplateDatabase);
            if (await SqlServerDatabaseExistsAsync(admin, SqlServerTemplateDatabase, cancellationToken)
                    .ConfigureAwait(false)
                && !await IsReusableSchemaTemplateAsync(
                        DatabaseProvider.SqlServer,
                        templateCs,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                await DropSqlServerDatabaseAsync(
                        admin,
                        SqlServerTemplateDatabase,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!await SqlServerDatabaseExistsAsync(
                    admin,
                    SqlServerTemplateDatabase,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                await admin.ExecuteAsync(
                        TimeoutCommand(cancellationToken,
                            $"CREATE DATABASE {SharedDatabaseFixture.QuoteSqlServerIdent(SqlServerTemplateDatabase)};"))
                    .ConfigureAwait(false);
                await materializeTemplate(templateCs, cancellationToken).ConfigureAwait(false);
            }

            await BackupSqlServerTemplateAsync(admin, cancellationToken).ConfigureAwait(false);
            _sqlServerTemplateReady = true;
        }
        finally
        {
            SqlServerTemplateLock.Release();
        }
    }

    private static async Task EnsureMySqlTemplateAsync(
        Func<string, CancellationToken, Task> materializeTemplate,
        CancellationToken cancellationToken)
    {
        if (_mySqlTemplateReady)
        {
            return;
        }

        await MySqlTemplateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_mySqlTemplateReady)
            {
                return;
            }

            var root = await SharedDatabaseFixture
                .GetMySqlRootConnectionStringAsync()
                .ConfigureAwait(false);
            await using var admin = new MySqlConnection(root);
            await admin.OpenAsync(cancellationToken).ConfigureAwait(false);
            var templateCs = BuildMySqlConnectionString(root, MySqlTemplateDatabase);
            // 多个 dotnet test 进程共享同一 Testcontainers MySQL；进程内 Semaphore 不足以保护模板库创建。
            await AcquireMySqlTemplateBootstrapLockAsync(admin, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var exists = await MySqlDatabaseExistsAsync(
                        admin,
                        MySqlTemplateDatabase,
                        cancellationToken)
                    .ConfigureAwait(false);
                var reusable = exists && await IsReusableSchemaTemplateAsync(
                        DatabaseProvider.MySql,
                        templateCs,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!reusable)
                {
                    if (exists)
                    {
                        await admin.ExecuteAsync(
                                TimeoutCommand(
                                    cancellationToken,
                                    $"DROP DATABASE {SharedDatabaseFixture.QuoteMySqlIdent(MySqlTemplateDatabase)};"))
                            .ConfigureAwait(false);
                    }

                    await admin.ExecuteAsync(
                            TimeoutCommand(
                                cancellationToken,
                                $"CREATE DATABASE {SharedDatabaseFixture.QuoteMySqlIdent(MySqlTemplateDatabase)};"))
                        .ConfigureAwait(false);
                    await GrantMySqlTemplateAsync(admin, MySqlTemplateDatabase, cancellationToken)
                        .ConfigureAwait(false);
                    await materializeTemplate(templateCs, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await ReleaseMySqlTemplateBootstrapLockAsync(admin, cancellationToken)
                    .ConfigureAwait(false);
            }

            _mySqlTemplateReady = true;
        }
        finally
        {
            MySqlTemplateLock.Release();
        }
    }

    private static async Task CloneSqlServerTemplateAsync(
        string targetConnectionString,
        CancellationToken cancellationToken)
    {
        var targetName = SharedDatabaseFixture.GetSqlServerDatabaseName(targetConnectionString);
        var quotedTarget = SharedDatabaseFixture.QuoteSqlServerIdent(targetName);
        var master = await SharedDatabaseFixture
            .GetSqlServerMasterConnectionStringAsync()
            .ConfigureAwait(false);
        await using var admin = new SqlConnection(master);
        await admin.OpenAsync(cancellationToken).ConfigureAwait(false);
        await DropSqlServerDatabaseAsync(admin, targetName, cancellationToken)
            .ConfigureAwait(false);
        SqlConnection.ClearAllPools();

        var files = (await admin.QueryAsync<SqlBackupFile>(
                TimeoutCommand(
                    cancellationToken,
                    "RESTORE FILELISTONLY FROM DISK = @Path;",
                    new { Path = SqlServerBackupPath }))
            .ConfigureAwait(false)).ToArray();
        var move = string.Join(
            ", ",
            files.Select(file =>
            {
                var suffix = string.Equals(file.Type.Trim(), "L", StringComparison.OrdinalIgnoreCase)
                    ? "_log.ldf"
                    : $"_{file.FileId}.mdf";
                var escapedLogical = file.LogicalName.Replace("'", "''", StringComparison.Ordinal);
                return $"MOVE N'{escapedLogical}' TO N'/var/opt/mssql/data/{targetName}{suffix}'";
            }));
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken,
                    $"""
                    RESTORE DATABASE {quotedTarget}
                    FROM DISK = @Path
                    WITH REPLACE, {move};
                    """,
                    new { Path = SqlServerBackupPath }))
            .ConfigureAwait(false);
    }

    private static async Task CloneMySqlTemplateAsync(
        string targetConnectionString,
        CancellationToken cancellationToken)
    {
        var targetName = SharedDatabaseFixture.GetMySqlDatabaseName(targetConnectionString);
        var root = await SharedDatabaseFixture
            .GetMySqlRootConnectionStringAsync()
            .ConfigureAwait(false);
        await using var admin = new MySqlConnection(root);
        await admin.OpenAsync(cancellationToken).ConfigureAwait(false);
        await AcquireMySqlTemplateBootstrapLockAsync(admin, cancellationToken).ConfigureAwait(false);
        try
        {
            await CloneMySqlSchemaAsync(
                    admin,
                    MySqlTemplateDatabase,
                    targetName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await ReleaseMySqlTemplateBootstrapLockAsync(admin, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal static async Task CloneMySqlSchemaAsync(
        MySqlConnection admin,
        string sourceName,
        string targetName,
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("模板来源和目标库不得相同。");
        var quotedTarget = SharedDatabaseFixture.QuoteMySqlIdent(targetName);
        var quotedTemplate = SharedDatabaseFixture.QuoteMySqlIdent(sourceName);
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken, $"DROP DATABASE {quotedTarget}; CREATE DATABASE {quotedTarget};"))
            .ConfigureAwait(false);
        MySqlConnection.ClearAllPools();
        await GrantMySqlTemplateAsync(admin, targetName, cancellationToken).ConfigureAwait(false);
        // SHOW CREATE 中的未限定外键、触发器表名必须在目标库解析，不能绑定到模板库。
        await admin.ExecuteAsync(TimeoutCommand(cancellationToken, $"USE {quotedTarget};")).ConfigureAwait(false);

        var objects = (await admin.QueryAsync<(string Name, string Type)>(
                TimeoutCommand(cancellationToken,
                    """
                    SELECT TABLE_NAME AS Name, TABLE_TYPE AS Type
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = @Schema
                    ORDER BY CASE TABLE_TYPE WHEN 'BASE TABLE' THEN 0 ELSE 1 END, TABLE_NAME;
                    """,
                    new { Schema = sourceName }))
            .ConfigureAwait(false)).ToArray();
        await admin.ExecuteAsync(TimeoutCommand(cancellationToken, "SET FOREIGN_KEY_CHECKS = 0;")).ConfigureAwait(false);
        try
        {
            foreach (var obj in objects)
            {
                if (string.Equals(obj.Type, "BASE TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    await CloneMySqlTableAsync(
                            admin,
                            quotedTemplate,
                            quotedTarget,
                            sourceName,
                            obj.Name,
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                await CloneMySqlViewAsync(
                        admin,
                        quotedTarget,
                        sourceName,
                        obj.Name,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await admin.ExecuteAsync(TimeoutCommand(cancellationToken, "SET FOREIGN_KEY_CHECKS = 1;"))
                .ConfigureAwait(false);
        }

        // 先复制结构与数据再安装触发器，避免复制基线行时执行业务副作用。
        var triggers = (await admin.QueryAsync<string>(TimeoutCommand(cancellationToken,
            "SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = @Schema ORDER BY TRIGGER_NAME;",
            new { Schema = sourceName })).ConfigureAwait(false)).ToArray();
        foreach (var trigger in triggers)
        {
            var definition = await GetMySqlCreateStatementAsync(admin, "TRIGGER", sourceName, trigger, cancellationToken)
                .ConfigureAwait(false);
            await admin.ExecuteAsync(TimeoutCommand(cancellationToken,
                QualifyMySqlCreateStatement(definition, quotedTarget, "TRIGGER"))).ConfigureAwait(false);
        }
    }

    private static async Task CloneMySqlTableAsync(
        MySqlConnection admin,
        string quotedTemplate,
        string quotedTarget,
        string templateDatabase,
        string tableName,
        CancellationToken cancellationToken)
    {
        var quotedTable = SharedDatabaseFixture.QuoteMySqlIdent(tableName);
        var definition = await GetMySqlCreateStatementAsync(admin, "TABLE", templateDatabase, tableName, cancellationToken)
            .ConfigureAwait(false);
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken, QualifyMySqlCreateStatement(definition, quotedTarget, "TABLE")))
            .ConfigureAwait(false);
        var columns = (await admin.QueryAsync<string>(
                TimeoutCommand(
                    cancellationToken,
                    """
                    SELECT COLUMN_NAME
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = @Schema
                      AND TABLE_NAME = @Table
                      AND EXTRA NOT LIKE '%STORED GENERATED%'
                      AND EXTRA NOT LIKE '%VIRTUAL GENERATED%'
                    ORDER BY ORDINAL_POSITION;
                    """,
                    new { Schema = templateDatabase, Table = tableName }))
            .ConfigureAwait(false)).ToArray();
        if (columns.Length == 0)
        {
            return;
        }

        var columnList = string.Join(
            ", ",
            columns.Select(SharedDatabaseFixture.QuoteMySqlIdent));
        await admin.ExecuteAsync(
                TimeoutCommand(
                    cancellationToken,
                    $"""
                    INSERT INTO {quotedTarget}.{quotedTable} ({columnList})
                    SELECT {columnList} FROM {quotedTemplate}.{quotedTable};
                    """))
            .ConfigureAwait(false);
    }

    private static async Task CloneMySqlViewAsync(
        MySqlConnection admin,
        string quotedTarget,
        string templateDatabase,
        string viewName,
        CancellationToken cancellationToken)
    {
        var createSql = await GetMySqlCreateStatementAsync(
                admin,
                "VIEW",
                templateDatabase,
                viewName,
                cancellationToken)
            .ConfigureAwait(false);
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken, QualifyMySqlCreateStatement(createSql, quotedTarget, "VIEW")
                    .Replace(SharedDatabaseFixture.QuoteMySqlIdent(templateDatabase) + ".", quotedTarget + ".", StringComparison.Ordinal)))
            .ConfigureAwait(false);
    }

    private static async Task<string> GetMySqlCreateStatementAsync(
        MySqlConnection admin,
        string objectKind,
        string schemaName,
        string objectName,
        CancellationToken cancellationToken)
    {
        var quoted =
            $"{SharedDatabaseFixture.QuoteMySqlIdent(schemaName)}.{SharedDatabaseFixture.QuoteMySqlIdent(objectName)}";
        var row = await admin.QuerySingleAsync<dynamic>(
                TimeoutCommand(cancellationToken, $"SHOW CREATE {objectKind} {quoted};"))
            .ConfigureAwait(false);
        var dictionary = (IDictionary<string, object>)row;
        var key = dictionary.Keys.First(name =>
            name.StartsWith("Create ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "SQL Original Statement", StringComparison.OrdinalIgnoreCase));
        return Convert.ToString(dictionary[key], System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"无法读取 MySQL {objectKind} {objectName} 的定义。");
    }

    private static string QualifyMySqlCreateStatement(
        string createSql,
        string quotedTarget,
        string objectKind)
    {
        var marker = objectKind + " ";
        var index = createSql.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            throw new InvalidOperationException($"无法限定 MySQL {objectKind} 定义到目标库。");
        }

        var nameStart = index + marker.Length;
        var nameEnd = ReadMySqlQuotedIdentifierEnd(createSql, nameStart);
        var objectStart = nameStart;
        // SHOW CREATE VIEW 可能带源库限定；替换限定，避免拼出三段对象名。
        if (nameEnd < createSql.Length && createSql[nameEnd] == '.')
        {
            objectStart = nameEnd + 1;
            nameEnd = ReadMySqlQuotedIdentifierEnd(createSql, objectStart);
        }

        return createSql[..nameStart] + quotedTarget + "." + createSql[objectStart..nameEnd] + createSql[nameEnd..];
    }

    private static int ReadMySqlQuotedIdentifierEnd(string sql, int start)
    {
        if (start >= sql.Length || sql[start] != '`')
        {
            throw new InvalidOperationException("MySQL SHOW CREATE 返回了未限定的非标准对象名。");
        }

        for (var index = start + 1; index < sql.Length; index++)
        {
            if (sql[index] != '`') continue;
            if (index + 1 < sql.Length && sql[index + 1] == '`')
            {
                index++;
                continue;
            }

            return index + 1;
        }

        throw new InvalidOperationException("MySQL SHOW CREATE 对象名缺少结束引号。");
    }

    private static async Task BackupSqlServerTemplateAsync(
        SqlConnection admin,
        CancellationToken cancellationToken)
    {
        var quoted = SharedDatabaseFixture.QuoteSqlServerIdent(SqlServerTemplateDatabase);
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken,
                    $"""
                    BACKUP DATABASE {quoted}
                    TO DISK = @Path
                    WITH COPY_ONLY, INIT, SKIP;
                    """,
                    new { Path = SqlServerBackupPath }))
            .ConfigureAwait(false);
    }

    private static async Task DropSqlServerDatabaseAsync(
        SqlConnection admin,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var quoted = SharedDatabaseFixture.QuoteSqlServerIdent(databaseName);
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken,
                    $"""
                    ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quoted};
                    """))
            .ConfigureAwait(false);
    }

    private static async Task GrantMySqlTemplateAsync(
        MySqlConnection admin,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var quoted = SharedDatabaseFixture.QuoteMySqlIdent(databaseName);
        await admin.ExecuteAsync(
                TimeoutCommand(cancellationToken,
                    $"""
                    GRANT ALL PRIVILEGES ON {quoted}.*
                    TO '{SharedDatabaseFixture.MySqlApplicationUserName}'@'%';
                    FLUSH PRIVILEGES;
                    """))
            .ConfigureAwait(false);
    }

    private static async Task<bool> SqlServerDatabaseExistsAsync(
        SqlConnection admin,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var id = await admin.ExecuteScalarAsync<int>(
                TimeoutCommand(cancellationToken,
                    "SELECT ISNULL(DB_ID(@Name), 0);",
                    new { Name = databaseName }))
            .ConfigureAwait(false);
        return id != 0;
    }

    private static async Task<bool> MySqlDatabaseExistsAsync(
        MySqlConnection admin,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var count = await admin.ExecuteScalarAsync<long>(
                TimeoutCommand(cancellationToken,
                    """
                    SELECT COUNT(*)
                    FROM information_schema.SCHEMATA
                    WHERE SCHEMA_NAME = @Name;
                    """,
                    new { Name = databaseName }))
            .ConfigureAwait(false);
        return count > 0;
    }

    private static async Task<bool> IsReusableSchemaTemplateAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        if (!await JournalMatchesAsync(provider, connectionString, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        return !await ContainsBootstrapDataAsync(provider, connectionString, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> JournalMatchesAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var expected = CountEmbeddedScripts(provider);
        try
        {
            var actual = await ReadJournalCountAsync(provider, connectionString, cancellationToken)
                .ConfigureAwait(false);
            return actual == expected;
        }
        catch (Exception exception) when (IsMissingJournalException(exception))
        {
            return false;
        }
    }

    private static async Task<bool> ContainsBootstrapDataAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            if (provider == DatabaseProvider.SqlServer)
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var count = await connection.ExecuteScalarAsync<int>(
                        TimeoutCommand(cancellationToken, "SELECT COUNT(*) FROM dbo.fn_identity_user;"))
                    .ConfigureAwait(false);
                return count > 0;
            }

            await using var mySql = new MySqlConnection(connectionString);
            await mySql.OpenAsync(cancellationToken).ConfigureAwait(false);
            var mySqlCount = await mySql.ExecuteScalarAsync<long>(
                    TimeoutCommand(cancellationToken, "SELECT COUNT(*) FROM fn_identity_user;"))
                .ConfigureAwait(false);
            return mySqlCount > 0;
        }
        catch (Exception exception) when (IsMissingJournalException(exception))
        {
            return true;
        }
    }

    private static int CountEmbeddedScripts(DatabaseProvider provider)
    {
        var segment = provider == DatabaseProvider.SqlServer
            ? ".Migrations.SqlServer."
            : ".Migrations.MySql.";
        return typeof(DbUpMigrationRunner).Assembly
            .GetManifestResourceNames()
            .Count(name => name.Contains(segment, StringComparison.Ordinal));
    }

    private static async Task<int> ReadJournalCountAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await connection.ExecuteScalarAsync<int>(
                    TimeoutCommand(cancellationToken, "SELECT COUNT(*) FROM dbo.SchemaVersions;"))
                .ConfigureAwait(false);
        }

        await using var mySql = new MySqlConnection(connectionString);
        await mySql.OpenAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(
            await mySql.ExecuteScalarAsync<long>(
                    TimeoutCommand(cancellationToken, "SELECT COUNT(*) FROM SchemaVersions;"))
                .ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsMissingJournalException(Exception exception)
    {
        return exception is SqlException or MySqlException;
    }

    private static async Task<bool> IsEmptyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var objectCount = await connection.ExecuteScalarAsync<int>(
                    TimeoutCommand(cancellationToken, """
                        SELECT
                            (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0)
                            + (SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 0 AND is_ms_shipped = 0)
                            + (SELECT COUNT(*) FROM sys.types WHERE is_user_defined = 1)
                            + (SELECT COUNT(*) FROM sys.schemas WHERE name NOT IN
                                ('dbo', 'guest', 'sys', 'INFORMATION_SCHEMA', 'db_owner', 'db_accessadmin',
                                 'db_securityadmin', 'db_ddladmin', 'db_backupoperator', 'db_datareader',
                                 'db_datawriter', 'db_denydatareader', 'db_denydatawriter'))
                            + (SELECT COUNT(*) FROM sys.database_principals WHERE principal_id > 4 AND is_fixed_role = 0)
                            + (SELECT COUNT(*) FROM sys.xml_schema_collections WHERE xml_collection_id > 1)
                            + (SELECT COUNT(*) FROM sys.assemblies WHERE is_user_defined = 1)
                            + (SELECT COUNT(*) FROM sys.database_scoped_credentials)
                            + (SELECT COUNT(*) FROM sys.external_data_sources)
                            + (SELECT COUNT(*) FROM sys.external_file_formats)
                            + (SELECT COUNT(*) FROM sys.symmetric_keys)
                            + (SELECT COUNT(*) FROM sys.asymmetric_keys)
                            + (SELECT COUNT(*) FROM sys.certificates);
                        """))
                .ConfigureAwait(false);
            return objectCount == 0;
        }

        await using var mySql = new MySqlConnection(connectionString);
        await mySql.OpenAsync(cancellationToken).ConfigureAwait(false);
        var count = await mySql.ExecuteScalarAsync<long>(
                TimeoutCommand(cancellationToken,
                    """
                    SELECT
                        (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE())
                        + (SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = DATABASE())
                        + (SELECT COUNT(*) FROM information_schema.EVENTS WHERE EVENT_SCHEMA = DATABASE());
                    """))
            .ConfigureAwait(false);
        return count == 0;
    }

    private static string BuildSqlServerConnectionString(string masterConnectionString, string databaseName)
    {
        return new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    private static string BuildMySqlConnectionString(string rootConnectionString, string databaseName)
    {
        return new MySqlConnectionStringBuilder(rootConnectionString)
        {
            UserID = SharedDatabaseFixture.MySqlApplicationUserName,
            Password = SharedDatabaseFixture.MySqlRootPassword,
            Database = databaseName,
        }.ConnectionString;
    }

    /// <summary>
    /// 映射 RESTORE FILELISTONLY 的列名。不能用位置元组，第二列是 PhysicalName 不是 Type。
    /// </summary>
    private sealed class SqlBackupFile
    {
        public string LogicalName { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public int FileId { get; init; }
    }

    private static async Task AcquireMySqlTemplateBootstrapLockAsync(
        MySqlConnection admin,
        CancellationToken cancellationToken)
    {
        var lockAcquired = await admin.ExecuteScalarAsync<int?>(
                TimeoutCommand(
                    cancellationToken,
                    "SELECT GET_LOCK(@Name, @TimeoutSeconds);",
                    new { Name = MySqlTemplateBootstrapLockName, TimeoutSeconds = 300 }))
            .ConfigureAwait(false);
        if (lockAcquired != 1)
        {
            throw new InvalidOperationException(
                "无法在 300 秒内获取 MySQL schema 模板引导锁。");
        }
    }

    private static Task ReleaseMySqlTemplateBootstrapLockAsync(
        MySqlConnection admin,
        CancellationToken cancellationToken) =>
        admin.ExecuteScalarAsync<int?>(
            TimeoutCommand(
                cancellationToken,
                "SELECT RELEASE_LOCK(@Name);",
                new { Name = MySqlTemplateBootstrapLockName }));

    private static CommandDefinition TimeoutCommand(
        CancellationToken cancellationToken,
        string sql,
        object? parameters = null) =>
        new(sql, parameters, commandTimeout: CommandTimeoutSeconds, cancellationToken: cancellationToken);
}
