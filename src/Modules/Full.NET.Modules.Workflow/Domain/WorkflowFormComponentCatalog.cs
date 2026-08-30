namespace Full.NET.Modules.Workflow.Domain;

internal sealed record WorkflowFormComponentDefinition(
    string FieldTypeKey,
    bool Designable,
    bool Publishable,
    bool Executable,
    IReadOnlyList<string> ConstraintKeys)
{
    public bool SupportsConstraint(string key) => ConstraintKeys.Contains(key, StringComparer.Ordinal);
}

internal sealed record WorkflowFormComponentCatalogSnapshot(
    int CatalogVersion,
    int SchemaVersion,
    int AdapterVersion,
    IReadOnlyList<WorkflowFormComponentDefinition> Components);

/// <summary>定义当前部署可设计、可发布并可执行的闭合表单组件目录。</summary>
internal static class WorkflowFormComponentCatalog
{
    private static readonly WorkflowFormComponentDefinition[] ComponentDefinitions =
    [
        Component("text", "minLength", "maxLength"),
        Component("textarea", "minLength", "maxLength"),
        Component("integer", "minimum", "maximum"),
        Component("decimal", "scale", "minimum", "maximum"),
        Component("money", "scale", "minimum", "maximum"),
        Component("date", "minimum", "maximum"),
        Component("time", "minimum", "maximum"),
        Component("datetime", "minimum", "maximum"),
        Component("radio", "options"),
        Component("checkbox", "options"),
        Component("select", "options"),
        Component("switch"),
    ];

    private static readonly IReadOnlyDictionary<string, WorkflowFormComponentDefinition> ByFieldType =
        ComponentDefinitions.ToDictionary(component => component.FieldTypeKey, StringComparer.Ordinal);

    public static WorkflowFormComponentCatalogSnapshot Current { get; } =
        new(1, 1, 1, Array.AsReadOnly(ComponentDefinitions));

    public static bool TryGet(
        string? fieldTypeKey,
        out WorkflowFormComponentDefinition? component)
    {
        component = null;
        return fieldTypeKey is not null && ByFieldType.TryGetValue(fieldTypeKey, out component);
    }

    private static WorkflowFormComponentDefinition Component(
        string fieldTypeKey,
        params string[] constraintKeys) =>
        new(fieldTypeKey, true, true, true, Array.AsReadOnly(constraintKeys));
}
