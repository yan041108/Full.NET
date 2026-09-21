using Full.NET.Modules.Identity;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

/// <summary>
/// Host 侧栏一级「领域目录」与模块 key 的声明式映射；同步与 Baseline 种子共用。
/// </summary>
internal static class HostNavigationDomainLayout
{
    internal const string OtherDomainKey = "other";

    internal const string DomainDirectoryPermission =
        IdentityAuthorizationContributor.DashboardRead;

    internal sealed record DomainDefinition(
        string Key,
        string Title,
        int Order,
        IReadOnlyList<string> ModuleKeys);

    private static readonly DomainDefinition[] Domains =
    [
        new(
            "overview-platform",
            "概览与平台",
            10,
            ["identity", "platform", "organization", "regions"]),
        new(
            "tenancy-commerce",
            "租户与商业",
            20,
            ["tenancy", "payments", "data-approval", "enterprise-request"]),
        new(
            "collaboration",
            "协同与内容",
            30,
            ["workflow", "notifications", "calendar", "document", "files"]),
        new(
            "data-intelligence",
            "数据与智能",
            40,
            [
                "reporting",
                "import-export",
                "ai",
                "goview",
                "ocr",
                "printing",
                "serial-numbers"
            ]),
        new(
            "integration-dev",
            "集成与开发",
            50,
            ["code-generation", "webhooks", "mqtt", "k3cloud", "messaging"]),
        new(
            "operations-security",
            "运维与安全",
            60,
            ["settings", "jobs", "observability", "auditing", "cryptography"]),
        new(OtherDomainKey, "其他能力", 90, [])
    ];

    internal static IReadOnlyList<DomainDefinition> AllDomains => Domains;

    /// <summary>
    /// 解析模块所属领域；未显式映射的模块归入 <see cref="OtherDomainKey"/>。
    /// </summary>
    internal static string ResolveDomainKeyForModule(string moduleKey)
    {
        foreach (var domain in Domains)
        {
            if (domain.ModuleKeys.Contains(moduleKey, StringComparer.Ordinal))
            {
                return domain.Key;
            }
        }

        return OtherDomainKey;
    }

    internal static DomainDefinition? FindDomain(string domainKey)
    {
        foreach (var domain in Domains)
        {
            if (string.Equals(domain.Key, domainKey, StringComparison.Ordinal))
            {
                return domain;
            }
        }

        return null;
    }
}
