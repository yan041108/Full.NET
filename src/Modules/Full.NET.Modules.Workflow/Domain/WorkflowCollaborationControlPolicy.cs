using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>正文固定原绑定、实际目标终态和最小投影；不携带目标私有完整表单。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowCollaborationControlBody(Guid ControlId, Guid CallId, string KindKey, string BindingHash,
    Guid? TargetInstanceId, Guid TargetDefinitionVersionId, long? TargetRevision, string OutcomeKey,
    string ProjectionJson, string ProjectionHash, int UnusedActivities);

/// <summary>仅由目标关闭入口在根锁与持久准入证明后创建，不能由HTTP提交。</summary>
internal sealed record WorkflowCollaborationCancellationContext(Guid CallId, string BindingHash, Guid TargetInstanceId);

/// <summary>摘要仅校验完整性；身份仍由认证信封及各侧自己的耐久事实提供。</summary>
internal static class WorkflowCollaborationControlPolicy
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    { "controlId", "callId", "kindKey", "bindingHash", "targetInstanceId", "targetDefinitionVersionId", "targetRevision", "outcomeKey", "projectionJson", "projectionHash", "unusedActivities" };
    internal static string EventType(string kind) => kind switch
    {
        "created" => WorkflowCollaborationIntegrationEventTypes.Created,
        "completed" => WorkflowCollaborationIntegrationEventTypes.Completed,
        "cancelled" => WorkflowCollaborationIntegrationEventTypes.Cancelled,
        _ => throw new InvalidDataException("workflow.collaboration.control_invalid")
    };

    internal static bool TryRead(WorkflowCollaborationControlRecord row, WorkflowCollaborationAdmissionRecord admission,
        out WorkflowCollaborationControlBody? body)
    {
        body = null;
        if (row.Id == Guid.Empty || row.CallId != admission.Id || row.Revision < 1 ||
            row.SourceTenantScopeKey != admission.SourceTenantScopeKey || row.TargetTenantScopeKey != admission.TargetTenantScopeKey ||
            row.KindKey is not ("created" or "completed" or "cancelled") || Encoding.UTF8.GetByteCount(row.BodyJson) > 262_144 ||
            WorkflowCollaborationCallPolicy.Hash(row.BodyJson) != row.BodyHash ||
            !WorkflowCollaborationAdmissionProof.TryRead(admission, out var binding)) return false;
        try
        {
            using var json = JsonDocument.Parse(row.BodyJson);
            if (!WorkflowVotePolicy.UniqueObject(json.RootElement) || json.RootElement.EnumerateObject().Count() != Names.Count ||
                json.RootElement.EnumerateObject().Any(property => !Names.Contains(property.Name))) return false;
            body = JsonSerializer.Deserialize(json.RootElement, WorkflowJsonSerializerContext.Default.WorkflowCollaborationControlBody);
            if (body is null || body.ControlId != row.Id || body.CallId != row.CallId || body.KindKey != row.KindKey || body.BindingHash != admission.BindingHash ||
                body.TargetInstanceId != admission.TargetInstanceId || body.TargetDefinitionVersionId != binding!.TargetDefinitionVersionId ||
                body.TargetInstanceId == Guid.Empty || body.UnusedActivities < 0 || body.UnusedActivities > binding.ReservedActivities ||
                (body.TargetInstanceId is null ? body.TargetRevision is not null : body.TargetRevision is null or < 1) ||
                body.ProjectionJson is null || Encoding.UTF8.GetByteCount(body.ProjectionJson) > 131_072 || WorkflowCollaborationCallPolicy.Hash(body.ProjectionJson) != body.ProjectionHash) return false;
            using var projection = JsonDocument.Parse(body.ProjectionJson);
            if (!WorkflowVotePolicy.UniqueObject(projection.RootElement) || projection.RootElement.EnumerateObject().Count() > 64) return false;
            return row.KindKey switch
            {
                "created" => body.TargetInstanceId is not null && body.OutcomeKey == "created" && body.UnusedActivities == 0 && body.ProjectionJson == "{}",
                "completed" => body.TargetInstanceId is not null && (body.OutcomeKey is "completed" or "rejected" or "cancelled") &&
                    (body.OutcomeKey == "completed" || body.ProjectionJson == "{}"),
                "cancelled" => body.ProjectionJson == "{}" && (body.TargetInstanceId is null
                    ? body.OutcomeKey == "not_created" && body.UnusedActivities == binding.ReservedActivities
                    : body.OutcomeKey is "completed" or "rejected" or "cancelled"),
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }
}
