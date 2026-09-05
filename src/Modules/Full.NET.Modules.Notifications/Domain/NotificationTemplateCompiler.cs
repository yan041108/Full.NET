using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>
/// 将模板草稿规范为可哈希、可校验的闭合 Schema 与正文，并按占位符做简单替换。
/// </summary>
/// <remarks>
/// 不执行表达式或脚本；未知参数、缺失必填、类型不匹配和超限一律失败关闭。
/// 错误消息只使用泛化英文，禁止回显参数值、模板全文或用户标识。
/// </remarks>
internal static partial class NotificationTemplateCompiler
{
    public const int MaxParameters = 32;
    public const int MaxRecipients = 20;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 4000;
    public const int SchemaVersion = 1;
    public const string InboxChannelKey = "inbox";
    public const string RecipientTypeUser = "user";
    public const string DispatchModeSingle = "single";
    public const string IntentStatusAccepted = "accepted";
    public const string EmptyRouteSnapshotJson = "[]";

    private static readonly HashSet<string> ContentCategories = new(StringComparer.Ordinal)
    {
        "mandatory",
        "transactional",
        "informational",
        "marketing",
    };

    private static readonly HashSet<string> Classifications = new(StringComparer.Ordinal)
    {
        "c0",
        "c1",
        "s2",
    };

    private static readonly HashSet<string> ParameterTypes = new(StringComparer.Ordinal)
    {
        "string",
        "integer",
        "boolean",
    };

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static Result<string> NormalizeStableKey(string? value, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 128)
        {
            return Result<string>.Failure(TemplateValidation(
                $"{fieldName} must be between 1 and 128 characters."));
        }

        if (normalized.Any(character => char.IsWhiteSpace(character) || character is '/' or '\\'))
        {
            return Result<string>.Failure(TemplateValidation($"{fieldName} is invalid."));
        }

