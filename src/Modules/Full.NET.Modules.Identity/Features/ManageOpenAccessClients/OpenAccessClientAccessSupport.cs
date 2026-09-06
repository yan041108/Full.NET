using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

/// <summary>
/// 接入方应用认证审计与配额守卫；审计指纹使用 AccessKeyId 哈希，禁止跨应用关联。
/// </summary>
internal sealed class OpenAccessClientAccessSupport(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>写入接入方 ApiKey 认证审计；失败与成功均记录，但不包含密钥或签名。</summary>
    public async Task WriteApiKeyAuditAsync(
        Guid? userId,
        string accessKeyId,
        string resultCode,
        bool succeeded,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await WriteAuditAsync(
                userId,
                accessKeyId,
                IdentityOpenAccessClientAuditEventTypes.ApiKeyAuthentication,
                resultCode,
                succeeded,
                httpContext,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 在成功认证前检查当日配额；仅对绑定 OpenAccess 应用且配置了配额的 API Key 生效。
    /// </summary>
    /// <returns>配额已用尽时返回稳定错误，否则返回 <see langword="null"/>。</returns>
    public async Task<Error?> TryGetQuotaExceededErrorAsync(
        Guid apiKeyId,
        string accessKeyId,
        CancellationToken cancellationToken)
    {
        var quota = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientQuotaRow>(
                OpenAccessClientObservabilitySql.FindQuotaByApiKeyId,
                IdentitySqlParameters.Create(("ApiKeyId", apiKeyId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (quota?.DailyRequestQuota is not int dailyQuota || dailyQuota <= 0)
        {
            return null;
        }

        var (windowStartUtc, windowEndUtc) = ResolveUtcDayWindow(clock.UtcNow);
        var usage = await CountUsageAsync(
                accessKeyId,
                windowStartUtc,
                windowEndUtc,
                cancellationToken)
            .ConfigureAwait(false);
        if (usage.SuccessCount >= dailyQuota)
        {
            return new Error(
                IdentityErrorCodes.OpenAccessClientQuotaExceeded,
                "The OpenAccess client daily request quota has been exceeded.",
                ErrorType.RateLimited);
        }

        return null;
    }

    internal static string ComputeAccessKeyFingerprint(string accessKeyId) =>
        TokenHash.Compute(accessKeyId);

    internal static (DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc) ResolveUtcDayWindow(
        DateTimeOffset nowUtc)
    {
        var windowStartUtc = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            0,
            0,
            0,
            TimeSpan.Zero);
        return (windowStartUtc, windowStartUtc.AddDays(1));
    }

    internal async Task<OpenAccessClientUsageCountRow> CountUsageAsync(
        string accessKeyId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        var provider = databaseOptions.Value.Provider;
        var statement = provider switch
        {
            DatabaseProvider.SqlServer => OpenAccessClientObservabilitySql.CountTodayUsageSqlServer,
            DatabaseProvider.MySql => OpenAccessClientObservabilitySql.CountTodayUsageMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        return await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientUsageCountRow>(
                statement,
                IdentitySqlParameters.Create(
                    ("AccessKeyFingerprint", ComputeAccessKeyFingerprint(accessKeyId)),
                    ("WindowStartUtc", windowStartUtc),
                    ("WindowEndUtc", windowEndUtc)),
                cancellationToken)
            .ConfigureAwait(false)
            ?? new OpenAccessClientUsageCountRow();
    }

    private async Task WriteAuditAsync(
        Guid? userId,
        string accessKeyId,
        string eventType,
        string resultCode,
        bool succeeded,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            userId,
            null,
            ComputeAccessKeyFingerprint(accessKeyId),
            eventType,
            resultCode,
            succeeded,
            Truncate(httpContext.Connection.RemoteIpAddress?.ToString(), 64),
            Truncate(httpContext.Request.Headers.UserAgent.ToString(), 512),
            null,
            clock.UtcNow);
        await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
