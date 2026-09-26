using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 生成应用 <c>framework-manifest.json</c> 的迁移闭包配置。
/// </summary>
public sealed class FrameworkManifestMigrationOptions
{
    public const string SectionName = "FullNet:FrameworkManifest";

    /// <summary>应用内容根；默认使用进程当前工作目录。</summary>
    public string? ContentRoot { get; set; }

    /// <summary>相对 <see cref="ContentRoot"/> 的 manifest 路径。</summary>
    public string RelativePath { get; set; } = "framework-manifest.json";
}

/// <summary>
/// 从 preset-scoped manifest 解析允许执行的迁移脚本文件名集合。
/// </summary>
internal static class FrameworkManifestMigrationScope
{
    public static HashSet<string>? TryLoadAllowedScriptNames(
        FrameworkManifestMigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var contentRoot = string.IsNullOrWhiteSpace(options.ContentRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(options.ContentRoot);
        var manifestPath = Path.GetFullPath(
            Path.Combine(contentRoot, options.RelativePath ?? "framework-manifest.json"));
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("migrationInventory", out var inventory))
        {
            return null;
        }

        if (!inventory.TryGetProperty("selectionStatus", out var statusElement))
        {
            return null;
        }

        var status = statusElement.GetString();
        if (string.IsNullOrWhiteSpace(status)
            || string.Equals(status, "unscoped", StringComparison.Ordinal))
        {
            return null;
        }

        if (!status.StartsWith("preset-", StringComparison.Ordinal))
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
            if (!script.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                allowed.Add(name);
            }
        }

        if (allowed.Count == 0)
        {
            throw new InvalidOperationException(
                "framework-manifest preset migration inventory is empty.");
        }

        return allowed;
    }
}
