using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class EnterpriseRequestSql
{
    /// <summary>按主键查询单条实体，含租户与软删除过滤。</summary>
    public const string FindById = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestNumber,
            Title,
            Status,
            TotalAmount,
            ApplicantUserId,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request
        WHERE Id = @Id
            AND TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>统计实体总数，含租户与软删除过滤。</summary>
    public const string Count = """
        SELECT COUNT(1)
        FROM demo_enterprise_request_enterprise_request
        WHERE TenantId = @TenantId
            AND IsDeleted = 0;
        """;

    /// <summary>SQL Server 分页列表查询，按 Id 升序，含租户与软删除过滤。</summary>
    public const string ListSqlServer = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestNumber,
            Title,
            Status,
            TotalAmount,
            ApplicantUserId,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request
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
            RequestNumber,
            Title,
            Status,
            TotalAmount,
            ApplicantUserId,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
        ORDER BY Id
        LIMIT @PageSize OFFSET @Offset;
        """;

    /// <summary>插入实体记录。</summary>
    public const string Insert = """
        INSERT INTO demo_enterprise_request_enterprise_request (
            Id, TenantId, OrganizationUnitId, RequestNumber, Title, Status, TotalAmount, ApplicantUserId, Version, CreatedAtUtc, CreatedById, UpdatedAtUtc, UpdatedById, IsDeleted, DeletedAtUtc, DeletedById)
        VALUES (
            @Id, @TenantId, @OrganizationUnitId, @RequestNumber, @Title, @Status, @TotalAmount, @ApplicantUserId, @Version, @CreatedAtUtc, @CreatedById, @UpdatedAtUtc, @UpdatedById, @IsDeleted, @DeletedAtUtc, @DeletedById);
        """;

    /// <summary>更新实体字段，含租户、乐观锁版本与软删除过滤。</summary>
    public const string Update = """
        UPDATE demo_enterprise_request_enterprise_request
        SET RequestNumber = @RequestNumber,
            Title = @Title,
            Status = @Status,
            TotalAmount = @TotalAmount,
            ApplicantUserId = @ApplicantUserId,
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
        UPDATE demo_enterprise_request_enterprise_request
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
        FROM demo_enterprise_request_enterprise_request
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestNumber IS NULL OR RequestNumber COLLATE Latin1_General_100_BIN2 LIKE @Query_RequestNumber ESCAPE '~')
            AND (@Query_Title IS NULL OR Title COLLATE Latin1_General_100_BIN2 LIKE @Query_Title ESCAPE '~')
            AND (@Query_Status IS NULL OR Status COLLATE Latin1_General_100_BIN2 LIKE @Query_Status ESCAPE '~')
            AND (@Query_TotalAmount IS NULL OR TotalAmount = @Query_TotalAmount)
            AND (@Query_ApplicantUserId IS NULL OR ApplicantUserId = @Query_ApplicantUserId);
        """;
    public const string QueryListSqlServer = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestNumber,
            Title,
            Status,
            TotalAmount,
            ApplicantUserId,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestNumber IS NULL OR RequestNumber COLLATE Latin1_General_100_BIN2 LIKE @Query_RequestNumber ESCAPE '~')
            AND (@Query_Title IS NULL OR Title COLLATE Latin1_General_100_BIN2 LIKE @Query_Title ESCAPE '~')
            AND (@Query_Status IS NULL OR Status COLLATE Latin1_General_100_BIN2 LIKE @Query_Status ESCAPE '~')
            AND (@Query_TotalAmount IS NULL OR TotalAmount = @Query_TotalAmount)
            AND (@Query_ApplicantUserId IS NULL OR ApplicantUserId = @Query_ApplicantUserId)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'requestNumber' AND @CodegenDescending = 0 THEN RequestNumber COLLATE Latin1_General_100_BIN2 END ASC,
            CASE WHEN @CodegenSortBy = 'requestNumber' AND @CodegenDescending = 1 THEN RequestNumber COLLATE Latin1_General_100_BIN2 END DESC,
            CASE WHEN @CodegenSortBy = 'title' AND @CodegenDescending = 0 THEN Title COLLATE Latin1_General_100_BIN2 END ASC,
            CASE WHEN @CodegenSortBy = 'title' AND @CodegenDescending = 1 THEN Title COLLATE Latin1_General_100_BIN2 END DESC,
            CASE WHEN @CodegenSortBy = 'status' AND @CodegenDescending = 0 THEN Status COLLATE Latin1_General_100_BIN2 END ASC,
            CASE WHEN @CodegenSortBy = 'status' AND @CodegenDescending = 1 THEN Status COLLATE Latin1_General_100_BIN2 END DESC,
            CASE WHEN @CodegenSortBy = 'totalAmount' AND @CodegenDescending = 0 THEN TotalAmount END ASC,
            CASE WHEN @CodegenSortBy = 'totalAmount' AND @CodegenDescending = 1 THEN TotalAmount END DESC,
            CASE WHEN @CodegenSortBy = 'applicantUserId' AND @CodegenDescending = 0 THEN ApplicantUserId END ASC,
            CASE WHEN @CodegenSortBy = 'applicantUserId' AND @CodegenDescending = 1 THEN ApplicantUserId END DESC,
            Id ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """;

    public static readonly SqlStatement QueryCountSqlServerStatement = new(
        "enterprise_request.count_enterprise_requests_query.sql_server",
        QueryCountSqlServer, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public const string QueryCountMySql = """
        SELECT COUNT(1)
        FROM demo_enterprise_request_enterprise_request
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestNumber IS NULL OR RequestNumber COLLATE utf8mb4_bin LIKE @Query_RequestNumber ESCAPE '~')
            AND (@Query_Title IS NULL OR Title COLLATE utf8mb4_bin LIKE @Query_Title ESCAPE '~')
            AND (@Query_Status IS NULL OR Status COLLATE utf8mb4_bin LIKE @Query_Status ESCAPE '~')
            AND (@Query_TotalAmount IS NULL OR TotalAmount = @Query_TotalAmount)
            AND (@Query_ApplicantUserId IS NULL OR ApplicantUserId = @Query_ApplicantUserId);
        """;
    public const string QueryListMySql = """
        SELECT
            Id,
            TenantId,
            OrganizationUnitId,
            RequestNumber,
            Title,
            Status,
            TotalAmount,
            ApplicantUserId,
            Version,
            CreatedAtUtc,
            CreatedById,
            UpdatedAtUtc,
            UpdatedById,
            IsDeleted,
            DeletedAtUtc,
            DeletedById
        FROM demo_enterprise_request_enterprise_request
        WHERE 1 = 1
            AND TenantId = @TenantId
            AND IsDeleted = 0
            AND (@Query_RequestNumber IS NULL OR RequestNumber COLLATE utf8mb4_bin LIKE @Query_RequestNumber ESCAPE '~')
            AND (@Query_Title IS NULL OR Title COLLATE utf8mb4_bin LIKE @Query_Title ESCAPE '~')
            AND (@Query_Status IS NULL OR Status COLLATE utf8mb4_bin LIKE @Query_Status ESCAPE '~')
            AND (@Query_TotalAmount IS NULL OR TotalAmount = @Query_TotalAmount)
            AND (@Query_ApplicantUserId IS NULL OR ApplicantUserId = @Query_ApplicantUserId)
        ORDER BY CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 0 THEN Id END ASC,
            CASE WHEN @CodegenSortBy = 'id' AND @CodegenDescending = 1 THEN Id END DESC,
            CASE WHEN @CodegenSortBy = 'requestNumber' AND @CodegenDescending = 0 THEN RequestNumber COLLATE utf8mb4_bin END ASC,
            CASE WHEN @CodegenSortBy = 'requestNumber' AND @CodegenDescending = 1 THEN RequestNumber COLLATE utf8mb4_bin END DESC,
            CASE WHEN @CodegenSortBy = 'title' AND @CodegenDescending = 0 THEN Title COLLATE utf8mb4_bin END ASC,
            CASE WHEN @CodegenSortBy = 'title' AND @CodegenDescending = 1 THEN Title COLLATE utf8mb4_bin END DESC,
            CASE WHEN @CodegenSortBy = 'status' AND @CodegenDescending = 0 THEN Status COLLATE utf8mb4_bin END ASC,
            CASE WHEN @CodegenSortBy = 'status' AND @CodegenDescending = 1 THEN Status COLLATE utf8mb4_bin END DESC,
            CASE WHEN @CodegenSortBy = 'totalAmount' AND @CodegenDescending = 0 THEN TotalAmount END ASC,
            CASE WHEN @CodegenSortBy = 'totalAmount' AND @CodegenDescending = 1 THEN TotalAmount END DESC,
            CASE WHEN @CodegenSortBy = 'applicantUserId' AND @CodegenDescending = 0 THEN ApplicantUserId END ASC,
            CASE WHEN @CodegenSortBy = 'applicantUserId' AND @CodegenDescending = 1 THEN ApplicantUserId END DESC,
            Id ASC
        LIMIT @PageSize OFFSET @Offset;
        """;

    public static readonly SqlStatement QueryCountMySqlStatement = new(
        "enterprise_request.count_enterprise_requests_query.my_sql",
        QueryCountMySql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByIdStatement = new(
        "enterprise_request.find_enterprise_request_by_id",
        FindById,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CountStatement = new(
        "enterprise_request.count_enterprise_requests",
        Count,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListSqlServerStatement = new(
        "enterprise_request.list_enterprise_requests.sql_server.rows",
        QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySqlStatement = new(
        "enterprise_request.list_enterprise_requests.my_sql.rows",
        QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServerStatement = new(
        "enterprise_request.list_enterprise_requests.sql_server",
        QueryCountSqlServer + "\n" + QueryListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySqlStatement = new(
        "enterprise_request.list_enterprise_requests.my_sql",
        QueryCountMySql + "\n" + QueryListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertStatement = new(
        "enterprise_request.insert_enterprise_request",
        Insert,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateStatement = new(
        "enterprise_request.update_enterprise_request",
        Update,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement DeleteStatement = new(
        "enterprise_request.delete_enterprise_request",
        Delete,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
