using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Identity.Catalogs;

/// <summary>Identity 模块内置枚举/常量目录（账号类型）。</summary>
internal sealed class IdentityEnumCatalogContributor : IEnumCatalogContributor
{
    public IReadOnlyCollection<EnumCatalogDefinition> Catalogs { get; } =
    [
        new EnumCatalogDefinition(
            "identity.account_type",
            "账号类型",
            "Host 用户账号类型，与 Admin.NET AccountTypeEnum 语义对齐。",
            IdentityAccountTypes.All
                .Select((code, index) => new EnumCatalogMemberDefinition(
                    code,
                    ToLabel(code),
                    (index + 1) * 10))
                .ToArray()),
    ];

    private static string ToLabel(string code) =>
        code switch
        {
            IdentityAccountTypes.SuperAdmin => "超级管理员",
            IdentityAccountTypes.SysAdmin => "系统管理员",
            IdentityAccountTypes.NormalUser => "普通用户",
            _ => code,
        };
}