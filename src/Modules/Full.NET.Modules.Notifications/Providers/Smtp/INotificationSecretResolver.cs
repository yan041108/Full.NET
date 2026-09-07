using Microsoft.Extensions.Configuration;

namespace Full.NET.Modules.Notifications.Providers.Smtp;

/// <summary>在投递瞬间解析外部 Secret 引用；实现不得缓存、记录或回显明文。</summary>
internal interface INotificationSecretResolver
{
    /// <summary>只解析运维为指定提供程序明确登记的引用，未知引用失败关闭。</summary>
    /// <param name="providerTypeKey">由适配器指定的稳定提供程序键，不能取自请求正文。</param>
    /// <param name="secretReference">数据库保存的外部秘密引用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<string?> ResolveAsync(
        string providerTypeKey,
        string? secretReference,
        CancellationToken cancellationToken);
}

/// <summary>只解析运维按提供程序登记的 env 引用，避免数据库配置越过进程秘密边界。</summary>
/// <param name="configuration">受信任的进程配置，提供程序专用引用登记不来自数据库。</param>
internal sealed class EnvironmentNotificationSecretResolver(IConfiguration configuration) : INotificationSecretResolver
{
    private const string Scheme = "env://";

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(
        string providerTypeKey,
        string? secretReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(providerTypeKey)
            || providerTypeKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_' && character != '.' && character != '-')
            || secretReference is null
            || !secretReference.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var variableName = secretReference[Scheme.Length..];
        if (!IsValidVariableName(variableName))
        {
            return ValueTask.FromResult<string?>(null);
        }

        // 完整引用逐字匹配；变量名前缀不能替代授权，也不能借其他渠道的登记读取秘密。
        var registered = configuration.GetSection($"Notifications:SecretReferences:{providerTypeKey}")
            .GetChildren()
            .Any(entry => string.Equals(entry.Value, secretReference, StringComparison.Ordinal));
        if (!registered)
        {
            return ValueTask.FromResult<string?>(null);
        }

        var value = Environment.GetEnvironmentVariable(variableName);
        return ValueTask.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>限制环境变量名格式，拒绝路径、配置节分隔符和空引用。</summary>
    /// <param name="value">待验证的变量名。</param>
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
