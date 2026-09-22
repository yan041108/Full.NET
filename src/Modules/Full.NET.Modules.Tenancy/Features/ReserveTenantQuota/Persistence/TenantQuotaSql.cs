using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;

internal static class TenantQuotaSql
{
    public static readonly SqlStatement FindMetric = new(
        "tenancy.quota.find_metric",
        """
        SELECT Id, TenantId, MetricCode, PeriodKey, LimitValue, UsedValue, ReservedValue, Version
        FROM fn_tenancy_quota_metric
        WHERE TenantId = @TenantId
          AND MetricCode = @MetricCode
          AND PeriodKey = @PeriodKey
        """,
        SqlDataScope.HostOnly);

    // 必须同时匹配预留的租户、指标和物理记录，不能退回当前周期。
    public static readonly SqlStatement FindBoundMetric = new(
        "tenancy.quota.find_bound_metric",
        """
        SELECT Id, TenantId, MetricCode, PeriodKey, LimitValue, UsedValue, ReservedValue, Version
        FROM fn_tenancy_quota_metric
        WHERE Id = @MetricId AND TenantId = @TenantId AND MetricCode = @MetricCode
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMetricsByTenant = new(
        "tenancy.quota.list_metrics_by_tenant",
        """
        SELECT Id, TenantId, MetricCode, PeriodKey, LimitValue, UsedValue, ReservedValue, Version
        FROM fn_tenancy_quota_metric
        WHERE TenantId = @TenantId
        ORDER BY MetricCode, PeriodKey, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateMetricLimit = new(
        "tenancy.quota.update_metric_limit",
        """
        UPDATE fn_tenancy_quota_metric
        SET LimitValue = @LimitValue,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MetricId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertMetric = new(
        "tenancy.quota.insert_metric",
        """
        INSERT INTO fn_tenancy_quota_metric
            (Id, TenantId, MetricCode, PeriodKey, LimitValue, UsedValue, ReservedValue,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @MetricCode, @PeriodKey, @LimitValue, 0, 0,
             @CreatedAtUtc, @UpdatedAtUtc, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReserveMetric = new(
        "tenancy.quota.reserve_metric",
        """
        UPDATE fn_tenancy_quota_metric
        SET ReservedValue = ReservedValue + @Amount,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MetricId
          AND UsedValue + ReservedValue + @Amount <= LimitValue
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ConfirmMetric = new(
        "tenancy.quota.confirm_metric",
        """
        UPDATE fn_tenancy_quota_metric
        SET UsedValue = UsedValue + @Amount,
            ReservedValue = ReservedValue - @Amount,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MetricId
          AND ReservedValue >= @Amount
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ReleaseMetric = new(
        "tenancy.quota.release_metric",
        """
        UPDATE fn_tenancy_quota_metric
        SET ReservedValue = ReservedValue - @Amount,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @MetricId
          AND ReservedValue >= @Amount
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    // 旧协议没有指标，只有租户内操作键唯一时才返回；不按排序随意选取重复记录。
    public static readonly SqlStatement FindReservationByTenantOperation = new(
        "tenancy.quota.find_reservation_by_tenant_operation",
        """
        SELECT reservation.Id, reservation.TenantId, reservation.MetricCode, reservation.OperationId,
               reservation.Amount, reservation.Status, reservation.ExpiresAtUtc, reservation.Version, reservation.MetricId
        FROM fn_tenancy_quota_reservation AS reservation
        WHERE reservation.TenantId = @TenantId
          AND reservation.OperationId = @OperationId
          AND NOT EXISTS (
              SELECT 1 FROM fn_tenancy_quota_reservation AS otherReservation
              WHERE otherReservation.TenantId = reservation.TenantId
                AND otherReservation.OperationId = reservation.OperationId
                AND otherReservation.Id <> reservation.Id
          )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindReservationByOperation = new(
        "tenancy.quota.find_reservation_by_operation",
        """
        SELECT Id, TenantId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc, Version, MetricId
        FROM fn_tenancy_quota_reservation
        WHERE TenantId = @TenantId
          AND MetricCode = @MetricCode
          AND OperationId = @OperationId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertReservation = new(
        "tenancy.quota.insert_reservation",
        """
        INSERT INTO fn_tenancy_quota_reservation
            (Id, TenantId, MetricId, MetricCode, OperationId, Amount, Status, ExpiresAtUtc,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @MetricId, @MetricCode, @OperationId, @Amount, @Status, @ExpiresAtUtc,
             @CreatedAtUtc, @UpdatedAtUtc, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListReservationsWithNullMetricId = new(
        "tenancy.quota.list_reservations_null_metric_id",
        """
        SELECT Id, TenantId, MetricCode, Version
        FROM fn_tenancy_quota_reservation
        WHERE MetricId IS NULL
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateReservationMetricId = new(
        "tenancy.quota.update_reservation_metric_id",
        """
        UPDATE fn_tenancy_quota_reservation
        SET MetricId = @MetricId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ReservationId
          AND MetricId IS NULL
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateReservationStatus = new(
        "tenancy.quota.update_reservation_status",
        """
        UPDATE fn_tenancy_quota_reservation
        SET Status = @Status,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ReservationId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}

internal sealed record TenantQuotaMetricRecord(
    Guid Id,
    Guid TenantId,
    string MetricCode,
    string PeriodKey,
    long LimitValue,
    long UsedValue,
    long ReservedValue,
    int Version);

internal sealed record TenantQuotaReservationRecord(
    Guid Id,
    Guid TenantId,
    string MetricCode,
    string OperationId,
    long Amount,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    int Version,
    Guid? MetricId = null);
