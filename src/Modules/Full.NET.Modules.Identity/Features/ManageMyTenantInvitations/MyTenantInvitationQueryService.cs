using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Abstractions.Time;

namespace Full.NET.Modules.Identity.Features.ManageMyTenantInvitations;

internal sealed class MyTenantInvitationQueryService(
    IQueryExecutor queryExecutor,
    IActiveTenantContextResolver tenantResolver,
    IClock clock)
{
    public async Task<Result<IReadOnlyList<MyTenantInvitationResponse>>> ListPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profileEmail = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                AccountLifecycleSql.FindUserProfileEmailByUserId,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        var normalizedEmail = AccountChallengeService.NormalizeEmail(profileEmail);
        var rows = await queryExecutor.QueryAsync<TenantInvitationRecord>(
                TenantMembershipSql.ListPendingInvitationsForInvitee,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("NormalizedEmail", normalizedEmail),
                    ("PendingStatus", TenantInvitationStatuses.Pending),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);

        var responses = new List<MyTenantInvitationResponse>(rows.Count);
        foreach (var row in rows)
        {
            var tenant = await tenantResolver.ResolveActiveByIdAsync(row.TenantId, cancellationToken)
                .ConfigureAwait(false);
            if (tenant is null)
            {
                continue;
            }

            responses.Add(new MyTenantInvitationResponse(
                row.Id,
                row.TenantId,
                tenant.Name,
                row.TargetEmail,
                row.MemberRole,
                row.ExpiresAtUtc,
                row.CreatedAtUtc));
        }

        return Result<IReadOnlyList<MyTenantInvitationResponse>>.Success(responses);
    }
}
