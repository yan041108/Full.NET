using Full.NET.Seeding.Abstractions;

namespace Full.NET.Seeding.Dapper;

/// <summary>
/// 描述 Migrator 是否应显式播种，以及是否使用了待退役的兼容别名。
/// </summary>
/// <param name="Profile">目标 Profile；为空表示只迁移、不播种。</param>
/// <param name="UsesLegacyAlias">是否由 <c>--seed-local</c> 映射到 Development。</param>
public sealed record SeedCommandLineOptions(
    SeedProfile? Profile,
    bool UsesLegacyAlias);

/// <summary>
/// 在宿主构建和数据库写入前解析封闭的 Seed CLI 参数集合。
/// </summary>
public static class SeedCommandLine
{
    /// <summary>
    /// 解析 <c>--seed &lt;profile&gt;</c> 或兼容的 <c>--seed-local</c>；其他宿主参数保持透传。
    /// </summary>
    /// <param name="arguments">Migrator 收到的原始参数。</param>
    /// <returns>封闭且类型化的 Seed 选择。</returns>
    /// <exception cref="SeedConfigurationException">Seed 参数重复、缺值、混用或包含未知 Profile。</exception>
    public static SeedCommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        SeedProfile? selectedProfile = null;
        var usesLegacyAlias = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--seed-local", StringComparison.OrdinalIgnoreCase))
            {
                EnsureNotSelected(selectedProfile);
                selectedProfile = SeedProfile.Development;
                usesLegacyAlias = true;
                continue;
            }

            if (string.Equals(argument, "--seed", StringComparison.OrdinalIgnoreCase))
            {
                EnsureNotSelected(selectedProfile);
                if (index + 1 >= arguments.Count
                    || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new SeedConfigurationException(SeedErrorCodes.CommandInvalid);
                }

                var value = arguments[++index];
                if (!SeedProfileNames.TryParse(value, out var profile))
                {
                    throw new SeedConfigurationException(SeedErrorCodes.CommandInvalid);
                }

                selectedProfile = profile;
                continue;
            }

            if (argument.StartsWith("--seed=", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--seed-local=", StringComparison.OrdinalIgnoreCase))
            {
                throw new SeedConfigurationException(SeedErrorCodes.CommandInvalid);
            }
        }

        return new SeedCommandLineOptions(selectedProfile, usesLegacyAlias);
    }

    private static void EnsureNotSelected(SeedProfile? selectedProfile)
    {
        if (selectedProfile.HasValue)
        {
            throw new SeedConfigurationException(SeedErrorCodes.CommandInvalid);
        }
    }
}
