using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.CrossModulePorts;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.UnitTests.Workflow;

/// <summary>实例 ID、业务身份、可信租户和原启动参数哈希共同构成恢复证据。</summary>
[TestClass]
public sealed class WorkflowBusinessStartProofDirectoryTests
{
    [TestMethod]
    public async Task Matching_original_receipt_returns_terminal_snapshot_without_mutation()
    {
        var f = new Fixture(); var value = await f.Read();
        Assert.IsNotNull(value); Assert.AreEqual(f.Instance.Id, value.InstanceId);
        Assert.AreEqual(f.Instance.StartedById, value.StartedById);
        Assert.AreEqual(f.Instance.CancelledAtUtc, value.CompletedAtUtc);
        await f.Queries.Received(1).QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    [DataRow("host")]
    [DataRow("tenant")]
    [DataRow("business")]
    [DataRow("actor")]
    [DataRow("hash")]
    [DataRow("action")]
    [DataRow("receipt")]
    public async Task Invalid_scope_or_original_receipt_never_returns_binding(string kind)
    {
        var f = new Fixture();
        if (kind == "host") f.Tenant.IsHost.Returns(true);
        if (kind == "tenant") f.Instance = f.Instance with { TenantId = Guid.NewGuid() };
        if (kind == "business") f.Instance = f.Instance with { BusinessId = Guid.NewGuid().ToString("D") };
        if (kind == "actor") f.Receipt = f.Receipt! with { ActorUserId = Guid.NewGuid() };
        if (kind == "hash") f.Receipt = f.Receipt! with { RequestHash = "bad" };
        if (kind == "action") f.Receipt = f.Receipt! with { ActionKey = "cancel" };
        if (kind == "receipt") f.Receipt = null;
        Assert.IsNull(await f.Read());
        if (kind == "host") Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }
    private sealed class Fixture
    {
        internal readonly Guid TenantId = Guid.NewGuid();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly WorkflowBusinessStartProofRequest Request;
        internal WorkflowInstanceRecord Instance;
        internal WorkflowActionReceiptRecord? Receipt;
        internal Fixture()
        {
            Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(TenantId);
            Request = new(Guid.NewGuid(), "demo.enterprise_request", Guid.NewGuid().ToString("D"), "{}", "submit:record:1");
            Instance = new(Request.InstanceId, TenantId, "tenant", $"tenant:{TenantId:N}", Guid.NewGuid(), Guid.NewGuid(),
                Request.BusinessType, Request.BusinessId, "Title", "cancelled", 2, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), null,
                Guid.NewGuid(), DateTimeOffset.UtcNow, "cancel", null, null);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Instance.DefinitionVersionId:D}\n{Request.BusinessType}\n{Request.BusinessId}\n{{}}")));
            Receipt = new("start", Instance.StartedById, 1, Request.IdempotencyKey, hash, null);
            Queries.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Instance);
            Queries.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Receipt);
        }
        internal Task<WorkflowBusinessInstanceSnapshot?> Read() => new WorkflowBusinessStartProofDirectoryAdapter(Queries, Tenant).FindAsync(Request);
    }
}