        return Result<string>.Success(normalized);
    }

    public static Result<string> NormalizeChannel(
        string? channelKey,
        INotificationProviderTypeCatalog catalog)
    {
        var normalized = channelKey?.Trim() ?? string.Empty;
        if (string.Equals(normalized, InboxChannelKey, StringComparison.Ordinal))
        {
            return Result<string>.Success(InboxChannelKey);
        }

        return catalog.SupportsChannel(normalized)
            ? Result<string>.Success(normalized)
            : Result<string>.Failure(new Error(
                NotificationsErrorCodes.IntentChannelUnsupported,
                "Only the inbox channel or a registered provider channel is supported.",
                ErrorType.Validation));
    }

    public static Result<string> NormalizeContentCategory(string? contentCategoryKey)
    {
        var normalized = contentCategoryKey?.Trim() ?? string.Empty;
        return ContentCategories.Contains(normalized)
            ? Result<string>.Success(normalized)
            : Result<string>.Failure(TemplateValidation("The content category is invalid."));
    }

    public static Result<string> NormalizeClassification(string? contentClassificationKey)
    {
        var normalized = contentClassificationKey?.Trim() ?? string.Empty;
        return Classifications.Contains(normalized)
            ? Result<string>.Success(normalized)
            : Result<string>.Failure(TemplateValidation("The content classification is invalid."));
    }

    public static Result<NormalizedTemplateDraft> NormalizeDraft(
        string? subject,
        NotificationTemplateBody? body,
        NotificationTemplateParameterSchema? schema)
    {
        var normalizedSubject = subject?.Trim() ?? string.Empty;
        if (normalizedSubject.Length is < 1 or > MaxSubjectLength)
        {
            return Result<NormalizedTemplateDraft>.Failure(
                TemplateValidation("The template subject is invalid."));
        }

        var text = body?.Text?.Trim() ?? string.Empty;
        if (text.Length is < 1 or > MaxBodyLength)
        {
            return Result<NormalizedTemplateDraft>.Failure(
                TemplateValidation("The template body is invalid."));
        }

        var schemaResult = NormalizeSchema(schema);
        if (!schemaResult.IsSuccess)
        {
            return Result<NormalizedTemplateDraft>.Failure(schemaResult.Error!);
        }

        var parameterNames = schemaResult.Value!.Parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var placeholder in CollectPlaceholders(normalizedSubject).Concat(CollectPlaceholders(text)))
        {
            if (!parameterNames.Contains(placeholder))
            {
                return Result<NormalizedTemplateDraft>.Failure(
                    TemplateValidation("The template contains an unknown placeholder."));
            }
        }

        return Result<NormalizedTemplateDraft>.Success(new NormalizedTemplateDraft(
            normalizedSubject,
            WriteBodyJson(text),
            schemaResult.Value.CanonicalJson,
            schemaResult.Value));
    }

    public static Result<NormalizedParameterSchema> NormalizeSchema(
        NotificationTemplateParameterSchema? schema)
    {
        if (schema is null || schema.SchemaVersion != SchemaVersion)
        {
            return Result<NormalizedParameterSchema>.Failure(
                TemplateValidation("The parameter schema version is invalid."));
        }

        if (schema.Parameters is null
            || schema.Parameters.Count is < 1 or > MaxParameters)
        {
            return Result<NormalizedParameterSchema>.Failure(
                TemplateValidation("The parameter schema size is invalid."));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<NotificationTemplateParameterDefinition>(schema.Parameters.Count);
        foreach (var parameter in schema.Parameters)
        {
            var name = parameter.Name?.Trim() ?? string.Empty;
            if (!ParameterNamePattern().IsMatch(name) || !seen.Add(name))
            {
                return Result<NormalizedParameterSchema>.Failure(
                    TemplateValidation("The parameter schema names are invalid."));
            }

            var typeKey = parameter.TypeKey?.Trim() ?? string.Empty;
            if (!ParameterTypes.Contains(typeKey))
            {
                return Result<NormalizedParameterSchema>.Failure(
                    TemplateValidation("The parameter schema types are invalid."));
            }

            int? maxLength = null;
            if (string.Equals(typeKey, "string", StringComparison.Ordinal))
            {
                if (parameter.MaxLength is not int length || length is < 1 or > 512)
                {
                    return Result<NormalizedParameterSchema>.Failure(
                        TemplateValidation("String parameters require an explicit maxLength."));
                }

                maxLength = length;
            }
            else if (parameter.MaxLength is not null)
            {
                return Result<NormalizedParameterSchema>.Failure(
                    TemplateValidation("Non-string parameters must not declare maxLength."));
            }

            normalized.Add(new NotificationTemplateParameterDefinition(
                name,
                typeKey,
                parameter.Required,
                maxLength));
        }

        normalized.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return Result<NormalizedParameterSchema>.Success(
            new NormalizedParameterSchema(WriteSchemaJson(normalized), normalized));
    }

    public static string ComputeContentHash(
        string localeTag,
        string subject,
        string bodyJson,
        string parameterSchemaJson,
        string contentClassificationKey)
    {
        var payload = $"{SchemaVersion}\n{localeTag}\n{subject}\n{bodyJson}\n{parameterSchemaJson}\n{contentClassificationKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static Result<string> ValidateAndSnapshotParameters(
        NormalizedParameterSchema schema,
        JsonElement parameters)
    {
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            parameters = EmptyObject;
        }

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return Result<string>.Failure(ParameterInvalid());
        }

        var provided = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in parameters.EnumerateObject())
        {
            provided.Add(property.Name);
            var definition = schema.Parameters.FirstOrDefault(item =>
                string.Equals(item.Name, property.Name, StringComparison.Ordinal));
            if (definition is null || !ValueMatches(definition, property.Value))
            {
                return Result<string>.Failure(ParameterInvalid());
            }
        }

        if (schema.Parameters.Any(item => item.Required && !provided.Contains(item.Name)))
        {
            return Result<string>.Failure(ParameterInvalid());
        }

        return Result<string>.Success(WriteParameterSnapshot(schema, parameters));
    }

    public static Result<RenderedNotification> Render(
        string subject,
        string bodyJson,
        string parameterSnapshotJson)
    {
        using var bodyDocument = JsonDocument.Parse(bodyJson);
        if (!bodyDocument.RootElement.TryGetProperty("text", out var textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            return Result<RenderedNotification>.Failure(TemplateValidation("The template body is invalid."));
        }

        using var snapshot = JsonDocument.Parse(parameterSnapshotJson);
        var replacements = snapshot.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, FormatValue, StringComparer.Ordinal);
        var renderedSubject = ReplacePlaceholders(subject, replacements);
        var renderedBody = ReplacePlaceholders(textElement.GetString() ?? string.Empty, replacements);
        if (renderedSubject.Length is < 1 or > MaxSubjectLength
            || renderedBody.Length is < 1 or > MaxBodyLength)
        {
            return Result<RenderedNotification>.Failure(
                TemplateValidation("The rendered inbox content is invalid."));
        }

        return Result<RenderedNotification>.Success(new RenderedNotification(renderedSubject, renderedBody));
    }

    public static bool PayloadsMatch(
        Guid templateVersionId,
        string sceneKey,
        string parameterSnapshotJson,
        IReadOnlyList<NotificationRecipientInput> recipients,
        NotificationIntentRecordSnapshot existing)
    {
        if (existing.TemplateVersionId != templateVersionId
            || !string.Equals(existing.SceneKey, sceneKey, StringComparison.Ordinal)
            || !string.Equals(existing.ParameterSnapshotJson, parameterSnapshotJson, StringComparison.Ordinal))
        {
            return false;
        }

        var incoming = CanonicalRecipients(recipients);
        var stored = CanonicalRecipients(existing.Recipients);
        return incoming.SequenceEqual(stored, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> CanonicalRecipients(
        IReadOnlyList<NotificationRecipientInput> recipients) =>
        recipients
            .Select(recipient => $"{recipient.RecipientTypeKey}\u001f{recipient.RecipientKey}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IEnumerable<string> CollectPlaceholders(string text) =>
        PlaceholderPattern()
            .Matches(text)
            .Select(match => match.Groups[1].Value);

    private static bool ValueMatches(
        NotificationTemplateParameterDefinition definition,
        JsonElement value)
    {
        return definition.TypeKey switch
        {
            "string" when value.ValueKind == JsonValueKind.String =>
                value.GetString() is { Length: > 0 } text
                && text.Length <= definition.MaxLength,
            "integer" when value.ValueKind == JsonValueKind.Number =>
                value.TryGetInt64(out _),
            "boolean" when value.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                true,
            _ => false,
        };
    }

    private static string FormatValue(JsonProperty property) =>
        property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
            JsonValueKind.Number when property.Value.TryGetInt64(out var number) =>
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };

    private static string ReplacePlaceholders(
        string template,
        IReadOnlyDictionary<string, string> replacements)
    {
        return PlaceholderPattern().Replace(
            template,
            match => replacements.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : match.Value);
    }

    private static string WriteBodyJson(string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteSchemaJson(IReadOnlyList<NotificationTemplateParameterDefinition> parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WritePropertyName("parameters");
            writer.WriteStartArray();
            foreach (var parameter in parameters)
            {
                writer.WriteStartObject();
                writer.WriteString("name", parameter.Name);
                writer.WriteString("typeKey", parameter.TypeKey);
                writer.WriteBoolean("required", parameter.Required);
                if (parameter.MaxLength is int maxLength)
                {
                    writer.WriteNumber("maxLength", maxLength);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteParameterSnapshot(
        NormalizedParameterSchema schema,
        JsonElement parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var definition in schema.Parameters)
            {
                if (!parameters.TryGetProperty(definition.Name, out var value))
                {
                    continue;
                }

                writer.WritePropertyName(definition.Name);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Error TemplateValidation(string message) =>
        new(NotificationsErrorCodes.TemplateValidationFailed, message, ErrorType.Validation);

    private static Error ParameterInvalid() =>
        new(
            NotificationsErrorCodes.TemplateParameterInvalid,
            "The template parameters are invalid.",
            ErrorType.Validation);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}

/// <summary>规范化后的草稿快照，可直接写入 Draft 列并参与发布哈希。</summary>
internal sealed record NormalizedTemplateDraft(
    string Subject,
    string BodyJson,
    string ParameterSchemaJson,
    NormalizedParameterSchema Schema);

/// <summary>规范化参数 Schema 与按名称排序后的稳定 JSON。</summary>
internal sealed record NormalizedParameterSchema(
    string CanonicalJson,
    IReadOnlyList<NotificationTemplateParameterDefinition> Parameters);

/// <summary>替换占位符后的 Inbox 标题与正文。</summary>
internal sealed record RenderedNotification(string Title, string Content);

/// <summary>幂等比较所需的已存在 Intent 快照，避免把持久化 Record 泄漏到比较逻辑。</summary>
internal sealed record NotificationIntentRecordSnapshot(
    Guid TemplateVersionId,
    string SceneKey,
    string ParameterSnapshotJson,
    IReadOnlyList<NotificationRecipientInput> Recipients);
