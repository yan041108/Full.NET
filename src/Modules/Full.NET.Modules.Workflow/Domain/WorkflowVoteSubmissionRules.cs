using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>个人答案先按独立表单校验，签名与请求摘要同时绑定选择及完整答案，不借用主表单修订。</summary>
internal static class WorkflowVoteSubmissionRules
{
    internal const int MaximumValueBytes = 256 * 1024;

    public static bool TryCompile(WorkflowFormSchema schema, JsonElement values, string? choiceKey, bool allowAbstain,
        out WorkflowVoteSubmissionData? submission)
    {
        submission = null;
        if (choiceKey is not ("yes" or "no" or "abstain") || choiceKey == "abstain" && !allowAbstain ||
            values.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(values.GetRawText()) > MaximumValueBytes) return false;
        var count = 0;
        if (!UniqueBounded(values, 1, ref count)) return false;
        var fields = values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        if (!WorkflowFormValueValidator.Validate(schema, fields)) return false;
        var body = WorkflowJsonCanonicalizer.Compile(values);
        var evidence = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("choiceKey", choiceKey);
            writer.WritePropertyName("values");
            WorkflowJsonCanonicalizer.WriteElement(writer, values);
            writer.WriteEndObject();
        });
        submission = new(body.CanonicalJson, evidence.CanonicalJson, evidence.ContentHash, choiceKey);
        return true;
    }

    /// <summary>只读取已经过本人Todo授权的该次私有视图，不接收当前成员行中的后来答案。</summary>
    public static bool TryReadView(WorkflowTodoViewRecord view, WorkflowFormSchema schema, bool allowAbstain,
        out WorkflowVotePersonalView? personal)
    {
        personal = null;
        if (view.InputRevision < 1 || (view.OutputJson is null) != (view.OutputRevision is null)) return false;
        try
        {
            if (view.OutputJson is null)
            {
                using var input = JsonDocument.Parse(view.InputJson);
                // 当前协议不保存草稿或隐式预填；未来引入预填必须先定义实际输入与修订协议。
                if (input.RootElement.ValueKind != JsonValueKind.Object || input.RootElement.EnumerateObject().Any()) return false;
                personal = new(input.RootElement.Clone(), view.InputRevision, null, false);
                return true;
            }
            if (view.OutputRevision <= view.InputRevision) return false;
            using var output = JsonDocument.Parse(view.OutputJson);
            var element = output.RootElement;
            if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != 2 ||
                !element.TryGetProperty("choiceKey", out var choice) || choice.ValueKind != JsonValueKind.String ||
                !element.TryGetProperty("values", out var values) || !TryCompile(schema, values, choice.GetString(), allowAbstain, out _)) return false;
            personal = new(values.Clone(), view.OutputRevision!.Value, choice.GetString(), true);
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static string HashRequest(Guid todoId, Guid actor, long todoRevision, long ballotRevision,
        WorkflowVoteSubmissionData submission, string? comment, Guid? signatureFileId) => WorkflowJsonCanonicalizer.Compile(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("todoId", todoId);
        writer.WriteString("actorUserId", actor);
        writer.WriteNumber("expectedRevision", todoRevision);
        writer.WriteNumber("expectedBallotRevision", ballotRevision);
        writer.WriteString("submissionHash", submission.EvidenceHash);
        writer.WriteString("comment", string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
        if (signatureFileId is { } file) writer.WriteString("signatureFileId", file); else writer.WriteNull("signatureFileId");
        writer.WriteEndObject();
    }).ContentHash;

    private static bool UniqueBounded(JsonElement element, int depth, ref int count)
    {
        if (depth > 16 || ++count > 16_384) return false;
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !UniqueBounded(property.Value, depth+1, ref count)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) if (!UniqueBounded(value, depth+1, ref count)) return false;
        }
        return element.ValueKind != JsonValueKind.Undefined;
    }
}

/// <summary>个人内容和带选择的证据分别固化；签名不能从赞成改成反对后继续复用。</summary>
internal sealed record WorkflowVoteSubmissionData(string ValuesJson, string EvidenceJson, string EvidenceHash, string ChoiceKey);
/// <summary>该次投票待办私有视图；历史和当前成员身份分离，避免转办后泄露后任答案。</summary>
internal sealed record WorkflowVotePersonalView(JsonElement Values, long Revision, string? ChoiceKey, bool Submitted);
