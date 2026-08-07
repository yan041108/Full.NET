using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Caching.Fusion;

/// <summary>基于 <see cref="CacheOptions"/> 构建的缓存策略注册表。</summary>
public sealed class CachePolicyRegistry : ICachePolicyRegistry
{
    private readonly IReadOnlyDictionary<string, CacheEntryPolicy> _policies;

    private CachePolicyRegistry(IReadOnlyDictionary<string, CacheEntryPolicy> policies)
    {
        _policies = policies;
    }

    /// <summary>从已验证的 <see cref="CacheOptions"/> 创建注册表，并合并内置默认条目。</summary>
    public static CachePolicyRegistry Create(CacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var policies = new Dictionary<string, CacheEntryPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            [CacheEntryNames.TenantResolution] = CreateDefaultTenantResolution(options),
            [CacheEntryNames.DiagnosticPolicy] = CreateDefaultDiagnosticPolicy(options),
            [CacheEntryNames.GridPreference] = CreateDefaultGridPreference(options),
        };

        foreach (var (entryName, definition) in options.Entries)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new OptionsValidationException(
                    CacheOptions.SectionName,
                    typeof(CacheOptions),
                    ["Cache:Entries contains an empty entry name."]);
            }

            policies[entryName.Trim()] = BuildPolicy(entryName.Trim(), definition, options);
        }

        return new CachePolicyRegistry(policies);
    }

    /// <inheritdoc />
    public CacheEntryPolicy GetRequired(string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        if (_policies.TryGetValue(entryName, out var policy))
        {
            return policy;
        }

        throw new InvalidOperationException(
            $"Unknown cache entry '{entryName}'. Register it under Cache:Entries before use.");
    }

    /// <inheritdoc />
    public CacheAccessDecision ResolveAccess(string entryName)
    {
        var policy = GetRequired(entryName);
        var kind = policy.ConsistencyClass switch
        {
            CacheConsistencyClass.AuthorityCritical => CacheAccessKind.AuthorityRead,
            CacheConsistencyClass.NotCached => CacheAccessKind.Bypass,
            _ => CacheAccessKind.UseCache,
        };
        return new CacheAccessDecision(kind, policy.ConsistencyClass, policy.EntryName);
    }

    /// <inheritdoc />
    public FusionCacheEntryOptions CreateEntryOptions(string entryName)
    {
        var policy = GetRequired(entryName);
        var access = ResolveAccess(entryName);
        if (access.Kind is CacheAccessKind.AuthorityRead or CacheAccessKind.Bypass)
        {
            throw new InvalidOperationException(
                $"Cache entry '{entryName}' is {policy.ConsistencyClass} and resolves to {access.Kind}; "
                + "CreateEntryOptions is forbidden for this class.");
        }

        var options = new FusionCacheEntryOptions
        {
            Duration = policy.L2Duration,
            MemoryCacheDuration = policy.L1Duration,
            JitterMaxDuration = policy.Jitter,
            IsFailSafeEnabled = policy.FailSafeEnabled,
        };

        if (policy.ConsistencyClass == CacheConsistencyClass.SharedL2Only)
        {
            // S0-L2：关闭节点 L1，只保留共享 L2，消除多实例 L1 漂移。
            options.SetSkipMemoryCache(true);
            options.MemoryCacheDuration = null;
        }

        return options;
    }

    /// <inheritdoc />
    public HybridCacheEntryOptions CreateHybridEntryOptions(
        string entryName,
        CacheEntryLifetime lifetime = CacheEntryLifetime.Normal)
    {
        var policy = GetRequired(entryName);
        var access = ResolveAccess(entryName);
        if (access.Kind is CacheAccessKind.AuthorityRead or CacheAccessKind.Bypass)
        {
            throw new InvalidOperationException(
                $"Cache entry '{entryName}' is {policy.ConsistencyClass} and resolves to {access.Kind}; "
                + "CreateHybridEntryOptions is forbidden for this class.");
        }

        TimeSpan l1Duration;
        TimeSpan l2Duration;
        if (lifetime == CacheEntryLifetime.Negative)
        {
            if (policy.NegativeDuration is not { } negativeDuration || negativeDuration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"Cache entry '{entryName}' does not declare a positive NegativeDuration.");
            }

            l1Duration = negativeDuration;
            l2Duration = negativeDuration;
        }
        else
        {
            l2Duration = policy.L2Duration;
            l1Duration = policy.ConsistencyClass == CacheConsistencyClass.SharedL2Only
                ? TimeSpan.Zero
                : policy.L1Duration;
        }

        return new HybridCacheEntryOptions
        {
            Expiration = l2Duration,
            LocalCacheExpiration = l1Duration,
        };
    }


    private static CacheEntryPolicy CreateDefaultDiagnosticPolicy(CacheOptions options) =>
        new(
            CacheEntryNames.DiagnosticPolicy,
            OwnerModule: "settings",
            CacheConsistencyClass.ImportantBusiness,
            L1Duration: TimeSpan.FromSeconds(30),
            L2Duration: TimeSpan.FromMinutes(2),
            Jitter: options.Jitter,
            NegativeDuration: TimeSpan.FromSeconds(15),
            FailSafeEnabled: false,
            RequiresVersionRecheck: false,
            MaxSerializedBytes: 65_536);

    private static CacheEntryPolicy CreateDefaultTenantResolution(CacheOptions options) =>
        new(
            CacheEntryNames.TenantResolution,
            OwnerModule: "tenancy",
            CacheConsistencyClass.ImportantBusiness,
            L1Duration: TimeSpan.FromMinutes(5),
            L2Duration: TimeSpan.FromMinutes(5),
            Jitter: options.Jitter,
            NegativeDuration: TimeSpan.FromMinutes(1),
            FailSafeEnabled: false,
            RequiresVersionRecheck: false,
            MaxSerializedBytes: 65_536);

    private static CacheEntryPolicy CreateDefaultGridPreference(CacheOptions options) =>
        new(
            CacheEntryNames.GridPreference,
            OwnerModule: "settings",
            CacheConsistencyClass.DegradableDisplay,
            L1Duration: TimeSpan.FromMinutes(15),
            L2Duration: TimeSpan.FromDays(7),
            Jitter: options.Jitter,
            NegativeDuration: null,
            FailSafeEnabled: false,
            RequiresVersionRecheck: false,
            MaxSerializedBytes: 65_536);

    private static CacheEntryPolicy BuildPolicy(
        string entryName,
        CacheEntryDefinitionOptions definition,
        CacheOptions fallback)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.OwnerModule))
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:OwnerModule is required."]);
        }

        var consistency = ParseConsistencyClass(entryName, definition.ConsistencyClass);
        var jitter = definition.Jitter < TimeSpan.Zero ? fallback.Jitter : definition.Jitter;
        if (jitter == TimeSpan.Zero && fallback.Jitter > TimeSpan.Zero
            && consistency is CacheConsistencyClass.ImportantBusiness
                or CacheConsistencyClass.DegradableDisplay
                or CacheConsistencyClass.SharedL2Only)
        {
            jitter = fallback.Jitter;
        }

        ValidateDurations(entryName, consistency, definition);
        ValidateFailSafe(entryName, consistency, definition.FailSafeEnabled);

        if (definition.MaxSerializedBytes <= 0)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:MaxSerializedBytes must be greater than zero."]);
        }

        return new CacheEntryPolicy(
            entryName,
            definition.OwnerModule.Trim(),
            consistency,
            definition.L1Duration,
            definition.L2Duration,
            jitter,
            definition.NegativeDuration,
            definition.FailSafeEnabled,
            definition.RequiresVersionRecheck,
            definition.MaxSerializedBytes);
    }

    private static void ValidateDurations(
        string entryName,
        CacheConsistencyClass consistency,
        CacheEntryDefinitionOptions definition)
    {
        if (consistency is CacheConsistencyClass.AuthorityCritical
            or CacheConsistencyClass.NotCached)
        {
            return;
        }

        if (definition.L2Duration <= TimeSpan.Zero)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:L2Duration must be greater than zero."]);
        }

        if (consistency != CacheConsistencyClass.SharedL2Only
            && definition.L1Duration <= TimeSpan.Zero)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:L1Duration must be greater than zero."]);
        }

        if (definition.Jitter < TimeSpan.Zero)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:Jitter cannot be negative."]);
        }
    }

    private static void ValidateFailSafe(
        string entryName,
        CacheConsistencyClass consistency,
        bool failSafeEnabled)
    {
        if (!failSafeEnabled)
        {
            return;
        }

        if (consistency != CacheConsistencyClass.DegradableDisplay)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:FailSafeEnabled is only allowed for S2 (DegradableDisplay)."]);
        }
    }

    private static CacheConsistencyClass ParseConsistencyClass(
        string entryName,
        string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:ConsistencyClass is required."]);
        }

        var normalized = raw.Trim();
        if (Enum.TryParse<CacheConsistencyClass>(normalized, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return normalized.ToUpperInvariant() switch
        {
            "C0" => CacheConsistencyClass.AuthorityCritical,
            "S0-L2" or "S0L2" or "S0_L2" => CacheConsistencyClass.SharedL2Only,
            "S1" => CacheConsistencyClass.ImportantBusiness,
            "S2" => CacheConsistencyClass.DegradableDisplay,
            "N0" => CacheConsistencyClass.NotCached,
            _ => throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                [$"Cache:Entries:{entryName}:ConsistencyClass '{raw}' is unknown."]),
        };
    }
}
