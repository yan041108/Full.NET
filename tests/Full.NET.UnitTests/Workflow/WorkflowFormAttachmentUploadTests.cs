using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>字段约束必须在Files副作用前校验，上传的所有者与作用域仅由可信上下文决定。</summary>
[TestClass]
public sealed class WorkflowFormAttachmentUploadTests
{
    [TestMethod]
    [DataRow("/api/v1/workflow/definition-versions/{versionId:guid}/attachment-uploads", WorkflowPermissions.InstancesStart)]
    [DataRow("/api/v1/workflow/todos/{todoId:guid}/attachment-uploads", WorkflowPermissions.TodosRead)]
    public async Task Upload_endpoints_require_resource_and_dedicated_upload_permissions(string path, string resourcePermission)
    {
        var builder = WebApplication.CreateBuilder(); var module = new WorkflowModule(); module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build(); module.MapEndpoints(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == path);
        CollectionAssert.AreEquivalent(new[] { FullNetPermissionPolicies.For(resourcePermission), FullNetPermissionPolicies.For(WorkflowPermissions.FormAttachmentsUpload) },
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(item => item.Policy).ToArray());
    }
    private const string Schema = """{"schemaVersion":1,"adapterVersion":1,"sections":[{"sectionKey":"main","fields":[{"fieldKey":"file","fieldTypeKey":"attachment","required":false,"constraints":{"maxCount":1,"maxSizeBytes":4,"allowedExtensions":["png"]}}]}]}""";
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task Upload_uses_the_current_host_or_tenant_owner_without_borrowing_a_transaction(bool scoped)
    {
        var data = Create(scoped); using var stream = new MemoryStream([1, 2, 3]);
        var result = await data.Service.ForStartAsync(data.Version, data.Actor, "file", "evidence.png", "image/png", stream, 3, default);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code); Assert.AreEqual(data.File, result.Value!.FileId);
        if (scoped)
        {
            await data.TenantFiles.Received(1).UploadAsync("workflow", data.Actor, data.Actor, "evidence.png", "image/png", Arg.Any<Stream>(), 3, Arg.Any<CancellationToken>());
            Assert.AreEqual(0, data.HostFiles.ReceivedCalls().Count());
        }
        else
        {
            await data.HostFiles.Received(1).UploadAsync(data.Actor, "evidence.png", "image/png", Arg.Any<Stream>(), 3, Arg.Any<CancellationToken>());
            Assert.AreEqual(0, data.TenantFiles.ReceivedCalls().Count());
        }
    }

    [TestMethod]
    [DataRow("field")][DataRow("extension")][DataRow("oversize")][DataRow("short")][DataRow("extra")]
    [DataRow("disabled")][DataRow("publication")][DataRow("missing")]
    public async Task Invalid_or_unpublished_input_has_no_file_or_quota_side_effect(string defect)
    {
        var data = Create(false); var asset = data.Asset;
        if (defect == "disabled") data.Query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(asset with { DefinitionStatusKey = "disabled" });
        if (defect == "missing") data.Query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((WorkflowRuntimeAssetRecord?)null);
        if (defect == "publication") data.Query.QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        using var stream = new MemoryStream(defect == "extra" ? [1, 2, 3, 4] : [1, 2, 3]);
        var result = await data.Service.ForStartAsync(data.Version, data.Actor, defect == "field" ? "hidden" : "file",
            defect == "extension" ? "script.html" : "evidence.png", "image/png", stream, defect == "oversize" ? 5 : defect == "short" ? 4 : 3, default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, data.HostFiles.ReceivedCalls().Count()); Assert.AreEqual(0, data.TenantFiles.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Foreign_transaction_is_rejected_before_any_query()
    {
        var data = Create(false); data.State.HasTransaction.Returns(true); data.Query.ClearReceivedCalls(); using var stream = new MemoryStream([1]);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.Service.ForStartAsync(data.Version, data.Actor, "file", "f.png", "image/png", stream, 1, default));
        Assert.AreEqual(0, data.Query.ReceivedCalls().Count()); Assert.AreEqual(0, data.HostFiles.ReceivedCalls().Count());
    }

    private static Case Create(bool scoped)
    {
        var tenant = new CurrentTenantAccessor(); if (scoped) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "tenant", "租户")); else tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>(); var state = Substitute.For<IDataTransactionState>(); var actor = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var version = Guid.CreateVersion7();
        var asset = new WorkflowRuntimeAssetRecord(version, Guid.CreateVersion7(), "{}", Schema, null, "active");
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(asset);
        query.QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Schema);
        var host = Substitute.For<IHostFileUploadWriter>(); var files = Substitute.For<ITenantResourceFileStore>(); var entitlement = Substitute.For<ITenantFeatureEntitlementPort>();
        entitlement.IsFeatureGrantedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        host.UploadAsync(actor, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), 3, Arg.Any<CancellationToken>()).Returns(Result<HostFileUploadResult>.Success(new(file, 3, new string('a', 64))));
        files.UploadAsync("workflow", actor, actor, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), 3, Arg.Any<CancellationToken>()).Returns(Result<TenantResourceFileReference>.Success(new(file, 3, new string('a', 64))));
        return new(new(query, tenant, Substitute.For<IClock>(), state, Substitute.For<IActiveTenantContextResolver>(), entitlement, host, files, null!),
            query, state, host, files, actor, file, version, asset);
    }
    private sealed record Case(WorkflowFormAttachmentUploadService Service, IQueryExecutor Query, IDataTransactionState State,
        IHostFileUploadWriter HostFiles, ITenantResourceFileStore TenantFiles, Guid Actor, Guid File, Guid Version, WorkflowRuntimeAssetRecord Asset);
}
