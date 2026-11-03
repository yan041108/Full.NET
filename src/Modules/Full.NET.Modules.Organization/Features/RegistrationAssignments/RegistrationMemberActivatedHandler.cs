using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.RegistrationAssignments;

/// <summary>事件只接收本地意图，避免 Inbox 事务内同步访问 Identity 权威数据。</summary>
internal sealed class RegistrationMemberActivatedHandler(IIntegrationEventSerializer serializer, IQueryExecutor queries,
    ICommandExecutor commands, ICommandTransaction transaction, ICurrentTenant tenant) : IIntegrationEventHandler
{
    public string EventType => RegistrationMemberIntegrationEventTypes.Activated;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<RegistrationMemberActivatedIntegrationEvent>(payload);
        if (context.TenantId != value.TenantId || tenant.Id != value.TenantId || value.TenantId == Guid.Empty
            || value.OperationId == Guid.Empty || value.MemberId == Guid.Empty || value.UserId == Guid.Empty
            || value.OrganizationUnitId == Guid.Empty || value.PositionId == Guid.Empty || value.MemberVersion <= 0)
            throw new InvalidOperationException("organization.registration_assignment.invalid_event");
        await transaction.ExecuteAsync(async token =>
        {
            var parameters = OrganizationSqlParameters.Create(("Id", value.OperationId), ("MemberId", value.MemberId),
                ("MemberVersion", value.MemberVersion), ("UserId", value.UserId), ("UnitId", value.OrganizationUnitId),
                ("PositionId", value.PositionId), ("Now", value.OccurredAtUtc));
            if (await commands.ExecuteAsync(RegistrationAssignmentSql.Enlist, parameters, token).ConfigureAwait(false) == 0)
            {
                var existing = await queries.QuerySingleOrDefaultAsync<RegistrationAssignmentRecord>(RegistrationAssignmentSql.Find,
                    parameters, token).ConfigureAwait(false);
                if (existing is null || existing.MemberId != value.MemberId || existing.MemberVersion != value.MemberVersion
                    || existing.UserId != value.UserId || existing.UnitId != value.OrganizationUnitId || existing.PositionId != value.PositionId)
                    throw new InvalidOperationException("organization.registration_assignment.intent_conflict");
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("organization.registration_assignment.context_required");
}
