using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs;

/// <summary>显式 opt-in 出站调用审计写入；调用方只提交安全元数据。</summary>
public sealed class OutboundCallAuditHandler(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock,
    ILogger<OutboundCallAuditHandler> logger)
{
    public async Task RecordAsync(
        OutboundCallAuditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sanitized = Sanitize(request);
        if (sanitized.HadSensitiveInput)
        {
            logger.LogWarning(
                "Outbound call audit input contained sensitive markers for provider {ProviderKey}.",
                sanitized.Record.ProviderKey);
        }

        var record = sanitized.Record with
        {
            Id = idGenerator.NewId(),
            OccurredAtUtc = clock.UtcNow,
        };

        await commandExecutor.ExecuteAsync(
                OutboundCallLogSql.Insert,
                record,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static SanitizedOutboundCallAudit Sanitize(OutboundCallAuditRequest request)
    {
        var hadSensitive = OutboundCallAuditSanitizer.ContainsSensitiveContent(request.ProviderKey)
            || OutboundCallAuditSanitizer.ContainsSensitiveContent(request.OperationKey)
            || OutboundCallAuditSanitizer.ContainsSensitiveContent(request.DestinationHostCategory)
            || (request.SafeErrorCode is not null
                && OutboundCallAuditSanitizer.ContainsSensitiveContent(request.SafeErrorCode))
            || (request.TraceId is not null
                && OutboundCallAuditSanitizer.ContainsSensitiveContent(request.TraceId));

        var record = new OutboundCallLogRecord
        {
            Id = Guid.Empty,
            OccurredAtUtc = default,
            ProviderKey = OutboundCallAuditSanitizer.SanitizeProviderKey(request.ProviderKey),
            OperationKey = OutboundCallAuditSanitizer.SanitizeOperationKey(request.OperationKey),
            DestinationHostCategory = OutboundCallAuditSanitizer.SanitizeDestinationHostCategory(
                request.DestinationHostCategory),
            StatusCode = Math.Clamp(request.StatusCode, 0, 999),
            Succeeded = request.Succeeded,
            DurationMs = Math.Max(request.DurationMs, 0),
            RetryCount = Math.Max(request.RetryCount, 0),
            TraceId = OutboundCallAuditSanitizer.SanitizeTraceId(request.TraceId),
            SafeErrorCode = OutboundCallAuditSanitizer.SanitizeSafeErrorCode(request.SafeErrorCode),
            TenantId = request.TenantId,
            UserId = request.UserId,
        };

        return new SanitizedOutboundCallAudit(record, hadSensitive);
    }

    internal OutboundCallLogRecord CreateRecord(OutboundCallAuditRequest request)
    {
        var sanitized = Sanitize(request);
        return sanitized.Record with
        {
            Id = idGenerator.NewId(),
            OccurredAtUtc = clock.UtcNow,
        };
    }

    internal readonly record struct SanitizedOutboundCallAudit(
        OutboundCallLogRecord Record,
        bool HadSensitiveInput);
}
