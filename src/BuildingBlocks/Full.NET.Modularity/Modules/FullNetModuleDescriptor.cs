namespace Full.NET.Modularity.Modules;

/// <summary>官方模块来源分类；只描述静态清单，不表示可动态加载。</summary>
public enum FullNetModuleSourceClassification
{
    /// <summary>Full.NET 官方 Composition 目录中的模块。</summary>
    Official = 0,

    /// <summary>兼容适配边界中的模块或能力。</summary>
    Compatibility = 1,

    /// <summary>示例或演示模块。</summary>
    Sample = 2,
}

/// <summary>模块健康检查能力声明；不包含可执行探针脚本。</summary>
public enum FullNetModuleHealthCapability
{
    /// <summary>未声明独立健康检查。</summary>
    None = 0,

    /// <summary>可贡献宿主就绪检查。</summary>
    Readiness = 1,
}

/// <summary>
/// 不可变模块描述符快照。显式禁止包含源码、程序集字节、编译入口或任意加载路径。
/// </summary>
public sealed class FullNetModuleDescriptor
{
    /// <summary>已知宿主 Profile 稳定名，与 <c>FullNetHostProfile</c> 枚举名对齐。</summary>
    public static IReadOnlySet<string> KnownHostProfiles { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Api",
            "Worker",
            "Migrator",
        };

    private FullNetModuleDescriptor(
        string moduleKey,
        string displayName,
        string version,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> hostProfiles,
        FullNetModuleSourceClassification sourceClassification,
        FullNetModuleHealthCapability healthCapability)
    {
        ModuleKey = moduleKey;
        DisplayName = displayName;
        Version = version;
        Dependencies = dependencies;
        HostProfiles = hostProfiles;
        SourceClassification = sourceClassification;
        HealthCapability = healthCapability;
    }

    /// <summary>稳定模块键，与 <see cref="IFullNetModule.Name"/> 一致。</summary>
    public string ModuleKey { get; }

    /// <summary>面向管理端的显示名；不得承载路径或脚本。</summary>
    public string DisplayName { get; }

    /// <summary>模块版本字符串，通常取自程序集版本。</summary>
    public string Version { get; }

    /// <summary>依赖的稳定模块键列表。</summary>
    public IReadOnlyList<string> Dependencies { get; }

    /// <summary>适用的宿主 Profile 名称列表。</summary>
    public IReadOnlyList<string> HostProfiles { get; }

    /// <summary>来源分类。</summary>
    public FullNetModuleSourceClassification SourceClassification { get; }

    /// <summary>健康检查能力。</summary>
    public FullNetModuleHealthCapability HealthCapability { get; }

    /// <summary>
    /// 创建并校验描述符；拒绝空白键、未知 Profile、绝对路径以及敏感载荷痕迹。
    /// </summary>
    public static FullNetModuleDescriptor Create(
        string moduleKey,
        string displayName,
        string version,
        IEnumerable<string> dependencies,
        IEnumerable<string> hostProfiles,
        FullNetModuleSourceClassification sourceClassification,
        FullNetModuleHealthCapability healthCapability)
    {
        var normalizedKey = RequireSafeToken(moduleKey, nameof(moduleKey));
        var normalizedDisplayName = RequireSafeDisplayText(displayName, nameof(displayName));
        var normalizedVersion = RequireSafeToken(version, nameof(version));

        var dependencyList = NormalizeDependencyKeys(normalizedKey, dependencies);
        var profileList = NormalizeHostProfiles(normalizedKey, hostProfiles);

        return new FullNetModuleDescriptor(
            normalizedKey,
            normalizedDisplayName,
            normalizedVersion,
            dependencyList,
            profileList,
            sourceClassification,
            healthCapability);
    }

    private static IReadOnlyList<string> NormalizeDependencyKeys(
        string moduleKey,
        IEnumerable<string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        var items = dependencies as IReadOnlyList<string> ?? dependencies.ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var dependency = items[index];
            if (string.IsNullOrWhiteSpace(dependency))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares a null or blank dependency key "
                    + $"at index {index}.");
            }

            var token = RequireSafeToken(dependency, "dependency");
            if (!seen.Add(token))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares duplicate dependency key '{token}'.");
            }

            normalized.Add(token);
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeHostProfiles(
        string moduleKey,
        IEnumerable<string> hostProfiles)
    {
        ArgumentNullException.ThrowIfNull(hostProfiles);
        var items = hostProfiles as IReadOnlyList<string> ?? hostProfiles.ToArray();
        if (items.Count == 0)
        {
            throw new InvalidOperationException(
                $"Module key '{moduleKey}' must declare at least one host profile.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(items.Count);
        foreach (var profile in items)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares a blank host profile.");
            }

            var token = RequireSafeToken(profile.Trim(), "hostProfile");
            if (!KnownHostProfiles.Contains(token))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares unknown host profile '{token}'.");
            }

            if (!seen.Add(token))
            {
                throw new InvalidOperationException(
                    $"Module key '{moduleKey}' declares duplicate host profile '{token}'.");
            }

            normalized.Add(token);
        }

        return normalized;
    }

    private static string RequireSafeToken(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Module descriptor field '{paramName}' must not be blank.");
        }

        var trimmed = value.Trim();
        RejectAbsolutePathOrSensitivePayload(trimmed, paramName);
        return trimmed;
    }

    private static string RequireSafeDisplayText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Module descriptor field '{paramName}' must not be blank.");
        }

        var trimmed = value.Trim();
        RejectAbsolutePathOrSensitivePayload(trimmed, paramName);
        return trimmed;
    }

    private static void RejectAbsolutePathOrSensitivePayload(string value, string paramName)
    {
        if (Path.IsPathRooted(value)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains('\0'))
        {
            throw new InvalidOperationException(
                $"Module descriptor field '{paramName}' must not contain absolute paths "
                + "or path separators.");
        }
    }
}
