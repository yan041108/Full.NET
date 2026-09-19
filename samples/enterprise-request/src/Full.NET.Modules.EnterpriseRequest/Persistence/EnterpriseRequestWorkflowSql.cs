using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

internal static class EnterpriseRequestWorkflowSql
{
    public static readonly SqlStatement ApplyTerminalStatus = new(
        "enterprise_request.apply_terminal_status",
        """
        UPDATE demo_enterprise_request_enterprise_request
        SET Status = @Status,
            Version = Version + 1,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE Id = @Id
          AND TenantId = @TenantId
          AND Status = @ExpectedStatus
          AND Version = @ExpectedVersion
          AND IsDeleted = 0
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ApplySubmittedStatus = new(
        "enterprise_request.apply_submitted_status",
        """
        UPDATE demo_enterprise_request_enterprise_request
        SET Status = @Status,
            Version = Version + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedById = @UpdatedById
        WHERE Id = @Id
          AND TenantId = @TenantId
          AND Status = @ExpectedStatus
          AND Version = @ExpectedVersion
          AND IsDeleted = 0
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
