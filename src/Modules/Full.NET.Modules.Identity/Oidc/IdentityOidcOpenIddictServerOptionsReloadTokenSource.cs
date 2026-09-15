using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>触发 OpenIddict Server 选项重建，使运行时 OIDC 签名密钥激活生效。</summary>
internal sealed class IdentityOidcOpenIddictServerOptionsReloadTokenSource
    : IOptionsChangeTokenSource<OpenIddictServerOptions>, IDisposable
{
    private CancellationTokenSource _source = new();

    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_source.Token);

    public void Reload() => Interlocked.Exchange(ref _source, new CancellationTokenSource()).Cancel();

    public void Dispose() => _source.Dispose();
}