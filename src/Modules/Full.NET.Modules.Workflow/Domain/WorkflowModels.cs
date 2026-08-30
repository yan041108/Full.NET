using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

internal sealed record WorkflowDefinitionDraft(
    int SchemaVersion,
    IReadOnlyList<WorkflowNodeDraft> Nodes);

internal sealed record WorkflowNodeDraft(
    string NodeKey,
    string NodeTypeKey,
    int NodeSchemaVersion,
    JsonElement Config);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowFormSchema(
    int SchemaVersion,
    int AdapterVersion,
    IReadOnlyList<WorkflowFormSection> Sections);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowFormSection(
    string SectionKey,
    IReadOnlyList<WorkflowFormField> Fields);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkflowFormField(
    string FieldKey,
    string FieldTypeKey,
    bool Required,
    IReadOnlyDictionary<string, JsonElement> Constraints);

internal sealed record WorkflowCompiledArtifact(string CanonicalJson, string ContentHash);

internal sealed record WorkflowCompilationResult(
    bool IsSuccess,
    WorkflowCompiledArtifact? Value,
    string? ErrorCode)
{
    public static WorkflowCompilationResult Success(WorkflowCompiledArtifact value) =>
        new(true, value, null);

    public static WorkflowCompilationResult Failure(string errorCode) =>
        new(false, null, errorCode);
}
