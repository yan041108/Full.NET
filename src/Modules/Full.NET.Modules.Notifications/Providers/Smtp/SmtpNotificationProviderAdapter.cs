using System.Text.Json;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using MimeKit;

namespace Full.NET.Modules.Notifications.Providers.Smtp;

/// <summary>将已发布邮件 Profile 转换为一次受控 SMTP 文本邮件调用。</summary>
internal sealed class SmtpNotificationProviderAdapter(
    INotificationSecretResolver secretResolver,
    ISmtpMailTransport transport) : INotificationProviderAdapter
{
    private static readonly HashSet<string> AllowedConfigFields = new(StringComparer.Ordinal)
    {
        "host",
        "port",
        "secureSocketMode",
        "username",
        "fromAddress",
        "fromDisplayName",
    };

    public NotificationProviderTypeDescriptor Descriptor { get; } = new(
        "email.smtp",
        "1.0.0",
        ["email"],
        [
            new NotificationProviderConfigField("host", "string", true),
            new NotificationProviderConfigField("port", "integer", true),
            new NotificationProviderConfigField("secureSocketMode", "string", true),
            new NotificationProviderConfigField("username", "string", true),
            new NotificationProviderConfigField("fromAddress", "string", true),
            new NotificationProviderConfigField("fromDisplayName", "string", false),
        ],
        ["password"],
        true,
        "none");

    public string? RecipientEndpointKindKey => "email";

    public async ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.ChannelKey, "email", StringComparison.Ordinal)
            || !TryParseConfig(request.NonSecretConfigJson, out var config)
            || !IsValidAddress(request.RecipientEndpoint))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var password = await secretResolver.ResolveAsync(request.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(password))
        {
            return Failed(NotificationDeliveryRetry.Permanent);
        }

        var command = new SmtpSendCommand(
            config!.Host,
            config.Port,
            config.SecureSocketMode,
            config.Username,
            password,
            config.FromAddress,
            config.FromDisplayName,
            request.RecipientEndpoint,
            request.Subject,
            request.Body,
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
        catch (SmtpTransportException exception)
        {
            var category = exception.FailureKind switch
            {
                SmtpTransportFailureKind.Transient => NotificationDeliveryRetry.Transient,
                SmtpTransportFailureKind.RateLimited => NotificationDeliveryRetry.RateLimited,
                _ => NotificationDeliveryRetry.Permanent,
            };
            return Failed(category);
        }
    }

    private static NotificationProviderResult Failed(string category) =>
        new(false, category, null, null);

    private static bool TryParseConfig(string json, out SmtpProviderConfig? config)
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

            if (!TryReadString(document.RootElement, "host", 253, out var host)
                || Uri.CheckHostName(host) == UriHostNameType.Unknown
                || !TryReadInteger(document.RootElement, "port", out var port)
                || port is < 1 or > 65535
                || !TryReadString(document.RootElement, "secureSocketMode", 32, out var socketMode)
                || !TryMapSecureSocketMode(socketMode, out var secureSocketMode)
                || !TryReadString(document.RootElement, "username", 320, out var username)
                || ContainsControlCharacter(username)
                || !TryReadString(document.RootElement, "fromAddress", 320, out var fromAddress)
                || !IsValidAddress(fromAddress))
            {
                return false;
            }

            string? fromDisplayName = null;
            if (document.RootElement.TryGetProperty("fromDisplayName", out var displayNameElement))
            {
                if (displayNameElement.ValueKind != JsonValueKind.String
                    || displayNameElement.GetString() is not { Length: > 0 and <= 128 } displayName
                    || ContainsControlCharacter(displayName))
                {
                    return false;
                }

                fromDisplayName = displayName;
            }

            config = new SmtpProviderConfig(
                host,
                port,
                secureSocketMode,
                username,
                fromAddress,
                fromDisplayName);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
            || parsed.Length > maxLength)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadInteger(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryMapSecureSocketMode(
        string value,
        out SmtpSecureSocketMode secureSocketMode)
    {
        secureSocketMode = value switch
        {
            "ssl_on_connect" => SmtpSecureSocketMode.SslOnConnect,
            "starttls" => SmtpSecureSocketMode.StartTls,
            _ => default,
        };
        return value is "ssl_on_connect" or "starttls";
    }

    private static bool IsValidAddress(string address)
    {
        var separator = address.LastIndexOf('@');
        return separator > 0
            && separator == address.IndexOf('@')
            && separator < address.Length - 1
            && Uri.CheckHostName(address[(separator + 1)..]) != UriHostNameType.Unknown
            && !ContainsControlCharacter(address)
            && MailboxAddress.TryParse(address, out var mailbox)
            && string.Equals(mailbox.Address, address, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsControlCharacter(string value) =>
        value.Any(char.IsControl);

    private sealed record SmtpProviderConfig(
        string Host,
        int Port,
        SmtpSecureSocketMode SecureSocketMode,
        string Username,
        string FromAddress,
        string? FromDisplayName);
}
