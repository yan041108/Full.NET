namespace Full.NET.Modules.Identity.Oidc;

/// <summary>在运行时激活 OIDC 签名密钥后，触发 OpenIddict Server 选项重建以刷新 SigningCredentials。</summary>
internal sealed class IdentityOidcOpenIddictSigningCredentialSynchronizer(
    IdentityOidcOpenIddictServerOptionsReloadTokenSource reloadTokenSource)
{
    public void Sync() => reloadTokenSource.Reload();
}