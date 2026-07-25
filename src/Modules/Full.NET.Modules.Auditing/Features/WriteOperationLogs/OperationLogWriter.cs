using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Auditing.Features.WriteOperationLogs;

/// <summary>
/// 操作日志写入载荷；仅汇总写操作元数据，不含 Body。
/// </summary>
internal sealed record OperationLogWriteModel(
    string ActionKey,
    string HttpMethod,
    string RequestPath,
    int StatusCode,
    int DurationMs,
    bool Succeeded,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint,
    string? PermissionCode);

/// <summary>
/// 尽力写入操作日志；失败只记警告，不得拖垮业务响应。
/// </summary>
internal sealed class OperationLogWriter(
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<OperationLogWriter> logger)
{
    public async Task TryWriteAsync(
        OperationLogWriteModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await commandExecutor.ExecuteAsync(
                    OperationLogSql.Insert,
                    new
                    {
                        Id = idGenerator.NewId(),
                        OccurredAtUtc = clock.UtcNow,
                        model.ActionKey,
                        model.HttpMethod,
                        model.RequestPath,
                        model.StatusCode,
                        model.DurationMs,
                        model.Succeeded,
                        model.UserId,
                        model.TenantId,
                        model.TraceId,
                        model.ClientIpFingerprint,
                        model.PermissionCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to persist HTTP operation log for {ActionKey}.",
                model.ActionKey);
        }
    }
}
