using System.Reflection;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 防止 owner-domain 类型再次进入 Identity.Contracts hub；规则见 R-20260816。
/// </summary>
[TestClass]
public sealed class IdentityContractsHubBoundaryTests
{
    private static readonly string[] ForbiddenTypeNameSuffixes =
    [
        "Repository",
        "WritePort",
        "CommandHandler",
        "DbContext",
        "SqlStatement",
    ];

    private static readonly string[] ForbiddenOwnerDomainPrefixes =
    [
        "Document",
        "Files",
        "Organization",
        "Tenancy",
        "Settings",
        "Jobs",
        "Messaging",
        "Notifications",
        "Auditing",
        "SerialNumbers",
        "CodeGeneration",
        "Observability",
    ];

    private static readonly string[] AllowedOwnerDomainPrefixExceptions =
    [
        "IdentityOrganization",
        "IIdentityOrganization",
        "ReconcileOrganization",
        "IOrganizationUnitProjection",
        "OrganizationUnitProjectionEntry",
    ];

    [TestMethod]
    public void Identity_contracts_hub_rejects_owner_domain_write_and_persistence_types()
    {
        var violations = typeof(VerifiedTenantContext).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace == typeof(VerifiedTenantContext).Namespace)
            .SelectMany(InspectType)
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            violations,
            "Identity.Contracts hub 边界违规：\n" + string.Join('\n', violations));
    }

    private static IEnumerable<string> InspectType(Type type)
    {
        var name = type.Name;

        foreach (var suffix in ForbiddenTypeNameSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                yield return $"{type.FullName} 禁止使用 owner/persistence 后缀“{suffix}”。";
            }
        }

        if (IsForbiddenOwnerDomainName(name))
        {
            yield return $"{type.FullName} 禁止以其他模块 owner 前缀命名；"
                         + "consumer-owned Port/IntegrationEvent 应使用 Identity* 或 IIdentity* 前缀。";
        }
    }

    private static bool IsForbiddenOwnerDomainName(string typeName)
    {
        foreach (var exception in AllowedOwnerDomainPrefixExceptions)
        {
            if (typeName.StartsWith(exception, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var prefix in ForbiddenOwnerDomainPrefixes)
        {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
