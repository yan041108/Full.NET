using System.Text.RegularExpressions;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs;

namespace Full.NET.Modules.Auditing.Features.QueryDomainChangeDiffs;

/// <summary>聚合各模块 <see cref="IDomainAuditChangeDiffReader"/> 并按 TraceId 返回脱敏差异。</summary>
internal sealed partial class DomainAuditChangeDiffQueryService(
    IEnumerable<IDomainAuditChangeDiffReader> readers)
{
    [GeneratedRegex(@"^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceIdPattern();

    public async Task<Result<DomainChangeDiffQueryResponse>> QueryByTraceIdAsync(
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return Result<DomainChangeDiffQueryResponse>.Failure(new Error(
                AuditingErrorCodes.DomainChangeDiffTraceIdRequired,
                "traceId is required.",
                ErrorType.Validation));
        }

        var normalizedTraceId = traceId.Trim();
        if (normalizedTraceId.Length > 64
            || !TraceIdPattern().IsMatch(normalizedTraceId)
            || OutboundCallAuditSanitizer.ContainsSensitiveContent(normalizedTraceId))
        {
            return Result<DomainChangeDiffQueryResponse>.Failure(new Error(
                AuditingErrorCodes.DomainChangeDiffTraceIdInvalid,
                "traceId is invalid.",
                ErrorType.Validation));
        }

        var entries = new List<DomainAuditChangeDiffEntryResponse>();
        foreach (var reader in readers)
        {
            var moduleEntries = await reader
                .ReadByTraceIdAsync(normalizedTraceId, cancellationToken)
                .ConfigureAwait(false);
            entries.AddRange(moduleEntries.Select(MapEntry));
        }

        entries.Sort(static (left, right) =>
            right.OccurredAtUtc.CompareTo(left.OccurredAtUtc));

        return Result<DomainChangeDiffQueryResponse>.Success(
            new DomainChangeDiffQueryResponse(normalizedTraceId, entries));
    }

    private static DomainAuditChangeDiffEntryResponse MapEntry(DomainAuditChangeDiffEntry entry) =>
        new(
            entry.AuditId,
            entry.ModuleKey,
            entry.ActionKey,
            entry.OccurredAtUtc,
            ToAvailabilityCode(entry.Availability),
            entry.Fields
                .Select(field => new DomainAuditChangeDiffFieldResponse(
                    field.FieldKey,
                    field.BeforeValue,
                    field.AfterValue))
                .ToArray());

    private static string ToAvailabilityCode(DomainAuditChangeDiffAvailability availability) =>
        availability switch
        {
            DomainAuditChangeDiffAvailability.Available => "available",
            DomainAuditChangeDiffAvailability.NoDiffRecorded => "no_diff_recorded",
            DomainAuditChangeDiffAvailability.Unparseable => "unparseable",
            _ => "no_diff_recorded",
        };
}
