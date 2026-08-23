using System.Text.Json;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Serialization;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;

/// <summary>任务定义 Args 序列化与 API 脱敏映射。</summary>
internal static class HostJobDefinitionArgsMapper
{
    public static string? SerializeForStorage(string handlerKind, HttpJobArgs? args)
    {
        if (string.Equals(handlerKind, JobHandlerKinds.Ping, StringComparison.Ordinal))
        {
            return null;
        }

        return args is null
            ? null
            : JsonSerializer.Serialize(
                args,
                JobsJsonSerializerContext.Default.HttpJobArgs);
    }

    public static HttpJobArgs? DeserializeFromStorage(string handlerKind, string? argsJson)
    {
        if (string.Equals(handlerKind, JobHandlerKinds.Ping, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(argsJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize(
            argsJson,
            JobsJsonSerializerContext.Default.HttpJobArgs);
    }

    /// <summary>读 API 脱敏：secretHeaders 仅回显 configKey。</summary>
    public static HttpJobArgs? RedactForResponse(string handlerKind, string? argsJson)
    {
        var args = DeserializeFromStorage(handlerKind, argsJson);
        if (args?.SecretHeaders is null || args.SecretHeaders.Count == 0)
        {
            return args;
        }

        return args with
        {
            SecretHeaders = args.SecretHeaders.ToDictionary(
                pair => pair.Key,
                pair => new HttpJobSecretHeaderRef(pair.Value.ConfigKey),
                StringComparer.Ordinal),
        };
    }
}
