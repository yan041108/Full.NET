using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>Worker 领取持久化意图后在事务外投递标准 logout_token；失败有界重试，终态可人工重放。</summary>
internal sealed class IdentityOidcLogoutDelivery(IQueryExecutor queries, ICommandExecutor commands, IClock clock,
    IHttpClientFactory clients, IdentityOidcSigningKeyRing keys, IOptions<IdentityOidcOptions> options,
    IOptions<DatabaseOptions> database, IdentityOidcKeyGovernance? governance = null)
{
    public const string HttpClientName = "fullnet-oidc-backchannel";
    public async Task<int> RunOnceAsync(CancellationToken token)
    {
        if (governance is not null) await governance.ReportNodeAsync(token).ConfigureAwait(false);
        await commands.ExecuteAsync(database.Value.Provider == DatabaseProvider.MySql
            ? IdentityOidcLogoutSql.ExhaustedMySql : IdentityOidcLogoutSql.ExhaustedSqlServer,
            IdentitySqlParameters.Create(("Now", clock.UtcNow)), token).ConfigureAwait(false);
        var due = await queries.QueryAsync<IdentityOidcLogoutDeliveryRow>(database.Value.Provider == DatabaseProvider.MySql
                ? IdentityOidcLogoutSql.DueMySql : IdentityOidcLogoutSql.DueSqlServer,
            IdentitySqlParameters.Create(("Now", clock.UtcNow)), token).ConfigureAwait(false);
        var delivered = 0;
        foreach (var row in due)
        {
            var lease = Guid.CreateVersion7();
            if (await commands.ExecuteAsync(IdentityOidcLogoutSql.Acquire,
                IdentitySqlParameters.Create(("Id", row.Id), ("AttemptCount", row.AttemptCount), ("LeaseId", lease),
                    ("LeaseUntil", clock.UtcNow.AddSeconds(30)), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) continue;
            string? error = null;
            try
            {
                if (!IdentityOidcBackchannelPolicy.IsAllowed(row.TargetUri, options.Value.BackchannelAllowedOrigins))
                    error = "destination_not_allowed";
                else
                {
                    var jwt = await CreateLogoutTokenAsync(row, token).ConfigureAwait(false);
                    using var request = new HttpRequestMessage(HttpMethod.Post, row.TargetUri)
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = jwt }) };
                    using var response = await clients.CreateClient(HttpClientName).SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) error = "http_" + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { error = "delivery_unavailable"; }
            if (error is null)
            {
                delivered += await commands.ExecuteAsync(IdentityOidcLogoutSql.Complete,
                    IdentitySqlParameters.Create(("Id", row.Id), ("LeaseId", lease), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
            }
            else
            {
                var terminal = row.AttemptCount + 1 >= 8 || error == "destination_not_allowed";
                await commands.ExecuteAsync(IdentityOidcLogoutSql.Fail,
                    IdentitySqlParameters.Create(("Id", row.Id), ("LeaseId", lease), ("StatusKey", terminal ? "failed" : "pending"),
                        ("NextAttempt", clock.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, row.AttemptCount + 1)))),
                        ("ErrorCode", error)), token).ConfigureAwait(false);
            }
        }
        return delivered;
    }

    internal async Task<string> CreateLogoutTokenAsync(IdentityOidcLogoutDeliveryRow row, CancellationToken token)
    {
        if (!keys.UsesEphemeralDevelopmentKey)
        {
            var active = await queries.QuerySingleOrDefaultAsync<string>(IdentityOidcSigningPolicySql.FindActiveKey,
                cancellationToken: token).ConfigureAwait(false);
            if (active is null || !keys.TryActivate(active).IsSuccess) throw new InvalidOperationException("Active logout signing material is unavailable.");
        }
        var subject = new ClaimsIdentity([
            new Claim("sub", row.UserId.ToString("D")), new Claim("sid", row.CenterSessionId.ToString("D")),
            new Claim("jti", row.Id.ToString("D")),
            new Claim("events", "{\"http://schemas.openid.net/event/backchannel-logout\":{}}", JsonClaimValueTypes.Json)
        ]);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer, Audience = row.ClientId, Subject = subject,
            IssuedAt = clock.UtcNow.UtcDateTime, NotBefore = clock.UtcNow.AddSeconds(-30).UtcDateTime,
            Expires = clock.UtcNow.AddMinutes(2).UtcDateTime, SigningCredentials = keys.SigningCredentials,
            TokenType = "logout+jwt"
        });
    }
}

internal sealed class IdentityOidcLogoutHostedService(IServiceScopeFactory scopes,
    ILogger<IdentityOidcLogoutHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                tenant.SetHost();
                try { await scope.ServiceProvider.GetRequiredService<IdentityOidcLogoutDelivery>().RunOnceAsync(stoppingToken).ConfigureAwait(false); }
                finally { tenant.Clear(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { IdentityOidcLogoutLog.Failed(logger); }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

internal static partial class IdentityOidcLogoutLog
{
    // 不记录协议令牌、目标 URL 或异常里的 HTTP 请求详情。
    [LoggerMessage(EventId = 4540, Level = LogLevel.Warning, Message = "OIDC backchannel delivery iteration failed")]
    public static partial void Failed(ILogger logger);
}
