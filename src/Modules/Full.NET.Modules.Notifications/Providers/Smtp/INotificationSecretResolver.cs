namespace Full.NET.Modules.Notifications.Providers.Smtp;

/// <summary>在投递瞬间解析外部 Secret 引用；实现不得缓存、记录或回显明文。</summary>
internal interface INotificationSecretResolver
{
    ValueTask<string?> ResolveAsync(
        string? secretReference,
        CancellationToken cancellationToken);
}

/// <summary>只允许解析 <c>env://NAME</c>，避免数据库配置演变为明文密钥容器。</summary>
internal sealed class EnvironmentNotificationSecretResolver : INotificationSecretResolver
{
    private const string Scheme = "env://";

    public ValueTask<string?> ResolveAsync(
        string? secretReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (secretReference is null
            || !secretReference.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var variableName = secretReference[Scheme.Length..];
        if (!IsValidVariableName(variableName))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var value = Environment.GetEnvironmentVariable(variableName);
        return ValueTask.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    private static bool IsValidVariableName(string value)
    {
        if (value.Length is < 1 or > 128
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
