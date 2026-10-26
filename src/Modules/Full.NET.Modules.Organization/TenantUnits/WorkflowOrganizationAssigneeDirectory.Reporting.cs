using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.TenantUnits;

internal sealed partial class WorkflowOrganizationAssigneeDirectory
{
    /// <inheritdoc />
    public async Task<WorkflowDirectManagerResolution> ResolveDirectManagerAsync(Guid initiatorUserId, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        if (initiatorUserId == Guid.Empty) return new(false, null, null, 0);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationReportingLineRecord>(OrganizationReportingLineSql.Find,
            OrganizationSqlParameters.Create(("EmployeeUserId", initiatorUserId)), cancellationToken).ConfigureAwait(false);
        if (row is null) return new(true, null, null, 0);
        if (row.Id == Guid.Empty || row.EmployeeUserId != initiatorUserId || row.Revision < 1 || row.ManagerUserId == Guid.Empty || row.ManagerUserId == initiatorUserId)
            return new(false, null, null, 0);
        return new(true, row.Id, row.ManagerUserId, row.Revision);
    }
}
