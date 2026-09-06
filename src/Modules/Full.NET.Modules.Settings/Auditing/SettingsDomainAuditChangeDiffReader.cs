using Full.NET.Abstractions.Auditing;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Auditing;

/// <summary>从 Settings 域审计表按 TraceId 读取并脱敏变更差异。</summary>
internal sealed class SettingsDomainAuditChangeDiffReader(
    IQueryExecutor queryExecutor) : IDomainAuditChangeDiffReader
{
    public string ModuleKey => "settings";

    public async Task<IReadOnlyList<DomainAuditChangeDiffEntry>> ReadByTraceIdAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<SettingsDomainAuditDiffRecord>(
                SettingsDomainAuditReadSql.ListByTraceId,
                SettingsSqlParameters.Create(("TraceId", traceId)),
                cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(MapRow)
            .ToArray();
    }

    private static DomainAuditChangeDiffEntry MapRow(SettingsDomainAuditDiffRecord row)
    {
        var availability = DomainAuditChangeDiffParser.ParseAvailability(row.DiffSummaryJson);
        var fields = availability == DomainAuditChangeDiffAvailability.Available
            ? DomainAuditChangeDiffParser.ParseFields(row.DiffSummaryJson)
            : [];
        return new DomainAuditChangeDiffEntry(
            row.Id,
            "settings",
            row.ActionKey,
            row.OccurredAtUtc,
            availability,
            fields);
    }
}
