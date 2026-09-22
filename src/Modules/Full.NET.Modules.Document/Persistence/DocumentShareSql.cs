using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Host 文档分享的 Dapper SQL 语句集。所有语句均使用 SqlDataScope.HostOnly；
/// ShareCode 由密码学安全随机源生成，口令以 PBKDF2 不可逆哈希存储，投影永不包含明文口令。
/// </summary>
internal static class DocumentShareSql
{
    /// <summary>分享投影字段；包含 PasswordHash 但服务层 Map 时强制置空，确保响应永不回显。</summary>
    private const string Projection = """
        Id, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
        PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version
        """;

    private const string PageFilterWhere = """
        WHERE TenantId IS NULL
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
          AND (@ShareCodePattern IS NULL OR ShareCode LIKE @ShareCodePattern)
          AND (@DocumentIdPattern IS NULL OR CAST(DocumentId AS VARCHAR(36)) LIKE @DocumentIdPattern)
          AND (@ApplyExpiredOnly = 0 OR ExpireTime < @Now)
          AND (@ApplyActiveOnly = 0 OR (ExpireTime >= @Now AND IsEnabled = 1))
          AND (@MinAccessCount IS NULL OR AccessCount >= @MinAccessCount)
          AND (@MaxAccessCount IS NULL OR AccessCount <= @MaxAccessCount)
        """;

    private const string PageFilterWhereMySql = """
        WHERE TenantId IS NULL
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
          AND (@ShareCodePattern IS NULL OR ShareCode LIKE @ShareCodePattern)
          AND (@DocumentIdPattern IS NULL OR CAST(DocumentId AS CHAR(36)) LIKE @DocumentIdPattern)
          AND (@ApplyExpiredOnly = 0 OR ExpireTime < @Now)
          AND (@ApplyActiveOnly = 0 OR (ExpireTime >= @Now AND IsEnabled = 1))
          AND (@MinAccessCount IS NULL OR AccessCount >= @MinAccessCount)
          AND (@MaxAccessCount IS NULL OR AccessCount <= @MaxAccessCount)
        """;

    /// <summary>
    /// 分享分页（SQL Server）：可选筛选 + 白名单排序列，先 COUNT 再分页。
    /// <paramref name="orderBySql"/> 必须由调用方通过 <see cref="ResolvePageOrderBy"/> 生成。
    /// </summary>
    public static SqlStatement BuildPageSqlServer(string orderBySql) =>
        new(
            $"document.host_share.page.sql_server.{orderBySql}",
            $$"""
            SELECT COUNT(1)
            FROM fn_document_share
            {{PageFilterWhere}};

            SELECT {{Projection}}
            FROM fn_document_share
            {{PageFilterWhere}}
            ORDER BY {{orderBySql}}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            SqlDataScope.HostOnly);

    /// <summary>分享分页（MySQL）：与 SQL Server 版本语义等价。</summary>
    public static SqlStatement BuildPageMySql(string orderBySql) =>
        new(
            $"document.host_share.page.my_sql.{orderBySql}",
            $$"""
            SELECT COUNT(1)
            FROM fn_document_share
            {{PageFilterWhereMySql}};

            SELECT {{Projection}}
            FROM fn_document_share
            {{PageFilterWhereMySql}}
            ORDER BY {{orderBySql}}
            LIMIT @PageSize OFFSET @Offset
            """,
            SqlDataScope.HostOnly);

    /// <summary>将 API sortBy/sortDir 解析为 SQL ORDER BY 片段（仅允许固定列名）。</summary>
    public static string ResolvePageOrderBy(string? sortBy, string? sortDir)
    {
        var normalized = sortBy?.Trim().ToLowerInvariant();
        var column = normalized switch
        {
            "accesscount" or "access_count" => "AccessCount",
            "expiretime" or "expire_time" => "ExpireTime",
            "createdatutc" or "created_at_utc" => "CreatedAtUtc",
            _ => "CreatedAtUtc",
        };

        var ascending = sortDir?.Trim().Equals("asc", StringComparison.OrdinalIgnoreCase) == true;
        var direction = ascending ? "ASC" : "DESC";
        return $"{column} {direction}, Id {direction}";
    }

    /// <summary>按 Id 查找分享；用于管理端读取与原子计数后回读。</summary>
    public static readonly SqlStatement FindById = new(
        "document.host_share.find_by_id",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_share
        WHERE Id = @Id AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 ShareCode 查找分享；用于匿名访问入口。ShareCode 等价于凭据，
    /// 查询失败与成功响应必须避免可区分的耗时差异以防止存在性侧信道。
    /// </summary>
    public static readonly SqlStatement FindByCode = new(
        "document.host_share.find_by_code",
        $$"""
        SELECT {{Projection}}
        FROM fn_document_share
        WHERE ShareCode = @ShareCode AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 新建分享；ShareCode 由调用方使用 RandomNumberGenerator 生成，
    /// PasswordHash 已经过 PBKDF2 哈希（无明文口令），AccessCount 初始为 0，IsEnabled 初始为 1。
    /// </summary>
    public static readonly SqlStatement Insert = new(
        "document.host_share.insert",
        """
        INSERT INTO fn_document_share
            (Id, TenantId, DocumentId, ShareCode, CreatedAtUtc, ExpireTime,
             PasswordHash, MaxAccessCount, AccessCount, IsEnabled, Version)
        VALUES
            (@Id, NULL, @DocumentId, @ShareCode, @CreatedAtUtc, @ExpireTime,
             @PasswordHash, @MaxAccessCount, 0, 1, @Version)
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 切换分享启停状态；WHERE 含 Version 乐观并发校验，受影响行数为 0 表示版本冲突。
    /// 不修改访问计数与过期时间，仅切换 IsEnabled 标志。
    /// </summary>
    public static readonly SqlStatement UpdateStatus = new(
        "document.host_share.update_status",
        """
        UPDATE fn_document_share
        SET IsEnabled = @IsEnabled,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 原子消费一次匿名访问：在单条 UPDATE 内同时校验 IsEnabled、ExpireTime 与 MaxAccessCount，
    /// 并自增 AccessCount。受影响行数为 1 表示消费成功；为 0 表示分享不存在、已禁用、已过期或达到访问上限，
    /// 上层必须重新查询区分具体原因。该语句是访问计数并发正确性的唯一权威边界，
    /// 禁止在内存层做 MaxAccessCount 预检后再 UPDATE——会引入 TOCTOU 漏洞。
    /// </summary>
    public static readonly SqlStatement TryConsumeAccess = new(
        "document.host_share.try_consume_access",
        """
        UPDATE fn_document_share
        SET AccessCount = AccessCount + 1,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantId IS NULL
          AND IsEnabled = 1
          AND ExpireTime >= @Now
          AND (MaxAccessCount IS NULL OR AccessCount < MaxAccessCount)
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}
