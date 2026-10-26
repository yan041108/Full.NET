using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.TenantUnits;

internal sealed partial class WorkflowOrganizationAssigneeDirectory
{
    /// <inheritdoc />
    public async Task<WorkflowOrganizationLeaderResolution> ResolveLeaderSourceAsync(Guid initiatorUserId,
        WorkflowOrganizationLeaderSource source, CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        if (!Enum.IsDefined(source.Kind) || source.Kind == WorkflowOrganizationLeaderSourceKind.Unit && (source.UnitId is null || source.UnitId == Guid.Empty || source.Depth != 0) ||
            source.Kind != WorkflowOrganizationLeaderSourceKind.Unit && (source.UnitId is not null || initiatorUserId == Guid.Empty) ||
            source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorPrimaryUnit && source.Depth != 0 ||
            source.Kind is WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit or WorkflowOrganizationLeaderSourceKind.InitiatorLeaderChain && source.Depth is < 1 or > 20)
            return InvalidLeadership();
        var initial = source.Kind == WorkflowOrganizationLeaderSourceKind.Unit ? source.UnitId
            : await queryExecutor.QuerySingleOrDefaultAsync<Guid?>(WorkflowOrganizationAssigneeSql.FindInitiatorPrimaryUnitId,
                OrganizationSqlParameters.Create(("UserId", initiatorUserId)), cancellationToken).ConfigureAwait(false);
        if (initial is null) return EmptyLeadership();
        var current = initial.Value; var visited = new HashSet<Guid>(); var rows = new List<OrganizationLeaderSourceUnitRecord>();
        var maximum = source.Kind switch {
            WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit => source.Depth + 1,
            WorkflowOrganizationLeaderSourceKind.InitiatorLeaderChain => source.Depth,
            _ => 1
        };
        for (var index = 0; index < maximum; index++)
        {
            if (current == Guid.Empty || !visited.Add(current)) return InvalidLeadership();
            var row = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationLeaderSourceUnitRecord>(OrganizationUnitLeadershipSql.ReadSourceUnit,
                OrganizationSqlParameters.Create(("UnitId", current)), cancellationToken).ConfigureAwait(false);
            if (row is null || row.ParentId == row.Id || row.ParentId == Guid.Empty || row.LeaderUserId == Guid.Empty) return InvalidLeadership();
            rows.Add(row);
            if (index == maximum - 1) break;
            if (row.ParentId is null)
            {
                if (source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit) return EmptyLeadership();
                break;
            }
            current = row.ParentId.Value;
        }
        var selected = source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit ? rows.TakeLast(1).ToArray() : rows.ToArray();
        if (selected.All(row => row.LeaderUserId is null)) return EmptyLeadership();
        // 连续链不静默省去中间缺失的负责人；不完整链必须修复，不能用部分成员冒充完整配置。
        if (selected.Any(row => row.LeaderUserId is null)) return InvalidLeadership();
        return new(true, selected.Select((row, index) => new WorkflowOrganizationLeaderDirectoryEntry(row.Id, row.LeaderUserId!.Value,
            source.Kind == WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit ? source.Depth + 1 : index + 1)).ToArray());
    }

    private static WorkflowOrganizationLeaderResolution InvalidLeadership() => new(false, []);
    private static WorkflowOrganizationLeaderResolution EmptyLeadership() => new(true, []);
}
