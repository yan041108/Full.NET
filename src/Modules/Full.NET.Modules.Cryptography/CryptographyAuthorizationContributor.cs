using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Cryptography.Contracts;

namespace Full.NET.Modules.Cryptography;

/// <summary>国密模块授权目录贡献者。</summary>
internal sealed class CryptographyAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("cryptography", "国密", 81);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            CryptographyPermissions.KeysRead,
            "读取国密密钥目录与部署状态",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            CryptographyPermissions.Sm2Sign,
            "执行受控 SM2 签名",
            AuthorizationScope.Host),
        new PermissionDefinition(
            CryptographyPermissions.Sm2Verify,
            "执行 SM2 验签",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "cryptography-gm-keys",
            null,
            "cryptography-gm-keys",
            "/cryptography/gm-keys",
            "cryptography-gm-keys",
            "国密密钥",
            "GM Keys",
            "lock",
            74,
            CryptographyPermissions.KeysRead),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "cryptography.sm2.sign",
            "cryptography-gm-keys",
            CryptographyPermissions.Sm2Sign,
            "SM2 签名",
            "sign",
            10),
        new AuthorizationActionDefinition(
            "cryptography.sm2.verify",
            "cryptography-gm-keys",
            CryptographyPermissions.Sm2Verify,
            "SM2 验签",
            "verify",
            20),
    ];
}
