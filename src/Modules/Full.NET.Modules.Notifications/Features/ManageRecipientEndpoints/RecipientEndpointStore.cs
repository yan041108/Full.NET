using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;

/// <summary>按受信作用域登记收件端点；查询只返回掩码与验证状态。</summary>
internal sealed class RecipientEndpointStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    NotificationRecipientEndpointProtector protector,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<IReadOnlyList<RecipientEndpointResponse>>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<NotificationRecipientEndpointRecord>(
                NotificationRecipientEndpointSql.ListMaskedByScopeUser,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<RecipientEndpointResponse>>.Success(rows.Select(Map).ToArray());
    }

    public Task<Result<RecipientEndpointResponse>> UpsertAsync(
        Guid actorUserId,
        Guid providerProfileVersionId,
        string endpointKindKey,
        string rawValue,
        string verificationStatusKey,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpsertCoreAsync(
                actorUserId,
                providerProfileVersionId,
                endpointKindKey,
                rawValue,
                verificationStatusKey,
                token),
            cancellationToken);

    private async Task<Result<RecipientEndpointResponse>> UpsertCoreAsync(
        Guid actorUserId,
        Guid providerProfileVersionId,
        string endpointKindKey,
        string rawValue,
        string verificationStatusKey,
        CancellationToken cancellationToken)
    {
        var kind = endpointKindKey?.Trim() ?? string.Empty;
        var value = rawValue?.Trim() ?? string.Empty;
        var status = verificationStatusKey?.Trim() ?? string.Empty;
        if (kind.Length is < 1 or > 32 || value.Length is < 1 or > 256
            || status is not (NotificationRecipientEndpointStatuses.Pending
                or NotificationRecipientEndpointStatuses.Verified
                or NotificationRecipientEndpointStatuses.Failed))
        {
            return Result<RecipientEndpointResponse>.Failure(new Error(
                NotificationsErrorCodes.RecipientEndpointValidationFailed,
                "The recipient endpoint value or kind is invalid.",
                ErrorType.Validation));
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointSql.Insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", id),
                    ("InboxTenantId", scope.TenantId),
                    ("ScopeKey", scope.ScopeKey),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", actorUserId),
                    ("ProviderProfileVersionId", providerProfileVersionId),
                    ("EndpointKindKey", kind),
                    ("ProtectedValue", protector.Protect(value)),
                    ("MaskedValue", NotificationRecipientEndpointMasker.Mask(value, kind)),
                    ("VerificationStatusKey", status),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var record = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientEndpointRecord>(
                NotificationRecipientEndpointSql.FindMaskedById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", id),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<RecipientEndpointResponse>.Failure(new Error(
                NotificationsErrorCodes.RecipientEndpointValidationFailed,
                "The recipient endpoint value or kind is invalid.",
                ErrorType.Validation))
            : Result<RecipientEndpointResponse>.Success(Map(record));
    }

    private static RecipientEndpointResponse Map(NotificationRecipientEndpointRecord record) =>
        new(
            record.Id,
            record.UserId,
            record.ProviderProfileVersionId,
            record.EndpointKindKey,
            record.MaskedValue,
            record.VerificationStatusKey,
            record.CreatedAtUtc);
}
