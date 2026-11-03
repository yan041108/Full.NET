using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.RegistrationAssignments;

/// <summary>活动成员复核在事务外；完成标记与缺失组织关系在 Organization 同一事务写入。</summary>
internal sealed class RegistrationAssignmentProcessor(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, ITenantMemberSelectionDirectory members, IClock clock, IIdGenerator ids)
{
    internal async Task ProcessAsync(RegistrationAssignmentRecord intent, bool tenantActive, CancellationToken cancellationToken)
    {
        if (intent.Status != "Pending" || intent.DueAtUtc > clock.UtcNow) return;
        var current = tenantActive && await members.IsCurrentActivationAsync(intent.MemberId, intent.UserId,
            intent.MemberVersion, cancellationToken).ConfigureAwait(false);
        await transaction.ExecuteAsync(async token =>
        {
            // 先取得本地意图写锁；相同版本只有一个执行器能继续，失败时所有组织写入一起回滚。
            if (await commands.ExecuteAsync(RegistrationAssignmentSql.Finish, Parameters(intent,
                current ? "Completed" : "Skipped", current ? null : "organization.registration_assignment.member_changed"), token).ConfigureAwait(false) != 1) return false;
            if (!current) return true;
            var unit = await queries.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(OrganizationSql.FindUnitById,
                OrganizationSqlParameters.Create(("UnitId", intent.UnitId)), token).ConfigureAwait(false);
            if (unit is not { IsActive: true }) throw new InvalidOperationException("organization.registration_assignment.unit_unavailable");
            if (intent.PositionId is Guid positionId)
            {
                var position = await queries.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(PositionSql.FindById,
                    OrganizationSqlParameters.Create(("PositionId", positionId)), token).ConfigureAwait(false);
                if (position is not { IsActive: true }) throw new InvalidOperationException("organization.registration_assignment.position_unavailable");
            }
            var assignment = await queries.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(OrganizationSql.FindUserUnitByTenantUserAndUnit,
                OrganizationSqlParameters.Create(("UserId", intent.UserId), ("UnitId", intent.UnitId)), token).ConfigureAwait(false);
            // 不重启人工停用的关系，也不替用户移动主部门/主职位。
            if (assignment is null)
                await commands.ExecuteAsync(OrganizationSql.InsertUserUnit, OrganizationSqlParameters.Create(
                    ("Id", ids.NewId()), ("UserId", intent.UserId), ("UnitId", intent.UnitId), ("IsPrimary", false),
                    ("IsActive", true), ("CreatedAtUtc", clock.UtcNow), ("Version", 1)), token).ConfigureAwait(false);
            if (intent.PositionId is Guid targetPosition)
            {
                var existing = await queries.QuerySingleOrDefaultAsync<OrganizationUserPositionRecord>(OrganizationSql.FindUserPositionByTenantUserAndPosition,
                    OrganizationSqlParameters.Create(("UserId", intent.UserId), ("PositionId", targetPosition)), token).ConfigureAwait(false);
                if (existing is null)
                    await commands.ExecuteAsync(OrganizationSql.InsertUserPosition, OrganizationSqlParameters.Create(
                        ("Id", ids.NewId()), ("UserId", intent.UserId), ("PositionId", targetPosition), ("IsPrimary", false),
                        ("IsActive", true), ("CreatedAtUtc", clock.UtcNow), ("Version", 1)), token).ConfigureAwait(false);
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    internal Task<int> RetryAsync(RegistrationAssignmentRecord intent, string errorCode, CancellationToken token) =>
        commands.ExecuteAsync(RegistrationAssignmentSql.Retry, OrganizationSqlParameters.Create(("Id", intent.Id),
            ("Version", intent.Version), ("ErrorCode", errorCode), ("DueAtUtc", clock.UtcNow.AddSeconds(30))), token);

    private object Parameters(RegistrationAssignmentRecord intent, string status, string? error) =>
        OrganizationSqlParameters.Create(("Id", intent.Id), ("Version", intent.Version), ("Status", status),
            ("ErrorCode", error), ("Now", clock.UtcNow));
}
