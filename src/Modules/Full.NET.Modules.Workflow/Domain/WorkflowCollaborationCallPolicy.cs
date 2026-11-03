using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>协作等待只引用已接受的固定授权，发布与运行分别复核当前源绑定和权威政策。</summary>
internal sealed record WorkflowCollaborationCallPolicy(Guid GrantId, long GrantRevision, string PolicyHash)
{
    internal const string NodeType = "process.collaboration";
    internal static bool TryRead(JsonElement config, out WorkflowCollaborationCallPolicy? policy)
    {
        policy = null;
        if (!WorkflowVotePolicy.UniqueObject(config) || config.EnumerateObject().Any(property =>
                property.Name is not ("nodeName" or "nextNodeKeys" or "collaborationPolicy")) ||
            !config.TryGetProperty("collaborationPolicy", out var value) || !WorkflowVotePolicy.UniqueObject(value) ||
            value.EnumerateObject().Count() != 4 ||
            !value.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32Safe(out var version) || version != 1 ||
            !WorkflowCollaborationOfferPolicy.GuidValue(value, "grantId", out var id) ||
            !value.TryGetProperty("grantRevision", out var revision) || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out var expected) || expected != 2 ||
            !value.TryGetProperty("policyHash", out var hash) || hash.ValueKind != JsonValueKind.String ||
            !WorkflowCollaborationAcceptancePolicy.Digest(hash.GetString())) return false;
        policy = new(id, expected, hash.GetString()!);
        return true;
    }

    /// <summary>附件原引用只能留在源本地preparing记录，发布前必须替换为真实能力。</summary>
    internal static bool TryProject(WorkflowFormSchema schema, WorkflowCollaborationOfferPolicy offer,
        WorkflowCollaborationAcceptancePolicy acceptance, IReadOnlyDictionary<string, JsonElement> values, out string? projection, bool preparingFiles = false)
    {
        projection = null;
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var projected = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in acceptance.InputFields)
        {
            if (!offer.InputFieldKeys.Contains(pair.Key, StringComparer.Ordinal) || !fields.TryGetValue(pair.Key, out var field) ||
                !WorkflowCollaborationValueProjection.Supports(field, preparingFiles))
                return false;
            if (values.TryGetValue(pair.Key, out var value))
            {
                if (!WorkflowCollaborationValueProjection.Valid(field, value, preparingFiles)) return false;
                projected.Add(pair.Value, value.Clone());
            }
            else if (field.Required) return false;
        }
        projection = JsonSerializer.Serialize(projected.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        return Encoding.UTF8.GetByteCount(projection) <= 131_072;
    }

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>独立调用身份绑定真实源激活、分叉输入、授权及目标固定版本，重试不得重新取当前表单。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowCollaborationCallBinding(Guid CallId, Guid DeliveryId, Guid SourceInstanceId, Guid SourceDefinitionVersionId,
    Guid SourceFormVersionId, Guid ExecutionEpochId, string SourceTenantScopeKey, string TargetTenantScopeKey,
    Guid? SourceTenantId, Guid? TargetTenantId, string NodeKey, string ContinuationNodeKey, Guid? ParallelJoinId, string? ParallelBranchKey,
    long InputFormRevision, string InputHash, Guid GrantId, long GrantRevision, string PolicyHash, Guid TargetDefinitionVersionId,
    string TargetSummaryHash, int ReservedActivities, int Depth, string ProjectionHash);

/// <summary>调用与后继各保留自己的真实等待，不复用同作用域子流程的调用身份。</summary>
internal sealed record WorkflowCollaborationWaitPlan(string NodeKey, string ContinuationNodeKey, WorkflowCollaborationCallPolicy Policy,
    WorkflowFormSchema SourceSchema) : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);
