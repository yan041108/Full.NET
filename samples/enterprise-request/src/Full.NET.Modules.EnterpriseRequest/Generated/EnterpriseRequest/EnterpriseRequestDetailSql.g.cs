using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class EnterpriseRequestDetailSql
{
    /// <summary>按主键查询单条实体，含租户与软删除过滤。</summary>
    public const string FindById = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestId,
            LineNumber,
            ItemDescription,
            Quantity,
            UnitPrice,
            LineAmount,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request_line
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>统计实体总数，含租户与软删除过滤。</summary>
    public const string Count = """
        SELECT COUNT(1)
        FROM demo_enterprise_request_enterprise_request_line
        WHERE TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>SQL Server 分页列表查询，按 Id 升序，含租户与软删除过滤。</summary>
    public const string ListSqlServer = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestId,
            LineNumber,
            ItemDescription,
            Quantity,
            UnitPrice,
            LineAmount,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request_line
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
        ORDER BY Id
        OFFSET @Offset ROWS
        FETCH NEXT @PageSize ROWS ONLY;
        """;

    /// <summary>MySQL 分页列表查询，按 Id 升序，含租户与软删除过滤。</summary>
    public const string ListMySql = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestId,
            LineNumber,
            ItemDescription,
            Quantity,
            UnitPrice,
            LineAmount,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request_line
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
        ORDER BY Id
        LIMIT @PageSize OFFSET @Offset;
        """;

    /// <summary>插入实体记录。</summary>
    public const string Insert = """
        INSERT INTO demo_enterprise_request_enterprise_request_line (
            Id, TenantId, OrganizationUnitId, RequestId, LineNumber, ItemDescription, Quantity, UnitPrice, LineAmount, Version, CreatedAtUtc, CreatedById, UpdatedAtUtc, UpdatedById, IsDeleted, DeletedAtUtc, DeletedById)
        VALUES (
            @Id, @TenantId, @OrganizationUnitId, @RequestId, @LineNumber, @ItemDescription, @Quantity, @UnitPrice, @LineAmount, @Version, @CreatedAtUtc, @CreatedById, @UpdatedAtUtc, @UpdatedById, @IsDeleted, @DeletedAtUtc, @DeletedById);
        """;

    /// <summary>更新实体字段，含租户、乐观锁版本与软删除过滤。</summary>
    public const string Update = """
        UPDATE demo_enterprise_request_enterprise_request_line
        SET RequestId = @RequestId,
            LineNumber = @LineNumber,
            ItemDescription = @ItemDescription,
            Quantity = @Quantity,
            UnitPrice = @UnitPrice,
            LineAmount = @LineAmount,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedById = @UpdatedById,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version
            AND IsDeleted = 0;
        """;

    /// <summary>软删除实体（置 IsDeleted=1），含租户、乐观锁版本与软删除过滤。</summary>
    public const string Delete = """
        UPDATE demo_enterprise_request_enterprise_request_line
        SET IsDeleted = 1,
            DeletedAtUtc = @DeletedAtUtc,
            DeletedById = @DeletedById,
            Version = Version + 1
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND Version = @Version
            AND IsDeleted = 0;
        """;

    public const string QueryCountSqlServer = """
        SELECT COUNT(1)
        FROM demo_enterprise_request_enterprise_request_line
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestId IS NULL OR RequestId = @Query_RequestId)
            AND (@Query_LineNumber IS NULL OR LineNumber = @Query_LineNumber)
            AND (@Query_ItemDescription IS NULL OR ItemDescription COLLATE Latin1_General_100_BIN2 LIKE @Query_ItemDescription ESCAPE '~')
            AND (@Query_Quantity IS NULL OR Quantity = @Query_Quantity)
            AND (@Query_UnitPrice IS NULL OR UnitPrice = @Query_UnitPrice)
            AND (@Query_LineAmount IS NULL OR LineAmount = @Query_LineAmount);
        """;
    public const string QueryListSqlServer = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestId,
            LineNumber,
            ItemDescription,
            Quantity,
            UnitPrice,
            LineAmount,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request_line
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestId IS NULL OR RequestId = @Query_RequestId)
            AND (@Query_LineNumber IS NULL OR LineNumber = @Query_LineNumber)
            AND (@Query_ItemDescription IS NULL OR ItemDescription COLLATE Latin1_General_100_BIN2 LIKE @Query_ItemDescription ESCAPE '~')
            AND (@Query_Quantity IS NULL OR Quantity = @Query_Quantity)
            AND (@Query_UnitPrice IS NULL OR UnitPrice = @Query_UnitPrice)
            AND (@Query_LineAmount IS NULL OR LineAmount = @Query_LineAmount)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'requestId' AND @CodegenDescending = 0 THEN RequestId END ASC,
            CASE WHEN @CodegenSortBy = 'requestId' AND @CodegenDescending = 1 THEN RequestId END DESC,
            CASE WHEN @CodegenSortBy = 'lineNumber' AND @CodegenDescending = 0 THEN LineNumber END ASC,
            CASE WHEN @CodegenSortBy = 'lineNumber' AND @CodegenDescending = 1 THEN LineNumber END DESC,
            CASE WHEN @CodegenSortBy = 'itemDescription' AND @CodegenDescending = 0 THEN ItemDescription COLLATE Latin1_General_100_BIN2 END ASC,
            CASE WHEN @CodegenSortBy = 'itemDescription' AND @CodegenDescending = 1 THEN ItemDescription COLLATE Latin1_General_100_BIN2 END DESC,
            CASE WHEN @CodegenSortBy = 'quantity' AND @CodegenDescending = 0 THEN Quantity END ASC,
            CASE WHEN @CodegenSortBy = 'quantity' AND @CodegenDescending = 1 THEN Quantity END DESC,
            CASE WHEN @CodegenSortBy = 'unitPrice' AND @CodegenDescending = 0 THEN UnitPrice END ASC,
            CASE WHEN @CodegenSortBy = 'unitPrice' AND @CodegenDescending = 1 THEN UnitPrice END DESC,
            CASE WHEN @CodegenSortBy = 'lineAmount' AND @CodegenDescending = 0 THEN LineAmount END ASC,
            CASE WHEN @CodegenSortBy = 'lineAmount' AND @CodegenDescending = 1 THEN LineAmount END DESC,
            Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """;

    public static readonly SqlStatement QueryCountSqlServerStatement = new(
        "enterprise_request.count_enterprise_request_lines_query.sql_server",
        QueryCountSqlServer, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public const string QueryCountMySql = """
        SELECT COUNT(1)
        FROM demo_enterprise_request_enterprise_request_line
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestId IS NULL OR RequestId = @Query_RequestId)
            AND (@Query_LineNumber IS NULL OR LineNumber = @Query_LineNumber)
            AND (@Query_ItemDescription IS NULL OR ItemDescription COLLATE utf8mb4_bin LIKE @Query_ItemDescription ESCAPE '~')
            AND (@Query_Quantity IS NULL OR Quantity = @Query_Quantity)
            AND (@Query_UnitPrice IS NULL OR UnitPrice = @Query_UnitPrice)
            AND (@Query_LineAmount IS NULL OR LineAmount = @Query_LineAmount);
        """;
    public const string QueryListMySql = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestId,
            LineNumber,
            ItemDescription,
            Quantity,
            UnitPrice,
            LineAmount,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request_line
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestId IS NULL OR RequestId = @Query_RequestId)
            AND (@Query_LineNumber IS NULL OR LineNumber = @Query_LineNumber)
            AND (@Query_ItemDescription IS NULL OR ItemDescription COLLATE utf8mb4_bin LIKE @Query_ItemDescription ESCAPE '~')
            AND (@Query_Quantity IS NULL OR Quantity = @Query_Quantity)
            AND (@Query_UnitPrice IS NULL OR UnitPrice = @Query_UnitPrice)
            AND (@Query_LineAmount IS NULL OR LineAmount = @Query_LineAmount)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'requestId' AND @CodegenDescending = 0 THEN RequestId END ASC,
            CASE WHEN @CodegenSortBy = 'requestId' AND @CodegenDescending = 1 THEN RequestId END DESC,
            CASE WHEN @CodegenSortBy = 'lineNumber' AND @CodegenDescending = 0 THEN LineNumber END ASC,
            CASE WHEN @CodegenSortBy = 'lineNumber' AND @CodegenDescending = 1 THEN LineNumber END DESC,
            CASE WHEN @CodegenSortBy = 'itemDescription' AND @CodegenDescending = 0 THEN ItemDescription COLLATE utf8mb4_bin END ASC,
            CASE WHEN @CodegenSortBy = 'itemDescription' AND @CodegenDescending = 1 THEN ItemDescription COLLATE utf8mb4_bin END DESC,
            CASE WHEN @CodegenSortBy = 'quantity' AND @CodegenDescending = 0 THEN Quantity END ASC,
            CASE WHEN @CodegenSortBy = 'quantity' AND @CodegenDescending = 1 THEN Quantity END DESC,
            CASE WHEN @CodegenSortBy = 'unitPrice' AND @CodegenDescending = 0 THEN UnitPrice END ASC,
            CASE WHEN @CodegenSortBy = 'unitPrice' AND @CodegenDescending = 1 THEN UnitPrice END DESC,
            CASE WHEN @CodegenSortBy = 'lineAmount' AND @CodegenDescending = 0 THEN LineAmount END ASC,
            CASE WHEN @CodegenSortBy = 'lineAmount' AND @CodegenDescending = 1 THEN LineAmount END DESC,
            Id ASC
        LIMIT @PageSize OFFSET @Offset;
        """;

    public static readonly SqlStatement QueryCountMySqlStatement = new(
        "enterprise_request.count_enterprise_request_lines_query.my_sql",
        QueryCountMySql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByIdStatement = new(
        "enterprise_request.find_enterprise_request_line_by_id",
        FindById,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountStatement = new(
        "enterprise_request.count_enterprise_request_lines",
        Count,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServerStatement = new(
        "enterprise_request.list_enterprise_request_lines.sql_server.rows",
        QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySqlStatement = new(
        "enterprise_request.list_enterprise_request_lines.my_sql.rows",
        QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServerStatement = new(
        "enterprise_request.list_enterprise_request_lines.sql_server",
        QueryCountSqlServer + "\n" + QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySqlStatement = new(
        "enterprise_request.list_enterprise_request_lines.my_sql",
        QueryCountMySql + "\n" + QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertStatement = new(
        "enterprise_request.insert_enterprise_request_line",
        Insert,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateStatement = new(
        "enterprise_request.update_enterprise_request_line",
        Update,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DeleteStatement = new(
        "enterprise_request.delete_enterprise_request_line",
        Delete,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
