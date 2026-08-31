using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>规范化 Profile 非密钥配置与 Secret Reference；禁止把密钥字段写入配置 JSON。</summary>
internal static class NotificationProfileCompiler
{
    public const string SecretConfigured = "configured";
    public const string SecretNotConfigured = "not-configured";
    public const int MaxSecretReferenceLength = 256;
    public const int MaxConfigJsonLength = 4000;

    private static readonly HashSet<string> DispatchModes = new(StringComparer.Ordinal)
    {
        "single",
        "fan_out",
        "failover",
        "match",
    };

    public static Result<string> NormalizeSecretReference(string? secretReference)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return Result<string>.Success(string.Empty);
        }

        var normalized = secretReference.Trim();
        if (normalized.Length > MaxSecretReferenceLength
            || normalized.Any(char.IsWhiteSpace)
            || normalized.Contains("://", StringComparison.Ordinal) is false)
        {
            return Result<string>.Failure(ProfileValidation("The secret reference is invalid."));
        }

        return Result<string>.Success(normalized);
    }

    public static Result<string> NormalizeNonSecretConfig(
        NotificationProviderTypeDescriptor descriptor,
        JsonElement config)
    {
        if (config.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            config = JsonDocument.Parse("{}").RootElement.Clone();
        }

        if (config.ValueKind != JsonValueKind.Object)
        {
            return Result<string>.Failure(ProfileValidation("The non-secret config is invalid."));
        }

        var secretNames = new HashSet<string>(descriptor.SecretFieldKeys, StringComparer.Ordinal);
        var allowed = descriptor.NonSecretFields.ToDictionary(
            field => field.Name,
            field => field,
            StringComparer.Ordinal);
        var provided = new HashSet<string>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var property in config.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (secretNames.Contains(property.Name) || !allowed.TryGetValue(property.Name, out var field))
                {
                    return Result<string>.Failure(ProfileValidation("The non-secret config is invalid."));
                }

                if (!ValueMatches(field.TypeKey, property.Value))
                {
                    return Result<string>.Failure(ProfileValidation("The non-secret config is invalid."));
                }

                provided.Add(property.Name);
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        foreach (var field in descriptor.NonSecretFields.Where(item => item.Required))
        {
            if (!provided.Contains(field.Name))
            {
                return Result<string>.Failure(ProfileValidation("The non-secret config is invalid."));
            }
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        return json.Length > MaxConfigJsonLength
            ? Result<string>.Failure(ProfileValidation("The non-secret config is invalid."))
            : Result<string>.Success(json);
    }

    public static string ComputeProfileHash(
        string providerTypeKey,
        string adapterVersion,
        string nonSecretConfigJson,
        string secretReference) =>
        ToHash($"{providerTypeKey}\n{adapterVersion}\n{nonSecretConfigJson}\n{secretReference}");

    public static Result<string> NormalizeDispatchMode(string? dispatchModeKey)
    {
        var normalized = dispatchModeKey?.Trim() ?? string.Empty;
        return DispatchModes.Contains(normalized)
            ? Result<string>.Success(normalized)
            : Result<string>.Failure(BindingValidation("The dispatch mode is invalid."));
    }

    public static Result<string> WriteBindingDraftJson(
        string producerKey,
        string sceneKey,
        string channelKey,
        IReadOnlyList<NotificationBindingTargetInput> targets)
    {
        if (targets.Count is < 1 or > 16)
        {
            return Result<string>.Failure(BindingValidation("The binding target list is invalid."));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<NotificationBindingTargetInput>(targets.Count);
        foreach (var target in targets.OrderBy(item => item.Order).ThenBy(item => item.ProfileKey, StringComparer.Ordinal))
        {
            var profileKey = NotificationTemplateCompiler.NormalizeStableKey(target.ProfileKey, "ProfileKey");
            if (!profileKey.IsSuccess)
            {
                return Result<string>.Failure(profileKey.Error!);
            }

            if (target.Order < 1 || !keys.Add(profileKey.Value!))
            {
                return Result<string>.Failure(BindingValidation("The binding target list is invalid."));
            }

            normalized.Add(new NotificationBindingTargetInput(profileKey.Value!, target.Order));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("producerKey", producerKey);
            writer.WriteString("sceneKey", sceneKey);
            writer.WriteString("channelKey", channelKey);
            writer.WritePropertyName("targets");
            writer.WriteStartArray();
            foreach (var target in normalized)
            {
                writer.WriteStartObject();
                writer.WriteString("profileKey", target.ProfileKey);
                writer.WriteNumber("order", target.Order);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Result<string>.Success(Encoding.UTF8.GetString(stream.ToArray()));
    }

    public static string ComputeBindingHash(
        string producerKey,
        string sceneKey,
        string channelKey,
        string dispatchModeKey,
        string targetsJson) =>
        ToHash($"{producerKey}\n{sceneKey}\n{channelKey}\n{dispatchModeKey}\n{targetsJson}");

    public static string SecretStatus(string? secretReference) =>
        string.IsNullOrEmpty(secretReference) ? SecretNotConfigured : SecretConfigured;

    private static bool ValueMatches(string typeKey, JsonElement value) =>
        typeKey switch
        {
            "string" => value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 and <= 512 },
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false,
        };

    private static string ToHash(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Error ProfileValidation(string message) =>
        new(NotificationsErrorCodes.ProviderProfileValidationFailed, message, ErrorType.Validation);

    private static Error BindingValidation(string message) =>
        new(NotificationsErrorCodes.BindingValidationFailed, message, ErrorType.Validation);
}
