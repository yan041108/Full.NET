namespace Full.NET.DatabaseDiagnostics.Cli;

/// <summary>有界诊断查询目录；不读取业务行、SQL 文本、默认值或锁键值。</summary>
internal static class DiagnosticQueries
{
    /// <summary>按已校验的提供程序与检查名选择固定查询，不拼接用户输入。</summary>
    public static string Get(string provider, string check) => (provider, check) switch
    {
        ("SqlServer", "connection") => """
            SELECT CONVERT(varchar(32), SERVERPROPERTY('ProductVersion')) AS Version,
                   DB_NAME() AS DatabaseName,
                   CONVERT(varchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS Collation,
                   IS_SRVROLEMEMBER('sysadmin') AS IsServerAdmin,
                   HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS CanViewDefinition;
            """,
        ("MySql", "connection") => """
            SELECT VERSION() AS Version, DATABASE() AS DatabaseName,
                   @@collation_database AS Collation, @@session.time_zone AS SessionTimeZone,
                   @@performance_schema AS PerformanceSchemaEnabled;
            """,
        ("SqlServer", "catalog") => """
            SELECT TOP (101) t.name AS TableName, c.name AS ColumnName, ty.name AS DataType,
                   c.max_length AS MaxLengthBytes, c.precision AS NumericPrecision,
                   c.scale AS NumericScale, c.is_nullable AS IsNullable
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = 'dbo' AND t.is_ms_shipped = 0 AND (@TableName IS NULL OR t.name = @TableName)
            ORDER BY t.name, c.column_id;
            """,
        ("MySql", "catalog") => """
            SELECT c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName, c.DATA_TYPE AS DataType,
                   c.CHARACTER_OCTET_LENGTH AS MaxLengthBytes, c.NUMERIC_PRECISION AS NumericPrecision,
                   c.NUMERIC_SCALE AS NumericScale, c.IS_NULLABLE AS IsNullable
            FROM information_schema.COLUMNS c
            JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
            WHERE c.TABLE_SCHEMA = DATABASE() AND t.TABLE_TYPE = 'BASE TABLE'
              AND (@TableName IS NULL OR c.TABLE_NAME = @TableName)
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION LIMIT 101;
            """,
        ("SqlServer", "indexes") => """
            SELECT TOP (101) t.name AS TableName, i.name AS IndexName, c.name AS ColumnName,
                   i.is_unique AS IsUnique, i.is_primary_key AS IsPrimaryKey,
                   ic.key_ordinal AS KeyOrdinal, ic.is_included_column AS IsIncludedColumn
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.indexes i ON i.object_id = t.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE s.name = 'dbo' AND t.is_ms_shipped = 0 AND (@TableName IS NULL OR t.name = @TableName)
            ORDER BY t.name, i.name, ic.index_column_id;
            """,
        ("MySql", "indexes") => """
            SELECT TABLE_NAME AS TableName, INDEX_NAME AS IndexName, COLUMN_NAME AS ColumnName,
                   NON_UNIQUE AS NonUnique, SEQ_IN_INDEX AS KeyOrdinal, INDEX_TYPE AS IndexType
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND (@TableName IS NULL OR TABLE_NAME = @TableName)
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX LIMIT 101;
            """,
        // 无查看权限时 DMV 可能只返回自己的请求；显式失败避免把不可见误判为无阻塞。
        ("SqlServer", "blocking") => """
            IF COALESCE(HAS_PERMS_BY_NAME(NULL, NULL,
                CASE WHEN CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) >= 16
                     THEN 'VIEW SERVER PERFORMANCE STATE' ELSE 'VIEW SERVER STATE' END), 0) <> 1
                THROW 50001, 'diagnostics_permission_required', 1;
            SELECT TOP (101) session_id AS SessionId, blocking_session_id AS BlockingSessionId,
                   status AS Status, wait_type AS WaitType, wait_time AS WaitMilliseconds
            FROM sys.dm_exec_requests
            WHERE database_id = DB_ID() AND session_id <> @@SPID AND blocking_session_id <> 0
            ORDER BY wait_time DESC, session_id;
            """,
        ("MySql", "blocking") => """
            SELECT w.REQUESTING_THREAD_ID AS RequestingThreadId,
                   w.BLOCKING_THREAD_ID AS BlockingThreadId, l.OBJECT_NAME AS TableName,
                   l.INDEX_NAME AS IndexName, l.LOCK_TYPE AS LockType, l.LOCK_MODE AS LockMode
            FROM performance_schema.data_lock_waits w
            JOIN performance_schema.data_locks l
              ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
            WHERE l.OBJECT_SCHEMA = DATABASE()
            ORDER BY w.REQUESTING_THREAD_ID LIMIT 101;
            """,
        _ => throw new ArgumentException()
    };
}
