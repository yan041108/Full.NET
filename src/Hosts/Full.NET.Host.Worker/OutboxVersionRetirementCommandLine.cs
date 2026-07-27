using System.Globalization;

namespace Full.NET.Host.Worker;

/// <summary>
/// 表示一次旧版本退役扫描的精确消息路由。
/// </summary>
/// <param name="MessageType">仍由当前 Handler 声明的消息类型。</param>
/// <param name="SchemaVersion">准备退役的正整数结构版本。</param>
internal sealed record OutboxVersionRetirementRequest(
    string MessageType,
    int SchemaVersion);

/// <summary>
/// 封闭 Worker 专用参数，并保留需要继续交给通用 Host 的参数。
/// </summary>
/// <param name="VersionRetirement">一次性退役扫描；为空时按普通 Worker 启动。</param>
/// <param name="HostArguments">已剥离专用参数的通用 Host 参数。</param>
internal sealed record OutboxWorkerCommandLineOptions(
    OutboxVersionRetirementRequest? VersionRetirement,
    IReadOnlyList<string> HostArguments);

/// <summary>
/// 集中声明旧版本退役扫描的稳定机器码。
/// </summary>
internal static class OutboxVersionRetirementErrorCodes
{
    public const string CommandInvalid =
        "outbox.version_retirement.command_invalid";

    public const string HandlerNotFound =
        "outbox.version_retirement.handler_not_found";

    public const string AmbiguousHandler =
        "outbox.version_retirement.ambiguous_handler";

    public const string Safe = "outbox.version_retirement.safe";

    public const string Blocked = "outbox.version_retirement.blocked";
}

/// <summary>
/// 表示已归类且可安全输出的退役扫描失败。
/// </summary>
internal sealed class OutboxVersionRetirementException : Exception
{
    public OutboxVersionRetirementException(string code)
        : base(code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// 在 Host 构建前解析并移除旧版本退役扫描的专用参数。
/// </summary>
internal static class OutboxVersionRetirementCommandLine
{
    private const string MessageTypeOption =
        "--outbox-version-retirement-message-type";

    private const string SchemaVersionOption =
        "--outbox-version-retirement-schema-version";

    public static OutboxWorkerCommandLineOptions Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? messageType = null;
        int? schemaVersion = null;
        var hostArguments = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(
                argument,
                MessageTypeOption,
                StringComparison.OrdinalIgnoreCase))
            {
                EnsureNotSelected(messageType is not null);
                var value = ReadValue(arguments, ref index);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw InvalidCommand();
                }

                messageType = value;
                continue;
            }

            if (string.Equals(
                argument,
                SchemaVersionOption,
                StringComparison.OrdinalIgnoreCase))
            {
                EnsureNotSelected(schemaVersion.HasValue);
                var value = ReadValue(arguments, ref index);
                if (!int.TryParse(
                        value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                    || parsed < 1)
                {
                    throw InvalidCommand();
                }

                schemaVersion = parsed;
                continue;
            }

            if (argument.StartsWith(
                    $"{MessageTypeOption}=",
                    StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith(
                    $"{SchemaVersionOption}=",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidCommand();
            }

            hostArguments.Add(argument);
        }

        if ((messageType is null) != !schemaVersion.HasValue)
        {
            throw InvalidCommand();
        }

        var request = messageType is null
            ? null
            : new OutboxVersionRetirementRequest(
                messageType,
                schemaVersion!.Value);
        return new OutboxWorkerCommandLineOptions(request, hostArguments);
    }

    private static string ReadValue(
        IReadOnlyList<string> arguments,
        ref int index)
    {
        if (index + 1 >= arguments.Count
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw InvalidCommand();
        }

        return arguments[++index];
    }

    private static void EnsureNotSelected(bool selected)
    {
        if (selected)
        {
            throw InvalidCommand();
        }
    }

    private static OutboxVersionRetirementException InvalidCommand() =>
        new(OutboxVersionRetirementErrorCodes.CommandInvalid);
}
