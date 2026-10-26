using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布版本的动作策略只收紧平台授权，不给当前用户增加权限。</summary>
internal sealed record WorkflowHumanActionPolicy(IReadOnlySet<string> Actions, bool SignRequired, bool CommentRequired)
{
    private static readonly HashSet<string> KnownActions = new(StringComparer.Ordinal)
        { "approve", "reject", "return", "transfer", "delegate", "countersign", "remind" };
    private static readonly HashSet<string> TaskActions = new(StringComparer.Ordinal) { "complete", "return", "transfer", "delegate", "remind" };
    private static readonly HashSet<string> VoteActions = new(StringComparer.Ordinal) { "vote", "transfer", "delegate", "remind" };
    private static readonly HashSet<string> ClaimActions = new(StringComparer.Ordinal) { "complete", "release", "transfer", "delegate", "remind" };

    public static bool TryRead(JsonElement config, out WorkflowHumanActionPolicy? policy, string nodeTypeKey = WorkflowHumanActivityKinds.Approval)
    {
        policy = null;
        if (config.ValueKind != JsonValueKind.Object ||
            config.EnumerateObject().GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            !ReadFlag(config, "signRequired", out var sign) || !ReadFlag(config, "commentRequired", out var comment))
            return false;
        if (!WorkflowHumanActivityKinds.IsFormActivity(nodeTypeKey)) return false;
        IReadOnlySet<string> knownActions = nodeTypeKey switch
        {
            WorkflowHumanActivityKinds.Task => TaskActions,
            WorkflowHumanActivityKinds.Vote => VoteActions,
            WorkflowHumanActivityKinds.Claim => ClaimActions,
            _ => KnownActions,
        };
        var actions = new HashSet<string>(knownActions, StringComparer.Ordinal);
        if (config.TryGetProperty("operationPermissions", out var permissions))
        {
            if (permissions.ValueKind != JsonValueKind.Object) return false;
            actions.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in permissions.EnumerateObject())
            {
                if (!knownActions.Contains(entry.Name) || !seen.Add(entry.Name) ||
                    entry.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                if (entry.Value.GetBoolean()) actions.Add(entry.Name);
            }
        }
        policy = new(actions, sign, comment);
        return true;
    }

    public static bool TryResolve(string canonicalJson, string nodeKey, out WorkflowHumanActionPolicy? policy)
    {
        policy = null;
        var draft = JsonSerializer.Deserialize(canonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var node = draft?.Nodes.SingleOrDefault(item => item.NodeKey == nodeKey);
        return node is not null && WorkflowHumanActivityKinds.IsFormActivity(node.NodeTypeKey) && TryRead(node.Config, out policy, node.NodeTypeKey);
    }

    public Result<bool> Validate(string actionKey, string? comment, Guid? signatureFileId)
    {
        if (!Actions.Contains(actionKey)) return Failure(WorkflowErrorCodes.TodoActionForbidden, ErrorType.Forbidden);
        if (CommentRequired && string.IsNullOrWhiteSpace(comment)) return Failure(WorkflowErrorCodes.TodoCommentRequired, ErrorType.Validation);
        if (SignRequired && (signatureFileId is null || signatureFileId == Guid.Empty))
            return Failure(WorkflowErrorCodes.TodoSignatureRequired, ErrorType.Validation);
        return Result<bool>.Success(true);
    }

    private static bool ReadFlag(JsonElement config, string key, out bool value)
    {
        value = false;
        if (!config.TryGetProperty(key, out var item)) return true;
        if (item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = item.GetBoolean();
        return true;
    }

    private static Result<bool> Failure(string code, ErrorType type) =>
        Result<bool>.Failure(new Error(code, "The workflow action does not satisfy the published node policy.", type));
}
