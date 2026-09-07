using System.Data;
using Dapper;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证表单版本查询的真实列集合可以物化，避免替身隐藏构造参数漂移。</summary>
[TestClass]
public sealed class WorkflowFormVersionMaterializationTests
{
    /// <summary>表单版本不包含属于流程定义的业务标题模板列。</summary>
    [TestMethod]
    public void Form_version_projection_materializes_without_workflow_definition_title()
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("FormDefinitionId", typeof(Guid));
        table.Columns.Add("VersionNumber", typeof(int));
        table.Columns.Add("SchemaVersion", typeof(int));
        table.Columns.Add("AdapterVersion", typeof(int));
        table.Columns.Add("ComponentCatalogVersion", typeof(int));
        table.Columns.Add("FormSchemaJson", typeof(string));
        table.Columns.Add("WebRenderSchemaJson", typeof(string));
        table.Columns.Add("ContentHash", typeof(string));
        table.Columns.Add("PublishedById", typeof(Guid));
        table.Columns.Add("PublishedAtUtc", typeof(DateTimeOffset));
        var id = Guid.NewGuid();
        table.Rows.Add(id, Guid.NewGuid(), 1, 1, 1, 1, "{}", "{}", "hash", Guid.NewGuid(), DateTimeOffset.UtcNow);
        using var reader = table.CreateDataReader();
        var parse = reader.GetRowParser<WorkflowFormVersionRecord>();
        Assert.IsTrue(reader.Read());
        var version = parse(reader);
        Assert.AreEqual(id, version.Id);
        Assert.AreEqual("{}", version.FormSchemaJson);
    }
}
