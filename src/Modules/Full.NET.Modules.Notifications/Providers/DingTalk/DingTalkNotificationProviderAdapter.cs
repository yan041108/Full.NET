using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>将已发布钉钉 Profile 转换为一次受控互动卡片 createAndDeliver 调用。</summary>
internal sealed partial class DingTalkNotificationProviderAdapter(
    INotificationSecretResolver secretResolver,
    DingTalkAccessTokenCache tokenCache,
    IDingTalkTransport transport) : INotificationProviderAdapter
{
    public const string ProviderTypeKeyValue = "im.dingtalk";

    private static readonly HashSet<string> AllowedConfigFields = new(StringComparer.Ordinal)
    {
        "appKey",
        "agentId",
        "cardTemplateId",
        "robotCode",
        "callbackRouteKey",
    };

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        ProviderTypeKeyValue,
        "1.0.0",
        ["dingtalk"],
        [
            new NotificationProviderConfigField("appKey", "string", true),
            new NotificationProviderConfigField("agentId", "string", true),
            new NotificationProviderConfigField("cardTemplateId", "string", true),
            new NotificationProviderConfigField("robotCode", "string", true),
            new NotificationProviderConfigField("callbackRouteKey", "string", false),
        ],
        ["appSecret"],
        true,
        NotificationReceiptModeKeys.Signed);

    public string? RecipientEndpointKindKey => "dingtalk";

    public async ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.ChannelKey, "dingtalk", StringComparison.Ordinal)
            || !TryParseConfig(request.NonSecretConfigJson, out var config)
            || !IsValidDingTalkUserId(request.RecipientEndpoint)
            || !TryBuildCardParamMap(request.Subject, request.Body, out var cardParamMap))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var appSecret = await secretResolver.ResolveAsync(request.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(appSecret))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var outTrackId = BuildOutTrackId(request.IdempotencyKey);
        var openSpaceId = $"dtv1.card//IM_ROBOT.{request.RecipientEndpoint}";
        try
        {
            var accessToken = await tokenCache
                .GetOrRefreshAsync(config!.AppKey, appSecret, cancellationToken)
                .ConfigureAwait(false);
            var providerMessageId = await transport.CreateAndDeliverAsync(
                    accessToken,
                    new DingTalkCreateAndDeliverCommand(
                        config.CardTemplateId,
                        outTrackId,
                        openSpaceId,
                        config.RobotCode,
                        config.CallbackRouteKey,
                        cardParamMap!),
                    cancellationToken)
                .ConfigureAwait(false);
            return new NotificationProviderResult(
                true,
                NotificationDeliveryRetry.Succeeded,
                providerMessageId,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DingTalkTransportException exception)
        {
            var category = exception.FailureKind switch
            {
                DingTalkTransportFailureKind.Transient => NotificationDeliveryRetry.Transient,
                DingTalkTransportFailureKind.RateLimited => NotificationDeliveryRetry.RateLimited,
                _ => NotificationDeliveryRetry.Permanent,
            };
            return Failed(category);
        }
    }

    internal static bool TryParseConfig(string json, out DingTalkProviderConfig? config)
    {
        config = null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var provided = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedConfigFields.Contains(property.Name) || !provided.Add(property.Name))
                {
                    return false;
                }
            }

            if (!TryReadString(document.RootElement, "appKey", 64, out var appKey)
                || !TryReadString(document.RootElement, "agentId", 32, out var agentId)
                || !TryReadString(document.RootElement, "cardTemplateId", 128, out var cardTemplateId)
                || !TryReadString(document.RootElement, "robotCode", 64, out var robotCode))
            {
                return false;
            }

            string? callbackRouteKey = null;
            if (document.RootElement.TryGetProperty("callbackRouteKey", out var callbackElement))
            {
                if (callbackElement.ValueKind != JsonValueKind.String
                    || callbackElement.GetString() is not { Length: > 0 and <= 64 } parsed
                    || ContainsControlCharacter(parsed))
                {
                    return false;
                }

                callbackRouteKey = parsed;
            }

            config = new DingTalkProviderConfig(
                appKey,
                agentId,
                cardTemplateId,
                robotCode,
                callbackRouteKey);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsValidDingTalkUserId(string userId) =>
        DingTalkUserIdPattern().IsMatch(userId);

    private static bool TryBuildCardParamMap(
        string subject,
        string body,
        out IReadOnlyDictionary<string, string>? cardParamMap)
    {
        cardParamMap = null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryParseCardParamJson(body, map))
        {
            cardParamMap = map;
            return map.Count > 0;
        }

        var title = subject?.Trim() ?? string.Empty;
        var content = body?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 256 || content.Length > 4096)
        {
            return false;
        }

        map["title"] = title;
        if (content.Length > 0)
        {
            map["content"] = content;
        }

        cardParamMap = map;
        return true;
    }

    private static bool TryParseCardParamJson(string body, Dictionary<string, string> map)
    {
        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 8192)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Length is < 1 or > 64
                    || property.Value.ValueKind != JsonValueKind.String
                    || property.Value.GetString() is not { Length: <= 4096 } value
                    || ContainsControlCharacter(value))
                {
                    return false;
                }

                map[property.Name] = value;
            }

            return map.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildOutTrackId(string idempotencyKey)
    {
        var normalized = idempotencyKey.Replace(":", "-", StringComparison.Ordinal);
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }

    private static NotificationProviderResult Failed(string category) =>
        new(false, category, null, null);

    private static bool TryReadString(
        JsonElement root,
        string name,
        int maxLength,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String
            || element.GetString() is not { Length: > 0 } parsed
            || parsed.Length > maxLength
            || ContainsControlCharacter(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool ContainsControlCharacter(string value) =>
        value.Any(char.IsControl);

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$")]
    private static partial Regex DingTalkUserIdPattern();

    internal sealed record DingTalkProviderConfig(
        string AppKey,
        string AgentId,
        string CardTemplateId,
        string RobotCode,
        string? CallbackRouteKey);
}
