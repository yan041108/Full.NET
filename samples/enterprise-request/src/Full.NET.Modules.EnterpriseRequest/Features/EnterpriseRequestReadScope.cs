using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

internal sealed partial class EnterpriseRequestQueryService
{
    // 机构目录的 self 指本人关联机构；申请单的 self 必须指不可伪造的创建者。
    // 申请人字段允许代填，不能用来提升读取范围。部门等其他角色仍通过权威 Port 取并集。
    partial void ConfigureReadDataScope(EffectiveUserDataScope scope, Guid currentUserId, ref DataScopeSqlFilter? filter)
    {
        if (scope.IsUnrestricted || !scope.RoleScopes.Any(role => role.DataScopeKind == RoleDataScopeKinds.Self)) return;
        var remaining = scope.RoleScopes.Where(role => role.DataScopeKind != RoleDataScopeKinds.Self).ToArray();
        var ownerSql = "CreatedById = @EnterpriseRequestSelfUserId";
        var parameters = new Dictionary<string, object?> { ["EnterpriseRequestSelfUserId"] = currentUserId };
        if (remaining.Length == 0)
        {
            filter = new(ownerSql, parameters);
            return;
        }
        var other = dataScopeFilterBuilder.BuildOrganizationUnitFilter(new(false, remaining), "OrganizationUnitId", currentUserId);
        // null 沿用 Port 的无限制约定，例如可信 all 角色；租户外层谓词始终保留。
        if (other is null) { filter = null; return; }
        if (other.Parameters is IEnumerable<KeyValuePair<string, object?>> entries)
            foreach (var entry in entries) parameters.Add(entry.Key, entry.Value);
        else if (other.Parameters is not null)
            throw new InvalidOperationException("Data scope parameters must use a static dictionary.");
        filter = new($"({ownerSql}) OR ({other.Sql})", parameters);
    }
}
