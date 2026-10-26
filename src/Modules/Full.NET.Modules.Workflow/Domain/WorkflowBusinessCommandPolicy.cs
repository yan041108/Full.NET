using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>业务节点只能选择已注册动作；真实目标和快照由根业务所有者授权确定。</summary>
internal sealed record WorkflowBusinessCommandPolicy(string ActionKey, int ActionVersion)
{
    public static bool TryRead(string kind, JsonElement config, out WorkflowBusinessCommandPolicy? policy)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object) return false;
        var properties = config.EnumerateObject().Where(item => item.NameEquals("businessCommandPolicy")).ToArray();
        var hashes = config.EnumerateObject().Where(item => item.NameEquals("boundBusinessActionHash")).ToArray();
        if (kind != "business.command") return properties.Length == 0 && hashes.Length == 0;
        if (config.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != config.EnumerateObject().Count()) return false;
        if (properties.Length != 1 || properties[0].Value.ValueKind != JsonValueKind.Object || hashes.Length > 1) return false;
        if (hashes.Length == 1 && !ValidHash(hashes[0].Value)) return false;
        var fields = properties[0].Value.EnumerateObject().ToArray();
        if (fields.Length != 2 || fields.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
            fields.Any(item => item.Name is not ("actionKey" or "actionVersion"))) return false;
        var key = properties[0].Value.GetProperty("actionKey"); var version = properties[0].Value.GetProperty("actionVersion");
        if (key.ValueKind != JsonValueKind.String || !ValidKey(key.GetString()) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number is < 1 or > 65535) return false;
        policy = new(key.GetString()!, number); return true;
    }
    internal static bool ValidKey(string? value) => value is { Length: >= 1 and <= 64 } && char.IsAsciiLetterLower(value[0]) &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '.' or '-');
    internal static bool ValidHash(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 64 } hash &&
        hash.All(char.IsAsciiHexDigitLower);
}
