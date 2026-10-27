namespace Full.NET.Data.Abstractions;

/// <summary>当前编译版本要求的迁移清单；生成应用按与 Migrator 相同的冻结清单收窄。</summary>
public static class DatabaseSchemaRequirements
{
    /// <summary>读取当前 Provider 的必需 DbUp 脚本名；缺失资源或非法预设均失败关闭。</summary>
    /// <param name="provider">正式数据库提供程序。</param>
    /// <param name="options">应用冻结迁移清单的位置。</param>
    /// <returns>必须已经在数据库记账的完整脚本名。</returns>
    public static IReadOnlyCollection<string> Read(DatabaseProvider provider, FrameworkManifestMigrationOptions options)
    {
        var name = provider switch { DatabaseProvider.SqlServer => "SqlServer", DatabaseProvider.MySql => "MySql",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)) };
        using var stream = typeof(DatabaseSchemaRequirements).Assembly.GetManifestResourceStream($"Full.NET.Schema.{name}")
            ?? throw new InvalidOperationException("Database schema requirements are missing.");
        using var reader = new StreamReader(stream);
        var available = reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var selected = FrameworkManifestMigrationScope.TryLoadAllowedScriptNames(options) ?? available;
        if (selected.Count == 0 || selected.Any(script => !available.Contains(script)))
            throw new InvalidOperationException("Database schema requirements do not match the application manifest.");
        return selected.Select(script => $"Full.NET.Migrations.DbUp.Migrations.{name}.{script}").ToArray();
    }
}
