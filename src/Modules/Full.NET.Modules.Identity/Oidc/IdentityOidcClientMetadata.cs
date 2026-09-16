using System.Collections.Immutable;
using System.Text.Json;
using OpenIddict.Abstractions;
using Full.NET.Modules.Identity.Serialization;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 客户端扩展元数据在 OpenIddict Properties 中的键与读写辅助。</summary>
internal static class IdentityOidcClientMetadata
{
    public const string IsFirstPartyKey = "fullnet_is_first_party";
    public const string ResourceAudienceKey = "fullnet_resource_audience";
    public const string DisabledKey = "fullnet_disabled";

    internal static void Write(
        OpenIddictApplicationDescriptor descriptor,
        bool isFirstParty,
        string? resourceAudience,
        bool isDisabled)
    {
        descriptor.Properties[IsFirstPartyKey] = JsonSerializer.SerializeToElement(isFirstParty, IdentityOidcStoreJsonContext.Default.Boolean);
        if (string.IsNullOrWhiteSpace(resourceAudience))
        {
            descriptor.Properties.Remove(ResourceAudienceKey);
        }
        else
        {
            descriptor.Properties[ResourceAudienceKey] =
                JsonSerializer.SerializeToElement(resourceAudience.Trim(), IdentityOidcStoreJsonContext.Default.String);
        }

        descriptor.Properties[DisabledKey] = JsonSerializer.SerializeToElement(isDisabled, IdentityOidcStoreJsonContext.Default.Boolean);
    }

    internal static IdentityOidcClientConfig Read(
        ImmutableDictionary<string, JsonElement> properties) =>
        new(
            ReadBoolean(properties, IsFirstPartyKey, true),
            ReadString(properties, ResourceAudienceKey),
            ReadBoolean(properties, DisabledKey, false));

    private static bool ReadBoolean(
        ImmutableDictionary<string, JsonElement> properties,
        string key,
        bool defaultValue)
    {
        if (!properties.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => defaultValue,
        };
    }

    private static string? ReadString(
        ImmutableDictionary<string, JsonElement> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

internal sealed record IdentityOidcClientConfig(
    bool IsFirstParty,
    string? ResourceAudience,
    bool IsDisabled);