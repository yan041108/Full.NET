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
    public const string RequireMfaKey = "fullnet_require_mfa";
    public const string BackchannelLogoutUriKey = "backchannel_logout_uri";

    internal static void Write(
        OpenIddictApplicationDescriptor descriptor,
        bool isFirstParty,
        string? resourceAudience,
        bool isDisabled,
        bool? requireMfa = null,
        string? backchannelLogoutUri = null)
    {
        // 资源权限跟随当前登记更新，防止收紧或更换受众后保留旧资源权限。
        var previousResource = ReadString(descriptor.Properties.ToImmutableDictionary(), ResourceAudienceKey);
        if (Uri.TryCreate(previousResource, UriKind.Absolute, out _)) descriptor.RemoveResourcePermissions(previousResource!);
        if (Uri.TryCreate(resourceAudience, UriKind.Absolute, out _)) descriptor.AddResourcePermissions(resourceAudience!.Trim());
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
        if (requireMfa.HasValue)
            descriptor.Properties[RequireMfaKey] = JsonSerializer.SerializeToElement(requireMfa.Value, IdentityOidcStoreJsonContext.Default.Boolean);
        if (backchannelLogoutUri is not null)
        {
            if (string.IsNullOrWhiteSpace(backchannelLogoutUri)) descriptor.Properties.Remove(BackchannelLogoutUriKey);
            else descriptor.Properties[BackchannelLogoutUriKey] = JsonSerializer.SerializeToElement(backchannelLogoutUri, IdentityOidcStoreJsonContext.Default.String);
        }
    }

    internal static IdentityOidcClientConfig Read(
        ImmutableDictionary<string, JsonElement> properties) =>
        new(
            ReadBoolean(properties, IsFirstPartyKey, false),
            ReadString(properties, ResourceAudienceKey),
            ReadBoolean(properties, DisabledKey, false),
            ReadBoolean(properties, RequireMfaKey, false), ReadString(properties, BackchannelLogoutUriKey));

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
    bool IsDisabled,
    bool RequireMfa = false,
    string? BackchannelLogoutUri = null);
