using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>实例内引用不代表当前办理人拥有文件可见性，读取必须沿用自己的节点和分支输入。</summary>
[TestClass]
public sealed class WorkflowAttachmentAccessTests
{
    [TestMethod]
    [DataRow("hidden", true, null, false)]
    [DataRow("editable", true, null, true)]
    [DataRow("editable", false, null, false)]
    [DataRow("editable", true, false, true)]
    [DataRow("editable", false, true, true)]
    [DataRow("hidden", false, true, false)]
    public async Task Attachment_requires_the_actors_visible_view(string fieldPolicy, bool included, bool? outputIncludesFile, bool allowed)
    {
        var query = Substitute.For<IQueryExecutor>(); var files = Substitute.For<IHostFileContentReader>();
        var instanceId = Guid.CreateVersion7(); var fileId = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "test", "one", null, "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
        query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForInstanceFile, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        var canonical = JsonSerializer.Serialize(new { nodes = new[] { new { nodeKey = "approval", nodeTypeKey = "human.approval",
            config = new { fieldPolicies = new { file = fieldPolicy } } } } });
        const string schema = """{"schemaVersion":1,"adapterVersion":1,"sections":[{"sectionKey":"main","fields":[{"fieldKey":"file","fieldTypeKey":"attachment","required":false,"constraints":{"maxCount":1,"maxSizeBytes":1024,"allowedExtensions":["png"]}}]}]}""";
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value, canonical, schema, null, "active"));
        query.QueryAsync<WorkflowAttachmentReadViewRecord>(WorkflowFormSubmissionAttachmentSql.ReadViewsSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowAttachmentReadViewRecord("approval", included ? JsonSerializer.Serialize(new { file = new[] { fileId } }) : "{}",
                outputIncludesFile.HasValue ? JsonSerializer.Serialize(new { file = outputIncludesFile.Value ? new[] { fileId } : Array.Empty<Guid>() }) : null) });
        files.OpenReadyContentAsync(fileId, Arg.Any<CancellationToken>()).Returns(Result<HostFileContent>.Success(new(new MemoryStream(), "image/png", "file.png")));
        var service = new WorkflowFormAttachmentAccessService(query, files, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Substitute.For<ITenantResourceFileStore>());
        var result = await service.OpenInstanceAttachmentAsync(instanceId, fileId, actor, "host");
        Assert.AreEqual(allowed, result.IsSuccess);
        await files.Received(allowed ? 1 : 0).OpenReadyContentAsync(fileId, Arg.Any<CancellationToken>());
        if (result.IsSuccess) await result.Value!.Content.DisposeAsync();
    }
}
