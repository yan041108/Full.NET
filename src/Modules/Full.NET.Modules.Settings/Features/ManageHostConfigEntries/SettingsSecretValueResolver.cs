using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Features.ManageHostConfigEntries;

/// <summary>按 ConfigKey 解析 Host secret 配置项明文，供跨模块 Contract Port 消费。</summary>
internal sealed class SettingsSecretValueResolver(IQueryExecutor queryExecutor)
    : ISettingsSecretValueResolver
{
    public async Task<Result<string>> ResolveSecretValueAsync(
        string configKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = configKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedKey.Length == 0)
        {
            return Failure(SettingsErrorCodes.ConfigEntrySecretUnavailable);
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntrySecretRecord>(
                ConfigEntrySql.FindSecretByKey,
                new { ConfigKey = normalizedKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null
            || !record.IsActive
            || !string.Equals(
                record.ValueKind,
                ConfigValueKinds.Secret,
                StringComparison.Ordinal))
        {
            return Failure(SettingsErrorCodes.ConfigEntrySecretUnavailable);
        }

        if (string.IsNullOrEmpty(record.Value))
        {
            return Failure(SettingsErrorCodes.ConfigEntrySecretUnavailable);
        }

        return Result<string>.Success(record.Value);
    }

    private static Result<string> Failure(string code) =>
        Result<string>.Failure(new Error(
            code,
            code,
            ErrorType.Validation));
}

internal sealed class ConfigEntrySecretRecord
{
    public string ValueKind { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
