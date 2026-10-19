using System.Security.Claims;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Full.NET.Modules.Auditing.Features.QueryLogTimeline;

/// <summary>跨来源的 Host 安全摘要聚合；逐来源授权，失败必须显式标记而非伪装成零条。</summary>
internal sealed class LogTimelineQueryService(IEnumerable<ILogTimelineReader> readers,
    IAuthorizationService authorization, ICurrentTenant tenant)
{
    public async Task<Result<LogTimelineResponse>> QueryAsync(LogTimelineQuery query,
        ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!tenant.IsHost) return Result<LogTimelineResponse>.Failure(new Error(
            "auditing.timeline.host_required", "Host context is required.", ErrorType.Forbidden));
        var validation = Validate(query);
        if (validation is not null) return Result<LogTimelineResponse>.Failure(validation);

        var entries = new List<LogTimelineEntry>();
        var sources = new List<LogTimelineCoverage>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        foreach (var reader in readers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!(await authorization.AuthorizeAsync(user, null,
                FullNetPermissionPolicies.For(reader.RequiredPermission)).ConfigureAwait(false)).Succeeded)
            {
                sources.Add(new(reader.Source, "forbidden"));
                continue;
            }

            using var sourceBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            sourceBudget.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                sourceBudget.Token.ThrowIfCancellationRequested();
                var result = await reader.ReadAsync(query, sourceBudget.Token).ConfigureAwait(false);
                entries.AddRange(result.Entries);
                sources.Add(new(reader.Source, result.Status));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                sources.Add(new(reader.Source, "timeout"));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 不向客户端返回底层 SQL、文件路径或连接异常正文。
                sources.Add(new(reader.Source, "unavailable"));
            }
        }

        var ordered = entries.OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
        return Result<LogTimelineResponse>.Success(new(query.FromUtc, query.ToUtc,
            ordered.Take(query.Limit).ToArray(), sources,
            ordered.Length > query.Limit || sources.Any(source => source.Status != "available")));
    }

    internal static Error? Validate(LogTimelineQuery query)
    {
        if (query.FromUtc >= query.ToUtc || query.ToUtc - query.FromUtc > TimeSpan.FromDays(1)
            || query.Limit is < 1 or > 200
            || (query.TraceId is null && query.SessionId is null && query.UserId is null && query.ClientIpFingerprint is null)
            || query.SessionId == Guid.Empty || query.UserId == Guid.Empty
            || (query.TraceId is not null && !IsHex(query.TraceId, 32))
            || (query.ClientIpFingerprint is not null && !IsHex(query.ClientIpFingerprint, 64)))
            return new Error("auditing.timeline.query_invalid", "Use a time range up to 24 hours and exact correlation identifiers.", ErrorType.Validation);
        return null;
    }

    private static bool IsHex(string value, int length) => value.Length == length
        && value.All(char.IsAsciiHexDigit) && value.Any(character => character != '0');
}

/// <summary>时间线独立读取权限；还需要各来源读取权限。</summary>
public static class LogTimelinePermissions
{
    /// <summary>读取 Host 日志关联摘要。</summary>
    public const string Read = "auditing.timeline.read";
}

/// <summary>来源可用性，包含截断、超时、未配置或无权限状态。</summary>
public sealed record LogTimelineCoverage(string Source, string Status);

/// <summary>有界时间线；Partial 表示本次查询覆盖不完整，false 也不保证日志从未采样或丢弃。</summary>
public sealed record LogTimelineResponse(DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    IReadOnlyList<LogTimelineEntry> Entries, IReadOnlyList<LogTimelineCoverage> Sources, bool Partial);
