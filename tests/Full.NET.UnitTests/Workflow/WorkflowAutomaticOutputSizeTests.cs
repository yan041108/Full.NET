using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowAutomaticOutputSizeTests
{
    [TestMethod]
    public async Task Oversized_aggregate_is_rejected_before_any_file_preparation()
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var claims = Substitute.For<IHostFileReferenceClaimService>(); var descriptors = Substitute.For<IHostFileDescriptorReader>();
        var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var actor = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), "{}", "Internal", 1, actor, now));
        var attachment = new WorkflowFormAttachmentCoordinator(claims, descriptors, query, command, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            NullLogger<WorkflowFormAttachmentCoordinator>.Instance, tenant, Substitute.For<ITenantResourceFileDescriptorReader>());
        var service = new WorkflowAutomaticOutputCoordinator(query, command, Substitute.For<IClock>(), new(query, command), attachment);
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "one", null, "active", 1, actor, now, null, null, null, null, null, null);
        var file = Guid.CreateVersion7();
        var values = new Dictionary<string, JsonElement> { ["content"] = JsonSerializer.SerializeToElement(new string('界', 50000)), ["document"] = JsonSerializer.SerializeToElement(new[] { file }) };
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("content", "text", false, new Dictionary<string, JsonElement>()),
            new("document", "attachment", false, new Dictionary<string, JsonElement> { ["maxCount"] = JsonSerializer.SerializeToElement(1),
                ["maxSizeBytes"] = JsonSerializer.SerializeToElement(1024), ["allowedExtensions"] = JsonSerializer.SerializeToElement(new[] { "png" }) })])]);
        var result = await service.PrepareAsync(instance, new(Guid.CreateVersion7(), "{}", WorkflowSubprocessBindings.Hash("{}"), 1, null, null), schema,
            new Dictionary<string, JsonElement>(), new(values, values, true), _ => true, default);
        Assert.IsNull(result); Assert.AreEqual(0, claims.ReceivedCalls().Count()); Assert.AreEqual(0, descriptors.ReceivedCalls().Count());
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }
}
