using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Security;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

internal sealed class WebhookSubscriptionManagementService(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    WebhookSigningSecretProtector signingSecretProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<WebhookSubscriptionResponse>> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    internal static Result<string> ValidateTargetUrl(string? targetUrl)
    {
        var normalized = targetUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || WebhookTargetUrlPolicy.IsBlocked(uri))
        {
            return Result<string>.Failure(new Error(
                WebhookErrorCodes.TargetUrlInvalid,
                "Target URL is invalid.",
                ErrorType.Validation));
        }

        return Result<string>.Success(normalized);
    }

    private async Task<Result<WebhookSubscriptionResponse>> CreateCoreAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is null)
        {
            return Result<WebhookSubscriptionResponse>.Failure(new Error(
                "tenancy.context_not_found",
                "Tenant context was not found.",
                ErrorType.Validation));
        }

        var urlValidation = ValidateTargetUrl(request.TargetUrl);
        if (!urlValidation.IsSuccess)
        {
            return Result<WebhookSubscriptionResponse>.Failure(urlValidation.Error!);
        }

        var eventType = request.EventType.Trim();
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return Result<WebhookSubscriptionResponse>.Failure(new Error(
                WebhookErrorCodes.TargetUrlInvalid,
                "Event type is required.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                WebhookSubscriptionSql.Insert,
                WebhookSqlParameters.Create(
                    ("Id", id),
                    ("TenantId", currentTenant.Id.Value),
                    ("EventType", eventType),
                    ("TargetUrl", urlValidation.Value),
                    ("SigningSecretHash", signingSecretProtector.Protect(request.SigningSecret)),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<WebhookSubscriptionResponse>.Success(
            new WebhookSubscriptionResponse(
                id,
                currentTenant.Id.Value,
                eventType,
                urlValidation.Value!,
                true,
                1));
    }
}
