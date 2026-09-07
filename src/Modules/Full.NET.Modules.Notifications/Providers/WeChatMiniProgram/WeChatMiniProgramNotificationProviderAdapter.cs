using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

/// <summary>将已发布微信小程序 Profile 转换为一次受控订阅消息调用。</summary>
internal sealed partial class WeChatMiniProgramNotificationProviderAdapter(
    INotificationSecretResolver secretResolver,
    WeChatMiniProgramAccessTokenCache tokenCache,
    IWeChatMiniProgramTransport transport) : INotificationProviderAdapter
{
    public const string ProviderTypeKeyValue = "im.wechat_miniprogram";

    private static readonly HashSet<string> AllowedConfigFields = new(StringComparer.Ordinal)
    {
        "appId",
        "defaultTemplateId",
    };

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        ProviderTypeKeyValue,
        "1.0.0",
        ["wechat_miniprogram"],
        [
            new NotificationProviderConfigField("appId", "string", true),
            new NotificationProviderConfigField("defaultTemplateId", "string", true),
        ],
        ["appSecret"],
        true,
        NotificationReceiptModeKeys.None);

    public string? RecipientEndpointKindKey => "wechat_miniprogram";

    public async ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.ChannelKey, "wechat_miniprogram", StringComparison.Ordinal)
            || !TryParseConfig(request.NonSecretConfigJson, out var config)
            || !IsValidOpenId(request.RecipientEndpoint)
            || !TryParseTemplateData(request.Body, out var dataJson))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var templateId = request.Subject?.Trim() ?? string.Empty;
        if (templateId.Length is < 1 or > 128)
        {
            templateId = config!.DefaultTemplateId;
        }

        var appSecret = await secretResolver.ResolveAsync(Descriptor.ProviderTypeKey, request.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(appSecret))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        try
        {
            var accessToken = await tokenCache
                .GetOrRefreshAsync(config!.AppId, appSecret, cancellationToken)
                .ConfigureAwait(false);
            var providerMessageId = await transport.SendSubscribeMessageAsync(
                    accessToken,
                    new WeChatMiniProgramSubscribeSendCommand(
                        request.RecipientEndpoint,
                        templateId,
                        null,
                        dataJson!,
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
        catch (WeChatMiniProgramTransportException exception)
        {
            var category = exception.FailureKind switch
            {
                WeChatMiniProgramTransportFailureKind.Transient => NotificationDeliveryRetry.Transient,
                WeChatMiniProgramTransportFailureKind.RateLimited => NotificationDeliveryRetry.RateLimited,
                _ => NotificationDeliveryRetry.Permanent,
            };
            return Failed(category);
        }
    }

    internal static bool TryParseConfig(string json, out WeChatMiniProgramProviderConfig? config)
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

            if (!TryReadString(document.RootElement, "appId", 32, out var appId)
                || !TryReadString(document.RootElement, "defaultTemplateId", 128, out var defaultTemplateId))
            {
                return false;
            }

            config = new WeChatMiniProgramProviderConfig(appId, defaultTemplateId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsValidOpenId(string openId) => OpenIdPattern().IsMatch(openId);

    private static bool TryParseTemplateData(string body, out string? dataJson)
    {
        dataJson = null;
        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 4096)
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

            dataJson = trimmed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
            || parsed.Any(char.IsControl))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_-]{7,63}$")]
    private static partial Regex OpenIdPattern();

    internal sealed record WeChatMiniProgramProviderConfig(string AppId, string DefaultTemplateId);
}
