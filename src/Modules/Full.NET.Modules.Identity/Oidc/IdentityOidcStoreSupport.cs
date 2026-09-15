using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Text.Json;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictExceptions;

namespace Full.NET.Modules.Identity.Oidc;

internal static class IdentityOidcStoreSupport
{
    internal const string SessionIdProperty = "fn:session_id";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    internal static NotSupportedException LinqNotSupported([CallerMemberName] string? member = null) =>
        new($"OpenIddict Dapper store does not support LINQ-based {member}; use explicit SQL-backed methods.");

    internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IReadOnlyList<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.CompletedTask;
        }
    }

    internal static Guid ParseRequiredId(string identifier)
    {
        if (!Guid.TryParse(identifier, out var id))
        {
            throw new InvalidOperationException($"OIDC entity identifier '{identifier}' is not a valid GUID.");
        }

        return id;
    }

    internal static Guid? ParseOptionalId(string? identifier) =>
        string.IsNullOrWhiteSpace(identifier) ? null : ParseRequiredId(identifier);

    internal static string FormatId(Guid id) => id.ToString("D");

    internal static string SerializeStringArray(ImmutableArray<string> values) =>
        JsonSerializer.Serialize(values.IsDefault ? Array.Empty<string>() : values.ToArray(), JsonOptions);

    internal static ImmutableArray<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableArray<string>.Empty;
        }

        return JsonSerializer.Deserialize<string[]>(json, JsonOptions)?.ToImmutableArray()
            ?? ImmutableArray<string>.Empty;
    }

    internal static string SerializeDisplayNames(ImmutableDictionary<CultureInfo, string> names)
    {
        var payload = names.IsEmpty
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : names.ToDictionary(pair => pair.Key.Name, pair => pair.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    internal static ImmutableDictionary<CultureInfo, string> DeserializeDisplayNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableDictionary<CultureInfo, string>.Empty;
        }

        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        if (payload is null || payload.Count == 0)
        {
            return ImmutableDictionary<CultureInfo, string>.Empty;
        }

        return payload.ToImmutableDictionary(
            pair => CultureInfo.GetCultureInfo(pair.Key),
            pair => pair.Value);
    }

    internal static string SerializeProperties(ImmutableDictionary<string, JsonElement> properties) =>
        JsonSerializer.Serialize(
            properties.IsEmpty ? new Dictionary<string, JsonElement>(StringComparer.Ordinal) : properties.ToDictionary(),
            JsonOptions);

    internal static ImmutableDictionary<string, JsonElement> DeserializeProperties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableDictionary<string, JsonElement>.Empty;
        }

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions)
            ?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty;
    }

    internal static string SerializeSettings(ImmutableDictionary<string, string> settings) =>
        JsonSerializer.Serialize(
            settings.IsEmpty ? new Dictionary<string, string>(StringComparer.Ordinal) : settings.ToDictionary(),
            JsonOptions);

    internal static ImmutableDictionary<string, string> DeserializeSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, string>.Empty;
    }

    internal static string? SerializeJsonWebKeySet(JsonWebKeySet? set) =>
        set is null ? null : set.ToString();

    internal static JsonWebKeySet? DeserializeJsonWebKeySet(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : new JsonWebKeySet(json);

    internal static string? ReadSessionId(string? propertiesJson)
    {
        var properties = DeserializeProperties(propertiesJson);
        return properties.TryGetValue(SessionIdProperty, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    internal static string WriteSessionId(string? propertiesJson, string? sessionId)
    {
        var properties = DeserializeProperties(propertiesJson).ToBuilder();
        if (string.IsNullOrEmpty(sessionId))
        {
            properties.Remove(SessionIdProperty);
        }
        else
        {
            properties[SessionIdProperty] = JsonSerializer.SerializeToElement(sessionId);
        }

        return SerializeProperties(properties.ToImmutable());
    }

    internal static bool IsAuthorizationCodeRedemption(IdentityOidcToken token) =>
        string.Equals(token.Type, OpenIddictConstants.TokenTypeIdentifiers.Private.AuthorizationCode, StringComparison.Ordinal)
        && token.RedemptionDateUtc is not null;

    internal static void EnsureConcurrency(int affected)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException("The entity was concurrently updated or deleted.");
        }
    }
}