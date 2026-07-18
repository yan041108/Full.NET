using System.Text.RegularExpressions;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Seeding.Dapper;

/// <summary>
/// 验证 Contributor 契约并产生不依赖 DI 枚举顺序的确定性拓扑序列。
/// </summary>
public static partial class SeedContributorGraph
{
    /// <summary>
    /// 验证全部 Contributor，再筛选目标 Profile 的有效层并进行稳定拓扑排序。
    /// </summary>
    /// <param name="contributors">进程内注册的全部 Contributor。</param>
    /// <param name="profile">本次请求的封闭 Profile。</param>
    /// <returns>依赖优先、同层按稳定名称排序的执行序列。</returns>
    /// <exception cref="SeedConfigurationException">Contributor 契约或依赖图无效。</exception>
    public static IReadOnlyList<IDataSeedContributor> Order(
        IEnumerable<IDataSeedContributor> contributors,
        SeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        var all = contributors.ToArray();
        ValidateContracts(all);

        var effectiveLayers = profile.EffectiveLayers();
        var selected = all
            .Where(item => item.Profiles.Overlaps(effectiveLayers))
            .ToDictionary(item => item.Name, StringComparer.Ordinal);
        var indegrees = selected.Keys.ToDictionary(
            name => name,
            _ => 0,
            StringComparer.Ordinal);
        var dependants = selected.Keys.ToDictionary(
            name => name,
            _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var contributor in selected.Values)
        {
            foreach (var dependency in contributor.Dependencies.Distinct(StringComparer.Ordinal))
            {
                if (!selected.ContainsKey(dependency))
                {
                    throw new SeedConfigurationException(SeedErrorCodes.DependencyMissing);
                }

                indegrees[contributor.Name]++;
                dependants[dependency].Add(contributor.Name);
            }
        }

        var ready = new SortedSet<string>(
            indegrees.Where(item => item.Value == 0).Select(item => item.Key),
            StringComparer.Ordinal);
        var ordered = new List<IDataSeedContributor>(selected.Count);
        while (ready.Count > 0)
        {
            var name = ready.Min!;
            ready.Remove(name);
            ordered.Add(selected[name]);

            foreach (var dependant in dependants[name])
            {
                indegrees[dependant]--;
                if (indegrees[dependant] == 0)
                {
                    ready.Add(dependant);
                }
            }
        }

        if (ordered.Count != selected.Count)
        {
            throw new SeedConfigurationException(SeedErrorCodes.DependencyCycle);
        }

        return ordered;
    }

    private static void ValidateContracts(IReadOnlyCollection<IDataSeedContributor> contributors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contributor in contributors)
        {
            if (!ContributorNamePattern().IsMatch(contributor.Name))
            {
                throw new SeedConfigurationException(SeedErrorCodes.ContributorNameInvalid);
            }

            if (contributor.Version < 1)
            {
                throw new SeedConfigurationException(SeedErrorCodes.ContributorVersionInvalid);
            }

            if (contributor.Profiles.Count == 0
                || contributor.Profiles.Any(profile => !Enum.IsDefined(profile)))
            {
                throw new SeedConfigurationException(SeedErrorCodes.ContributorProfileInvalid);
            }

            if (!names.Add(contributor.Name))
            {
                throw new SeedConfigurationException(SeedErrorCodes.ContributorDuplicate);
            }

            if (contributor.Dependencies.Any(
                dependency => !ContributorNamePattern().IsMatch(dependency)))
            {
                throw new SeedConfigurationException(SeedErrorCodes.ContributorNameInvalid);
            }
        }
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ContributorNamePattern();
}
