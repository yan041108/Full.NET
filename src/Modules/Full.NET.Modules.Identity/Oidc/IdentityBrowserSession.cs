using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Client;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class NoIdentityBrowserSession : IIdentityBrowserSession
{
    public bool IsActive => false;
    public Task ReplaceCredentialsAsync(TenantContextTokenResponse credentials, CancellationToken cancellationToken)
        => throw new InvalidOperationException("No browser session is active.");
}

internal sealed class IdentityBrowserSessionUnavailableException(bool busy) : Exception
{
    public bool IsBusy { get; } = busy;
}

/// <summary>随机浏览器票据与共享刷新租约；不确定刷新结果只允许重新登录，绝不重放旧 refresh。</summary>
internal sealed class IdentityBrowserSession(IQueryExecutor queries, ICommandExecutor commands,
    ICurrentTenantContextWriter tenant, IClock clock, IDataProtectionProvider protection,
    OpenIddictClientService protocol, IOptions<IdentityBrowserOptions> options,
    IdentityOidcSessionService sessions, ICommandTransaction transaction, OidcAuthenticationEventWriter audit) : IIdentityBrowserSession
{
    public const string CookieName = "__Host-fullnet-browser";
    public const string RegistrationId = "fullnet-admin-browser";
    private readonly IDataProtector _protector = protection.CreateProtector("Full.NET.Identity.BrowserTicket.v1");
    private IdentityBrowserSessionRow? _row;
    private Guid? _leaseId;
    public IdentityBrowserTicket? Ticket { get; private set; }
    public bool IsActive => Ticket is not null;
    public DateTimeOffset ExpiresAtUtc => _row!.ExpiresAtUtc;

    public async Task CreateAsync(HttpContext context, string access, string refresh, string identity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh) || string.IsNullOrEmpty(identity))
            throw new InvalidOperationException("The browser authorization did not return all required tokens.");
        await DeleteCookieSessionAsync(context, cancellationToken, clearCookie: false).ConfigureAwait(false);
        var handle = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var ticket = new IdentityBrowserTicket(access, refresh, identity,
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), AccessExpiration(access));
        var row = new IdentityBrowserSessionRow(Guid.CreateVersion7(), Hash(handle), Protect(ticket),
            clock.UtcNow.AddHours(options.Value.SessionHours), 1, null, null);
        await InHostAsync(async () => await commands.ExecuteAsync(IdentityBrowserSessionSql.Insert,
            IdentitySqlParameters.Create(("Id", row.Id), ("HandleHash", row.HandleHash),
                ("Ciphertext", row.Ciphertext), ("ExpiresAtUtc", row.ExpiresAtUtc)), cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        _row = row; Ticket = ticket;
        context.Response.Cookies.Append(CookieName, handle, Cookie(row.ExpiresAtUtc));
    }

    public async Task LoadAsync(string handle, CancellationToken cancellationToken, bool allowRefresh = true)
    {
        if (handle.Length != 43) throw new IdentityBrowserSessionUnavailableException(false);
        var hash = Hash(handle);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            _row = await FindAsync(hash, cancellationToken).ConfigureAwait(false);
            if (_row is null) throw new IdentityBrowserSessionUnavailableException(false);
            if (_row.RefreshLeaseId.HasValue)
            {
                if (_row.RefreshLeaseUntilUtc <= clock.UtcNow)
                {
                    // 租约拥有者可能已换得新 refresh 却在落库前崩溃；不能重用旧令牌。
                    await DeleteAsync(cancellationToken).ConfigureAwait(false);
                    throw new IdentityBrowserSessionUnavailableException(false);
                }
                await Task.Delay(Math.Min(500, 50 + attempt * 50), cancellationToken).ConfigureAwait(false);
                continue;
            }
            try { Ticket = Unprotect(_row.Ciphertext); }
            catch (Exception error) when (error is CryptographicException or JsonException)
            {
                await DeleteAsync(cancellationToken).ConfigureAwait(false);
                throw new IdentityBrowserSessionUnavailableException(false);
            }
            if (!allowRefresh || Ticket.AccessExpiresAtUtc > clock.UtcNow.AddSeconds(30)) return;
            if (!await AcquireAsync(cancellationToken).ConfigureAwait(false)) continue;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                var refreshed = await protocol.AuthenticateWithRefreshTokenAsync(new()
                {
                    RegistrationId = RegistrationId, RefreshToken = Ticket.RefreshToken,
                    CancellationToken = timeout.Token, DisableUserInfo = true
                }).ConfigureAwait(false);
                if (string.IsNullOrEmpty(refreshed.RefreshToken))
                    throw new IdentityBrowserSessionUnavailableException(false);
                await SaveAsync(Ticket with { AccessToken = refreshed.AccessToken,
                    RefreshToken = refreshed.RefreshToken,
                    IdentityToken = refreshed.IdentityToken ?? Ticket.IdentityToken,
                    AccessExpiresAtUtc = refreshed.AccessTokenExpirationDate ?? AccessExpiration(refreshed.AccessToken) },
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch
            {
                // 包括取消及网络不确定结果；遗留租约也会在下次读取时失败关闭。
                await InvalidateAfterCredentialFailureAsync().ConfigureAwait(false);
                throw;
            }
        }
        throw new IdentityBrowserSessionUnavailableException(true);
    }

    public Task<bool> AcquireMutationAsync(CancellationToken cancellationToken) => AcquireAsync(cancellationToken);

    public async Task LogoutAsync(HttpContext context, Guid applicationSessionId, Guid userId, CancellationToken token)
    {
        await InHostAsync(() => transaction.ExecuteAsync(async ct =>
        {
            await sessions.RevokeApplicationSessionAsync(applicationSessionId, ct).ConfigureAwait(false);
            await audit.WriteAsync(userId, null, OidcAuthenticationEventWriter.ApplicationLogout,
                "identity.oidc_application_logged_out", true, ct, applicationSessionId: applicationSessionId).ConfigureAwait(false);
            await DeleteAsync(ct).ConfigureAwait(false);
            return true;
        }, token)).ConfigureAwait(false);
        context.Response.Cookies.Delete(CookieName, Cookie(clock.UtcNow));
    }

    private async Task<bool> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = Guid.CreateVersion7();
        var acquired = await InHostAsync(() => commands.ExecuteAsync(IdentityBrowserSessionSql.Acquire,
            IdentitySqlParameters.Create(("Id", _row!.Id), ("Version", _row.Version), ("LeaseId", lease),
                ("LeaseUntil", clock.UtcNow.AddSeconds(30)), ("Now", clock.UtcNow)), cancellationToken))
            .ConfigureAwait(false) == 1;
        if (acquired) _leaseId = lease;
        return acquired;
    }

    public async Task ReplaceCredentialsAsync(TenantContextTokenResponse credentials, CancellationToken cancellationToken)
    {
        if (Ticket is null || !_leaseId.HasValue) throw new IdentityBrowserSessionUnavailableException(true);
        try
        {
            await SaveAsync(Ticket with { AccessToken = credentials.AccessToken,
                RefreshToken = credentials.RefreshToken ?? Ticket.RefreshToken,
                AccessExpiresAtUtc = credentials.ExpiresAtUtc }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 上下文切换可能已消费旧 refresh；票据保存失败后同样禁止以后重放旧凭据。
            await InvalidateAfterCredentialFailureAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task SaveAsync(IdentityBrowserTicket ticket, CancellationToken cancellationToken)
    {
        var ciphertext = Protect(ticket);
        var count = await InHostAsync(() => commands.ExecuteAsync(IdentityBrowserSessionSql.Replace,
            IdentitySqlParameters.Create(("Id", _row!.Id), ("Version", _row.Version), ("LeaseId", _leaseId),
                ("Ciphertext", ciphertext), ("Now", clock.UtcNow)), cancellationToken)).ConfigureAwait(false);
        if (count != 1) throw new IdentityBrowserSessionUnavailableException(false);
        _row = _row! with { Ciphertext = ciphertext, Version = _row.Version + 1, RefreshLeaseId = null,
            RefreshLeaseUntilUtc = null };
        Ticket = ticket; _leaseId = null;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (_leaseId is null || _row is null) return;
        await InHostAsync(() => commands.ExecuteAsync(IdentityBrowserSessionSql.Release,
            IdentitySqlParameters.Create(("Id", _row.Id), ("LeaseId", _leaseId)), cancellationToken))
            .ConfigureAwait(false);
        _leaseId = null;
    }

    private async Task InvalidateAfterCredentialFailureAsync()
    {
        try { await DeleteAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        // 删除也失败时保留数据库租约；finally 不得释放它，否则旧 refresh 会重新变成可领取。
        // 另一节点只能等待该租约，过期后关闭票据，不能接管并重放旧凭据。
        Ticket = null; _row = null; _leaseId = null;
    }

    public async Task DeleteCookieSessionAsync(HttpContext context, CancellationToken cancellationToken, bool clearCookie = true)
    {
        var handle = context.Request.Cookies[CookieName];
        if (_row is null && handle?.Length == 43) _row = await FindAsync(Hash(handle), cancellationToken).ConfigureAwait(false);
        await DeleteAsync(cancellationToken).ConfigureAwait(false);
        if (clearCookie) context.Response.Cookies.Delete(CookieName, Cookie(clock.UtcNow));
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (_row is not null)
            await InHostAsync(() => commands.ExecuteAsync(IdentityBrowserSessionSql.Delete,
                IdentitySqlParameters.Create(("Id", _row.Id)), cancellationToken)).ConfigureAwait(false);
        Ticket = null; _row = null; _leaseId = null;
    }

    private Task<IdentityBrowserSessionRow?> FindAsync(string hash, CancellationToken cancellationToken) =>
        InHostAsync(() => queries.QuerySingleOrDefaultAsync<IdentityBrowserSessionRow>(IdentityBrowserSessionSql.Find,
            IdentitySqlParameters.Create(("HandleHash", hash), ("Now", clock.UtcNow)), cancellationToken));
    private string Protect(IdentityBrowserTicket ticket) => _protector.Protect(JsonSerializer.Serialize(ticket,
        IdentityJsonSerializerContext.Default.IdentityBrowserTicket));
    private IdentityBrowserTicket Unprotect(string text) => JsonSerializer.Deserialize(_protector.Unprotect(text),
        IdentityJsonSerializerContext.Default.IdentityBrowserTicket) ?? throw new CryptographicException("Invalid browser ticket.");
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static DateTimeOffset AccessExpiration(string token) => new(new JsonWebToken(token).ValidTo, TimeSpan.Zero);
    private static CookieOptions Cookie(DateTimeOffset expires) => new()
        { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires, IsEssential = true };

    private async Task<T> InHostAsync<T>(Func<Task<T>> action)
    {
        var wasHost = tenant.IsHost;
        var previous = tenant.Id is Guid id ? new TenantContext(id, tenant.Identifier!, tenant.Name!) : null;
        tenant.SetHost();
        try { return await action().ConfigureAwait(false); }
        finally
        {
            if (previous is not null) tenant.SetTenant(previous);
            else if (wasHost) tenant.SetHost();
            else tenant.Clear();
        }
    }
}
