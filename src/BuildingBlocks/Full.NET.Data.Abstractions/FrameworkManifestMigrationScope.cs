using System.Text.Json;

namespace Full.NET.Data.Abstractions;

/// <summary>
/// 生成应用 <c>framework-manifest.json</c> 的迁移闭包配置。
/// </summary>
public sealed class FrameworkManifestMigrationOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:FrameworkManifest";

    /// <summary>应用内容根；默认使用进程当前工作目录。</summary>
    public string? ContentRoot { get; set; }

    /// <summary>相对 <see cref="ContentRoot"/> 的 manifest 路径。</summary>
    public string RelativePath { get; set; } = "framework-manifest.json";
}

/// <summary>
/// 从 preset-scoped manifest 解析允许执行的迁移脚本文件名集合。
/// </summary>
public static class FrameworkManifestMigrationScope
{
    /// <summary>从 preset-scoped 的 <c>framework-manifest.json</c> 解析允许执行的迁移脚本名集合。</summary>
    /// <param name="options">manifest 的内容根与相对路径配置。</param>
    /// <returns>返回允许脚本名集合；未声明生成应用且清单无限定时返回 <see langword="null"/>，表示保持框架兼容的全量迁移模式。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/> 时抛出。</exception>
    /// <exception cref="InvalidOperationException">已声明生成应用却缺失或无限定清单，或清单 selectionStatus/scripts 结构非法、为空或含重名脚本时抛出。</exception>
    public static HashSet<string>? TryLoadAllowedScriptNames(
        FrameworkManifestMigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var contentRoot = string.IsNullOrWhiteSpace(options.ContentRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(options.ContentRoot);
        var manifestPath = Path.GetFullPath(
            Path.Combine(contentRoot, options.RelativePath ?? "framework-manifest.json"));
        var applicationPath = Path.Combine(contentRoot, "fullnet-app.json");
        // 应用内容根声明了冻结预设，残缺清单不能静默扩大为全量框架迁移。
        var applicationDeclared = File.Exists(applicationPath) || Directory.Exists(applicationPath);
        if (!File.Exists(manifestPath))
        {
            return UnscopedOrRejectApplication(applicationDeclared);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("migrationInventory", out var inventory))
        {
            return UnscopedOrRejectApplication(applicationDeclared);
        }

        if (!inventory.TryGetProperty("selectionStatus", out var statusElement))
        {
            return UnscopedOrRejectApplication(applicationDeclared);
        }

        var status = statusElement.GetString();
        if (string.IsNullOrWhiteSpace(status)
            || string.Equals(status, "unscoped", StringComparison.Ordinal))
        {
            return UnscopedOrRejectApplication(applicationDeclared);
        }

        if (!status.StartsWith("preset-", StringComparison.Ordinal) || status.Length == "preset-".Length)
        {
            throw new InvalidOperationException(
                "framework-manifest migrationInventory.selectionStatus is invalid.");
        }

        if (!inventory.TryGetProperty("scripts", out var scriptsElement)
            || scriptsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "framework-manifest migrationInventory.scripts is missing.");
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var script in scriptsElement.EnumerateArray())
        {
            if (script.ValueKind != JsonValueKind.Object || !script.TryGetProperty("name", out var nameElement))
            {
                throw new InvalidOperationException("framework-manifest preset migration inventory entry is invalid.");
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("framework-manifest preset migration inventory entry is invalid.");
            }
            if (!allowed.Add(name))
            {
                throw new InvalidOperationException("framework-manifest preset migration inventory contains duplicate names.");
            }
        }

        if (allowed.Count == 0)
        {
            throw new InvalidOperationException(
                "framework-manifest preset migration inventory is empty.");
        }

        return allowed;
    }

    /// <summary>校验清单允许的脚本集合同时被 SQL Server 与 MySQL 的成对嵌入资源覆盖。</summary>
    /// <param name="allowed">允许脚本名集合；为 <see langword="null"/> 表示无限定兼容模式，直接跳过校验。</param>
    /// <param name="resourceNames">待校验的迁移嵌入资源名集合。</param>
    /// <exception cref="InvalidOperationException">允许集合中存在未被某一 Provider 的嵌入资源承载的脚本名时抛出。</exception>
    public static void ValidateEmbeddedScriptNames(HashSet<string>? allowed, IEnumerable<string> resourceNames)
    {
        // 无限定清单的框架兼容模式保持不变；显式子集必须由真实双库资源承载。
        if (allowed is null) return;
        var resources = resourceNames.ToArray();
        foreach (var segment in new[] { ".Migrations.SqlServer.", ".Migrations.MySql." })
        {
            var available = resources.Select(name => GetEmbeddedScriptName(name, segment))
                .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
            if (allowed.Any(name => !available.Contains(name)))
            {
                throw new InvalidOperationException("framework-manifest migration inventory does not match paired embedded scripts.");
            }
        }
    }

    /// <summary>从嵌入资源全名中截取指定 Provider 段之后的脚本名。</summary>
    /// <param name="resourceName">迁移嵌入资源全名。</param>
    /// <param name="providerSegment">Provider 资源段，例如 <c>.Migrations.SqlServer.</c>。</param>
    /// <returns>截取到的脚本名；资源名不含该段时返回 <see langword="null"/>。</returns>
    public static string? GetEmbeddedScriptName(string resourceName, string providerSegment)
    {
        var index = resourceName.IndexOf(providerSegment, StringComparison.Ordinal);
        return index < 0 ? null : resourceName[(index + providerSegment.Length)..];
    }

    private static HashSet<string>? UnscopedOrRejectApplication(bool applicationDeclared)
    {
        if (applicationDeclared)
        {
            throw new InvalidOperationException("framework-manifest application migration inventory is missing or unscoped.");
        }
        return null;
    }
}
