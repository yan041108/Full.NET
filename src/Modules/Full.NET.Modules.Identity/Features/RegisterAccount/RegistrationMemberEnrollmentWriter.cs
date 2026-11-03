using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.RegisterAccount;

/// <summary>只在创建账号的 Identity 本地事务内登记成员与席位来源，不调用配额或组织模块。</summary>
internal interface IRegistrationMemberEnrollmentWriter
{
    Task<Result<bool>> EnlistAsync(Guid userId, Guid tenantId, Guid registrationWayId, CancellationToken token);
}

/// <summary>目标来自已复核的注册方式；成员保持 Pending，直到持久席位编排完成。</summary>
internal sealed class RegistrationMemberEnrollmentWriter(ICommandExecutor commands, ICurrentTenantContextWriter tenant,
    TenantSeatOperationProcessor seats, IClock clock, IIdGenerator ids) : IRegistrationMemberEnrollmentWriter
{
    public async Task<Result<bool>> EnlistAsync(Guid userId, Guid tenantId, Guid registrationWayId, CancellationToken token)
    {
        var memberId = ids.NewId();
        var now = clock.UtcNow;
        await Features.AcceptTenantInvitation.IdentityTenantInvitationScope.RunAsync(tenant,
            new TenantContext(tenantId, tenantId.ToString("D"), tenantId.ToString("D")), async () =>
            {
                if (await commands.ExecuteAsync(TenantMembershipSql.InsertMember, IdentitySqlParameters.Create(
                    ("Id", memberId), ("UserId", userId), ("MemberRole", TenantMemberRoles.Member),
                    ("Status", TenantMemberStatuses.Pending), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)), token).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("identity.registration_membership.intent_not_persisted");
                await seats.EnlistAsync(new TenantSeatOperation(ids.NewId(), "registration:" + userId.ToString("D"), tenantId,
                    userId, memberId, null, null, null, 1, null, TenantSeatPhases.Requested, null, null,
                    now, now, null, null, 1, 1, registrationWayId), token).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
}
