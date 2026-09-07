using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.Modules.Notifications.Providers.WeCom;

/// <summary>将已发布企业微信 Profile 转换为一次受控文本应用消息调用。</summary>
internal sealed partial class WeComNotificationProviderAdapter(
    INotificationSecretResolver secretResolver,
    WeComAccessTokenCache tokenCache,
    IWeComTransport transport) : INotificationProviderAdapter
{
    public const string ProviderTypeKeyValue = "im.wecom";

    private static readonly HashSet<string> AllowedConfigFields = new(StringComparer.Ordinal)
    {
        "corpId",
        "agentId",
    };

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        ProviderTypeKeyValue,
        "1.0.0",
        ["wecom"],
        [
            new NotificationProviderConfigField("corpId", "string", true),
            new NotificationProviderConfigField("agentId", "string", true),
        ],
        ["corpSecret"],
        true,
        NotificationReceiptModeKeys.None);

    public string? RecipientEndpointKindKey => "wecom";

    public async ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.ChannelKey, "wecom", StringComparison.Ordinal)
            || !TryParseConfig(request.NonSecretConfigJson, out var config)
            || !IsValidWeComUserId(request.RecipientEndpoint)
            || !TryBuildTextContent(request.Subject, request.Body, out var content))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var corpSecret = await secretResolver.ResolveAsync(Descriptor.ProviderTypeKey, request.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(corpSecret))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        try
        {
            var accessToken = await tokenCache
                .GetOrRefreshAsync(config!.CorpId, corpSecret, cancellationToken)
                .ConfigureAwait(false);
            var providerMessageId = await transport.SendTextAsync(
                    accessToken,
                    new WeComSendTextCommand(
                        request.RecipientEndpoint,
                        config.AgentId,
                        content!,
                        request.IdempotencyKey),
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
        catch (WeComTransportException exception)
        {
            var category = exception.FailureKind switch
            {
                WeComTransportFailureKind.Transient => NotificationDeliveryRetry.Transient,
                WeComTransportFailureKind.RateLimited => NotificationDeliveryRetry.RateLimited,
                _ => NotificationDeliveryRetry.Permanent,
            };
            return Failed(category);
        }
    }

    internal static bool TryParseConfig(string json, out WeComProviderConfig? config)
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

            if (!TryReadString(document.RootElement, "corpId", 64, out var corpId)
                || !TryReadAgentId(document.RootElement, out var agentId))
            {
                return false;
            }

            config = new WeComProviderConfig(corpId, agentId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsValidWeComUserId(string userId) =>
        WeComUserIdPattern().IsMatch(userId);

    private static bool TryBuildTextContent(string subject, string body, out string? content)
    {
        content = null;
        var title = subject?.Trim() ?? string.Empty;
        var text = body?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 256 || text.Length > 4096)
        {
            return false;
        }

        content = string.IsNullOrEmpty(text) ? title : $"{title}\n{text}";
        if (content.Length > 4096)
        {
            return false;
        }

        return true;
    }

    private static NotificationProviderResult Failed(string category) =>
        new(false, category, null, null);

    private static bool TryReadAgentId(JsonElement root, out int agentId)
    {
        agentId = 0;
        if (!root.TryGetProperty("agentId", out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), out agentId)
            && agentId > 0)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out agentId)
            && agentId > 0)
        {
            return true;
        }

        return false;
    }

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
    private static partial Regex WeComUserIdPattern();

    internal sealed record WeComProviderConfig(string CorpId, int AgentId);
}
