using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>只给已授权字段视图中的共享附件补安全名称；历史元数据不授予新的内容读取能力。</summary>
internal static class WorkflowAttachmentNameReader
{
    internal static async Task<IReadOnlyDictionary<string, string>?> ReadAsync(Guid instanceId, WorkflowManagementScope scope,
        WorkflowFormSchema schema, IReadOnlyDictionary<string, JsonElement> visibleValues, IQueryExecutor query, CancellationToken token)
    {
        var visible = WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, visibleValues)
            .SelectMany(field => field.Value).Distinct().Order().ToArray();
        if (visible.Length == 0) return null;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in visible.Chunk(256))
        {
            var allowed = batch.ToHashSet();
            var rows = await query.QueryAsync<WorkflowRemoteAttachmentNameRecord>(WorkflowCollaborationFileSql.ReadVisibleNames,
                WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", scope.TenantScopeKey), ("FileIds", batch)), token).ConfigureAwait(false);
            foreach (var row in rows)
                if (allowed.Contains(row.FileId) && row.OriginalFileName is { Length: > 0 and <= 260 })
                    names.TryAdd(row.FileId.ToString("D"), row.OriginalFileName);
        }
        return names.Count == 0 ? null : names;
    }
}
