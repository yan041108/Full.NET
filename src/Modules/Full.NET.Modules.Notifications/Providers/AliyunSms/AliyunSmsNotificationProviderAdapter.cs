using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.Modules.Notifications.Providers.AliyunSms;

/// <summary>将已发布阿里云短信 Profile 转换为一次受控 dysmsapi SendSms 调用。</summary>
internal sealed partial class AliyunSmsNotificationProviderAdapter(
    INotificationSecretResolver secretResolver,
    IAliyunSmsTransport transport) : INotificationProviderAdapter
{
    public const string ProviderTypeKeyValue = "sms.aliyun";

    private static readonly HashSet<string> AllowedConfigFields = new(StringComparer.Ordinal)
    {
        "regionId",
        "signName",
        "accessKeyId",
        "verificationTemplateCode",
    };

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        ProviderTypeKeyValue,
        "1.0.0",
        ["sms"],
        [
            new NotificationProviderConfigField("regionId", "string", true),
            new NotificationProviderConfigField("signName", "string", true),
            new NotificationProviderConfigField("accessKeyId", "string", true),
            new NotificationProviderConfigField("verificationTemplateCode", "string", false),
        ],
        ["accessKeySecret"],
        true,
        NotificationReceiptModeKeys.Signed);

    public string? RecipientEndpointKindKey => "sms";

    public async ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.ChannelKey, "sms", StringComparison.Ordinal)
            || !TryParseConfig(request.NonSecretConfigJson, out var config)
            || !IsValidPhoneNumber(request.RecipientEndpoint)
            || !TryParseTemplateParam(request.Body, out var templateParamJson))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var templateCode = request.Subject?.Trim() ?? string.Empty;
        if (templateCode.Length is < 1 or > 64)
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var accessKeySecret = await secretResolver.ResolveAsync(request.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessKeySecret))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var command = new AliyunSmsSendCommand(
            config!.RegionId,
            config.AccessKeyId,
            accessKeySecret,
            request.RecipientEndpoint,
            config.SignName,
            templateCode,
            templateParamJson!,
            request.IdempotencyKey);
        try
        {
            var providerMessageId = await transport.SendAsync(command, cancellationToken)
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
        catch (AliyunSmsTransportException exception)
        {
            var category = exception.FailureKind switch
            {
                AliyunSmsTransportFailureKind.Transient => NotificationDeliveryRetry.Transient,
                AliyunSmsTransportFailureKind.RateLimited => NotificationDeliveryRetry.RateLimited,
                _ => NotificationDeliveryRetry.Permanent,
            };
            return Failed(category);
        }
    }

    internal static bool TryParseConfig(string json, out AliyunSmsProviderConfig? config)
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

            if (!TryReadString(document.RootElement, "regionId", 32, out var regionId)
                || !TryReadString(document.RootElement, "signName", 64, out var signName)
                || !TryReadString(document.RootElement, "accessKeyId", 64, out var accessKeyId))
            {
                return false;
            }

            string? verificationTemplateCode = null;
            if (document.RootElement.TryGetProperty("verificationTemplateCode", out var verificationElement))
            {
                if (verificationElement.ValueKind != JsonValueKind.String
                    || verificationElement.GetString() is not { Length: > 0 and <= 64 } parsed
                    || ContainsControlCharacter(parsed))
                {
                    return false;
                }

                verificationTemplateCode = parsed;
            }

            config = new AliyunSmsProviderConfig(
                regionId,
                signName,
                accessKeyId,
                verificationTemplateCode);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static NotificationProviderResult Failed(string category) =>
        new(false, category, null, null);

    private static bool TryParseTemplateParam(string body, out string? templateParamJson)
    {
        templateParamJson = null;
        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 1024)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(property =>
                    property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array))
            {
                return false;
            }

            templateParamJson = trimmed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsValidChinaMobilePhone(string phone) =>
        ChinaMobilePattern().IsMatch(phone);

    private static bool IsValidPhoneNumber(string phone) =>
        IsValidChinaMobilePhone(phone);

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

    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex ChinaMobilePattern();

    internal sealed record AliyunSmsProviderConfig(
        string RegionId,
        string SignName,
        string AccessKeyId,
        string? VerificationTemplateCode);
}
