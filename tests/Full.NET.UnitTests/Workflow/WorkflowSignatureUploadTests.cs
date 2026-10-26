using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>采集入口不能越过待办归属、活动修订和内容上限，也不能在租户路径借用 Host 上传。</summary>
[TestClass]
public sealed class WorkflowSignatureUploadTests
{
    [TestMethod]
    [DataRow("foreign")]
    [DataRow("stale")]
    [DataRow("fake-image")]
    [DataRow("oversize")]
    public async Task Invalid_capture_never_uploads(string failure)
    {
        var fixture = Fixture();
        var bytes = failure == "fake-image" ? new byte[8] : new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var result = await fixture.Service.UploadAsync(fixture.Todo.Id, failure == "foreign" ? Guid.CreateVersion7() : fixture.Actor,
            failure == "stale" ? 2 : 1, "image/png", new MemoryStream(bytes), failure == "oversize" ? 1_048_577 : bytes.Length, default);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, fixture.HostFiles.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.TenantFiles.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Tenant_capture_binds_current_actor_resource_without_host_upload()
    {
        var fixture = Fixture();
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var result = await fixture.Service.UploadAsync(fixture.Todo.Id, fixture.Actor, 1, "image/png", new MemoryStream(bytes), bytes.Length, default);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, fixture.HostFiles.ReceivedCalls().Count());
        await fixture.TenantFiles.Received(1).UploadAsync("workflow", fixture.Actor, fixture.Actor, "signature.png", "image/png",
            Arg.Any<Stream>(), bytes.Length, Arg.Any<CancellationToken>());
    }

    private static UploadFixture Fixture()
    {
        var tenantId = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7();
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var query = Substitute.For<IQueryExecutor>(); var host = Substitute.For<IHostFileUploadWriter>(); var files = Substitute.For<ITenantResourceFileStore>();
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), actor, "active", DateTimeOffset.UtcNow,
            null, null, 1, "approval", 1, null, null, null, null, null, null);
        var instance = new WorkflowInstanceRecord(instanceId, tenantId, "tenant", $"tenant:{tenantId:N}", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "one",
            null, "active", 1, actor, DateTimeOffset.UtcNow, null, null, null, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(todo);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value,
                """{"schemaVersion":1,"nodes":[{"nodeKey":"approval","nodeTypeKey":"human.approval","nodeSchemaVersion":1,"config":{}}]}""", "{}", null, "active"));
        files.UploadAsync("workflow", actor, actor, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(
            Result<TenantResourceFileReference>.Success(new(Guid.CreateVersion7(), 8, "hash")));
        return new(new(query, tenant, host, files), actor, todo, host, files);
    }

    private sealed record UploadFixture(WorkflowSignatureUploadService Service, Guid Actor, WorkflowTodoRuntimeRecord Todo,
        IHostFileUploadWriter HostFiles, ITenantResourceFileStore TenantFiles);
}
