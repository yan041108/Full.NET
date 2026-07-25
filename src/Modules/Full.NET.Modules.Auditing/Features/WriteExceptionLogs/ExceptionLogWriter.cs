using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Auditing.Features.WriteExceptionLogs;

/// <summary>
/// 异常日志写入载荷；消息与堆栈已截断，不含请求体。
/// </summary>
internal sealed record ExceptionLogWriteModel(
    string ExceptionType,
    string Message,
    string? StackTrace,
    string? HttpMethod,
    string? RequestPath,
    Guid? UserId,
    Guid? TenantId,
    string? TraceId,
    string? ClientIpFingerprint);

/// <summary>
/// 尽力写入异常日志；失败只记警告，不得拖垮异常处理管道。
/// </summary>
internal sealed class ExceptionLogWriter(
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<ExceptionLogWriter> logger)
{
    public async Task TryWriteAsync(
        ExceptionLogWriteModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await commandExecutor.ExecuteAsync(
                    ExceptionLogSql.Insert,
                    new
                    {
                        Id = idGenerator.NewId(),
                        OccurredAtUtc = clock.UtcNow,
                        model.ExceptionType,
                        model.Message,
                        model.StackTrace,
                        model.HttpMethod,
                        model.RequestPath,
                        model.UserId,
                        model.TenantId,
                        model.TraceId,
                        model.ClientIpFingerprint,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to persist exception log for {ExceptionType}.",
                model.ExceptionType);
        }
    }
}
