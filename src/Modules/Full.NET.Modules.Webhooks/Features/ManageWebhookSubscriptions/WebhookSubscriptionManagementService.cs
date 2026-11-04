using Microsoft.Extensions.Options;
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

/// <summary>Webhooks 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

internal sealed class WebhookSubscriptionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    WebhookSigningSecretProtector signingSecretProtector,
    IClock clock,
    IIdGenerator idGenerator, IOptions<DatabaseOptions>? databaseOptions = null)
{
    /// <summary>先核对本企业与修订，再以同模块事务更新；重复目标状态返回当前事实。</summary>
    /// <param name="id">当前企业中的指定订阅。</param>
    /// <param name="request">明确目标状态及人工确认时读取的修订。</param>
    /// <param name="token">取消当前事务的令牌。</param>
    internal Task<Result<WebhookSubscriptionResponse>> SetStatusAsync(Guid id, SetWebhookSubscriptionStatusRequest request, CancellationToken token) =>
        transaction.ExecuteResultAsync(async cancellation =>
        {
            if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
                return Result<WebhookSubscriptionResponse>.Failure(new Error("tenancy.context_not_found", "Tenant context was not found.", ErrorType.Validation));
            if (id == Guid.Empty || request.Version < 1)
                return Result<WebhookSubscriptionResponse>.Failure(new Error(ValidationErrorCodes.Failed, "A subscription identity and positive version are required.", ErrorType.Validation));
            var values = WebhookSqlParameters.Create(("TenantId", currentTenant.Id.Value), ("Id", id));
            var current = await queryExecutor.QuerySingleOrDefaultAsync<WebhookSubscriptionRecord>(WebhookSubscriptionSql.FindById, values, cancellation).ConfigureAwait(false);
            if (current is null) return Result<WebhookSubscriptionResponse>.Failure(new Error(WebhookErrorCodes.SubscriptionNotFound, "The subscription was not found.", ErrorType.NotFound));
            if (current.Version != request.Version) return StatusConflict();
            // 重复设置不推进版本；状态确认不能借管理入口改写目标或重新加密签名材料。
            if (current.IsActive == request.IsActive) return Result<WebhookSubscriptionResponse>.Success(new(current.Id, current.TenantId, current.EventType, current.TargetUrl, current.IsActive, current.Version));
            values["Version"] = request.Version; values["IsActive"] = request.IsActive; values["NowUtc"] = clock.UtcNow;
            if (await commandExecutor.ExecuteAsync(WebhookSubscriptionSql.SetStatus, values, cancellation).ConfigureAwait(false) != 1) return StatusConflict();
            return Result<WebhookSubscriptionResponse>.Success(new(current.Id, current.TenantId, current.EventType, current.TargetUrl, request.IsActive, checked(current.Version + 1)));
        }, token);

    private static Result<WebhookSubscriptionResponse> StatusConflict() => Result<WebhookSubscriptionResponse>.Failure(
        new Error(WebhookErrorCodes.SubscriptionVersionConflict, "The subscription changed. Read the current state before confirming again.", ErrorType.Conflict));

    /// <summary>校验有界输入并在本地事务中保留扇出名额、加密签名材料和创建订阅。</summary>
    /// <param name="request">事件、允许的 HTTPS 接收地址及不回显的签名密钥。</param>
    /// <param name="cancellationToken">取消创建事务的令牌。</param>
    public Task<Result<WebhookSubscriptionResponse>> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
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

        var eventType = request.EventType?.Trim();
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 128 || eventType.Any(value => value < '!' || value > '~')
            || request.TargetUrl.Length > 2048 || request.SigningSecret is not { Length: >= 16 and <= 128 })
        {
            return Result<WebhookSubscriptionResponse>.Failure(new Error(
                WebhookErrorCodes.SubscriptionInvalid,
                "The event, target or signing secret is invalid.",
                ErrorType.Validation));
        }

        // 正式双库事件键为 128 字符 ASCII；签名材料加密后的实际长度也必须落在现有存储预算内。
        var protectedSecret = signingSecretProtector.Protect(request.SigningSecret);
        if (protectedSecret.Length > 1024)
            return Result<WebhookSubscriptionResponse>.Failure(new Error(WebhookErrorCodes.SubscriptionInvalid,
                "The protected signing secret exceeds the storage budget.", ErrorType.Validation));

        var budget = WebhookSqlParameters.Create(("TenantId", currentTenant.Id.Value),
            ("EventType", eventType), ("MaximumTargets", 500));
        await commandExecutor.ExecuteAsync(databaseOptions?.Value.Provider == DatabaseProvider.MySql
            ? WebhookSubscriptionSql.EnsureFanoutBudgetMySql : WebhookSubscriptionSql.EnsureFanoutBudgetSqlServer,
            budget, cancellationToken).ConfigureAwait(false);
        if (await commandExecutor.ExecuteAsync(WebhookSubscriptionSql.ReserveFanoutSlot,
            budget, cancellationToken).ConfigureAwait(false) != 1)
            return Result<WebhookSubscriptionResponse>.Failure(new Error(
                WebhookErrorCodes.FanoutBudgetExceeded, "An event may have at most 500 subscriptions per tenant.", ErrorType.Validation));

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                WebhookSubscriptionSql.Insert,
                WebhookSqlParameters.Create(
                    ("Id", id),
                    ("TenantId", currentTenant.Id.Value),
                    ("EventType", eventType),
                    ("TargetUrl", urlValidation.Value),
                    ("SigningSecretHash", protectedSecret),
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
