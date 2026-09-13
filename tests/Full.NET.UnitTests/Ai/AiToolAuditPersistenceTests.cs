using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Agents.Approvals;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>审计不能把零行当作成功；参数和结果只记录服务端结构化摘要。</summary>
[TestClass]
public sealed class AiToolAuditPersistenceTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Audit_intent_and_receipt_require_persistence_async(int affected)
    {
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(affected);
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        using var arguments = JsonDocument.Parse("{\"token\":\"private-secret\"}");
        var operation = new ToolInvocation(Guid.CreateVersion7(), null, "ai.tools.ping", 1, arguments.RootElement);
        var writer = new AiAgentToolCallAuditWriter(commands, new NullApprovalBindingReader(), tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        var intent = writer.BeginOperationAsync(Guid.NewGuid(), operation, "permission", "started", null, null, default);
        if (affected == 0)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await intent);
            return;
        }

        await intent;
        var values = (IReadOnlyDictionary<string, object?>)commands.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.AreEqual(operation.OperationId, values["Id"]);
        Assert.DoesNotContain("private-secret", (string)values["InputSummary"]!, StringComparison.Ordinal);
        var receipt = writer.CompleteOperationAsync(Guid.NewGuid(), operation.OperationId, "succeeded", null, 12, 1, default);
        if (affected == 0) await Assert.ThrowsAsync<InvalidOperationException>(() => receipt); else await receipt;
    }

    private sealed class NullApprovalBindingReader : IAgentToolApprovalBindingReader
    {
        public ValueTask<AgentApprovalBinding?> FindBindingByOperationAsync(Guid operationId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentApprovalBinding?>(null);
    }
}
