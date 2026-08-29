namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class CachePolicyBoundaryTests
{
    private static readonly AllowedCacheOptionsSite[] AllowedSites = [];

    [TestMethod]
    public void Business_modules_must_not_hand_build_cache_entry_options_outside_allowlist()
    {
        var root = FindRepositoryRoot();
        var modulesRoot = Path.Combine(root, "src", "Modules");
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(
                     modulesRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var source = File.ReadAllText(path);
            foreach (var kind in DetectHandBuiltCacheOptions(source))
            {
                var allowed = AllowedSites.Any(site =>
                    string.Equals(site.RelativePath, relative, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(site.TokenKind, kind, StringComparison.Ordinal));
                if (!allowed)
                {
                    offenders.Add($"{relative}: {kind}");
                }
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail(
                "业务模块不得直接 new FusionCacheEntryOptions/HybridCacheEntryOptions；"
                + "请改走 ICachePolicyRegistry，或把精确路径登记到白名单并写明移除任务。违规: "
                + string.Join("; ", offenders));
        }
    }

    [TestMethod]
    public void Allowlist_must_remain_empty_after_policy_registry_adoption()
    {
        Assert.HasCount(0, AllowedSites);
    }

    [TestMethod]
    public void Allowlist_entries_must_exist_with_reason_and_removal_task()
    {
        if (AllowedSites.Length == 0)
        {
            return;
        }

        var root = FindRepositoryRoot();

        foreach (var site in AllowedSites)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(site.Reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(site.RemovalTask));
            Assert.IsFalse(
                site.RelativePath.Contains('*', StringComparison.Ordinal)
                || site.RelativePath.EndsWith('/'),
                "白名单禁止通配或目录豁免。");

            var fullPath = Path.Combine(root, site.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(fullPath), $"白名单路径不存在: {site.RelativePath}");

            var source = File.ReadAllText(fullPath);
            Assert.IsTrue(
                DetectHandBuiltCacheOptions(source).Contains(site.TokenKind),
                $"白名单已过期，文件不再手写 {site.TokenKind}: {site.RelativePath}");
        }
    }

    [TestMethod]
    public void Caching_building_block_may_create_fusion_entry_options()
    {
        var root = FindRepositoryRoot();
        var registryPath = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Caching.Fusion",
            "CachePolicyRegistry.cs");
        var source = File.ReadAllText(registryPath);
        StringAssert.Contains(source, "new FusionCacheEntryOptions");
    }

    [TestMethod]
    public void Module_cache_invalidators_must_not_depend_on_fusion_sdk_types()
    {
        var root = FindRepositoryRoot();
        var modulesRoot = Path.Combine(root, "src", "Modules");
        var offenders = Directory
            .EnumerateFiles(
                modulesRoot,
                "*CacheInvalidator.cs",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("IFusionCache", StringComparison.Ordinal)
                    || source.Contains("FusionCacheEntryOptions", StringComparison.Ordinal)
                    || source.Contains("using ZiggyCreatures.Caching.Fusion", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "模块缓存失效器必须通过 ICacheInvalidator 声明传播语义，不得依赖 FusionCache SDK。违规: "
            + string.Join("; ", offenders));
    }

    private static IReadOnlyList<string> DetectHandBuiltCacheOptions(string source)
    {
        var kinds = new List<string>();
        if (source.Contains("new FusionCacheEntryOptions", StringComparison.Ordinal)
            || ContainsTargetTypedConstruction(source, "FusionCacheEntryOptions"))
        {
            kinds.Add("FusionCacheEntryOptions");
        }

        if (source.Contains("new HybridCacheEntryOptions", StringComparison.Ordinal)
            || ContainsTargetTypedConstruction(source, "HybridCacheEntryOptions"))
        {
            kinds.Add("HybridCacheEntryOptions");
        }

        return kinds;
    }

    private static bool ContainsTargetTypedConstruction(string source, string typeName)
    {
        // 覆盖 `HybridCacheEntryOptions options = new()` 这类目标类型 new，避免漏扫。
        var index = 0;
        while ((index = source.IndexOf(typeName, index, StringComparison.Ordinal)) >= 0)
        {
            var afterType = index + typeName.Length;
            var slice = source.AsSpan(afterType);
            var equalsIndex = slice.IndexOf('=');
            if (equalsIndex >= 0 && equalsIndex < 128)
            {
                var between = slice[..equalsIndex].Trim();
                var rhs = slice[(equalsIndex + 1)..].TrimStart();
                var looksLikeIdentifier =
                    between.Length == 0
                    || (between.IndexOfAny([' ', '\t', '\r', '\n', '(', ')', ',', ';']) < 0
                        && !between.Contains('<')
                        && !between.Contains('>'));
                if (looksLikeIdentifier
                    && (rhs.StartsWith("new(", StringComparison.Ordinal)
                        || rhs.StartsWith("new()", StringComparison.Ordinal)
                        || rhs.StartsWith("new {", StringComparison.Ordinal)
                        || rhs.StartsWith("new{", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            index = afterType;
        }

        return false;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private sealed record AllowedCacheOptionsSite(
        string RelativePath,
        string TokenKind,
        string Reason,
        string RemovalTask);
}
