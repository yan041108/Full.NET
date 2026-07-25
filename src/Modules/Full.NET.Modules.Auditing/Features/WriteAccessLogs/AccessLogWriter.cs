using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>
/// 访问日志写入载荷；不含 QueryString 与 Body，避免敏感数据落库。
/// </summary>
internal sealed record AccessLogWriteModel(
    string HttpMethod,
    string RequestPath,
    int StatusCode,
    int DurationMs,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint,
    bool IsAuthenticated);

/// <summary>
/// 尽力写入访问日志；失败只记警告，不得拖垮业务响应。
/// </summary>
internal sealed class AccessLogWriter(
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<AccessLogWriter> logger)
{
    public async Task TryWriteAsync(
        AccessLogWriteModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await commandExecutor.ExecuteAsync(
                    AccessLogSql.Insert,
                    new
                    {
                        Id = idGenerator.NewId(),
                        OccurredAtUtc = clock.UtcNow,
                        model.HttpMethod,
                        model.RequestPath,
                        model.StatusCode,
                        model.DurationMs,
                        model.UserId,
                        model.TenantId,
                        model.TraceId,
                        model.ClientIpFingerprint,
                        model.IsAuthenticated,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to persist HTTP access log for {HttpMethod} {RequestPath}.",
                model.HttpMethod,
                model.RequestPath);
        }
    }
}
