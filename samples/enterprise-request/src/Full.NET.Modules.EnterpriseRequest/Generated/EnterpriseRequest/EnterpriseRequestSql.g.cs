using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class EnterpriseRequestSql
{
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

    public const string Count = """
        SELECT COUNT(1)
        FROM demo_enterprise_request_enterprise_request
        WHERE TenantId = @TenantId
            AND IsDeleted = 0;
        """;

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

    public const string Insert = """
        INSERT INTO demo_enterprise_request_enterprise_request (
            Id, TenantId, OrganizationUnitId, RequestNumber, Title, Status, TotalAmount, ApplicantUserId, Version, CreatedAtUtc, CreatedById, UpdatedAtUtc, UpdatedById, IsDeleted, DeletedAtUtc, DeletedById)
        VALUES (
            @Id, @TenantId, @OrganizationUnitId, @RequestNumber, @Title, @Status, @TotalAmount, @ApplicantUserId, @Version, @CreatedAtUtc, @CreatedById, @UpdatedAtUtc, @UpdatedById, @IsDeleted, @DeletedAtUtc, @DeletedById);
        """;

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
        ListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ListMySqlStatement = new(
        "enterprise_request.list_enterprise_requests.my_sql.rows",
        ListMySql,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServerStatement = new(
        "enterprise_request.list_enterprise_requests.sql_server",
        Count + "\n" + ListSqlServer,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySqlStatement = new(
        "enterprise_request.list_enterprise_requests.my_sql",
        Count + "\n" + ListMySql,
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
