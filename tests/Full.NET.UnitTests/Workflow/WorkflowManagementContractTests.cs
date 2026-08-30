using System.Reflection;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowManagementContractTests
{
    private static readonly Assembly WorkflowAssembly =
        typeof(Full.NET.Modules.Workflow.WorkflowModule).Assembly;

    [TestMethod]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageForms.CreateWorkflowFormRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageForms.UpdateWorkflowFormDraftRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageForms.PublishWorkflowFormRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageDefinitions.CreateWorkflowDefinitionRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageDefinitions.UpdateWorkflowDefinitionDraftRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageDefinitions.PublishWorkflowDefinitionRequest")]
    public void Management_requests_must_not_expose_server_authority_fields(string typeName)
    {
        var requestType = WorkflowAssembly.GetType(typeName, throwOnError: false);

        Assert.IsNotNull(requestType, $"Missing management contract: {typeName}");
        var propertyNames = requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsFalse(propertyNames.Contains("TenantId"));
        Assert.IsFalse(propertyNames.Contains("TenantScopeKey"));
        Assert.IsFalse(propertyNames.Contains("Hash"));
        Assert.IsFalse(propertyNames.Contains("ContentHash"));
        Assert.IsFalse(propertyNames.Contains("Published"));
        Assert.IsFalse(propertyNames.Contains("ComponentCatalogVersion"));
    }

    [TestMethod]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageForms.UpdateWorkflowFormDraftRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageForms.PublishWorkflowFormRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageDefinitions.UpdateWorkflowDefinitionDraftRequest")]
    [DataRow("Full.NET.Modules.Workflow.Features.ManageDefinitions.PublishWorkflowDefinitionRequest")]
    public void Mutating_existing_draft_requires_expected_revision(string typeName)
    {
        var requestType = WorkflowAssembly.GetType(typeName, throwOnError: false);

        Assert.IsNotNull(requestType, $"Missing management contract: {typeName}");
        Assert.IsNotNull(requestType.GetProperty("ExpectedRevision"));
    }

    [TestMethod]
    public void Published_definition_version_persists_the_bound_form_version()
    {
        var recordType = WorkflowAssembly.GetType(
            "Full.NET.Modules.Workflow.Persistence.WorkflowDefinitionVersionRecord",
            throwOnError: true)!;

        var formVersionId = recordType.GetProperty(
            "FormVersionId",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.IsNotNull(
            formVersionId,
            "A published definition must retain its immutable FormVersionId binding.");
        Assert.AreEqual(typeof(Guid), formVersionId.PropertyType);
    }

    [TestMethod]
    public void Todo_detail_exposes_the_visible_form_schema_hash()
    {
        var responseType = WorkflowAssembly.GetType(
            "Full.NET.Modules.Workflow.Features.ManageMyTodos.WorkflowTodoRuntimeResponse",
            throwOnError: true)!;

        var formSchemaHash = responseType.GetProperty(
            "FormSchemaHash",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.IsNotNull(
            formSchemaHash,
            "移动端必须获得可见 Schema 摘要，禁止不同字段策略复用同一缓存条目。");
        Assert.AreEqual(typeof(string), formSchemaHash.PropertyType);
    }
}
