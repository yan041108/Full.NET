using System.Text.Json;
using Full.NET.Agents.Approvals;

namespace Full.NET.UnitTests.Ai;

/// <summary>审批绑定在参数、主体、期限或消费状态变化时必须拒绝。</summary>
[TestClass]
public sealed class AiApprovalBindingTests
{
    private static readonly Guid RunId = Guid.CreateVersion7();
    private static readonly Guid OperationId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();

    [TestMethod]
    public void Approved_unconsumed_binding_matches_operation_and_arguments()
    {
        using var arguments = JsonDocument.Parse("""{"sessionId":"00000000-0000-0000-0000-000000000001","title":"new"}""");
        var hash = AgentApprovalGate.ComputeArgumentsHash(arguments.RootElement);
        var binding = CreateBinding(hash, AgentApprovalDecisionKeys.Approved, consumed: null, expiresInMinutes: 5);
        Assert.IsTrue(AgentApprovalGate.TryValidateForConsume(
            binding,
            OperationId,
            RunId,
            "ai.chat.sessions.rename",
            1,
            arguments.RootElement,
            ActorId,
            TenantId,
            DateTimeOffset.UtcNow,
            out var error));
        Assert.AreEqual(string.Empty, error);
    }

    [TestMethod]
    public void Arguments_hash_mismatch_is_rejected()
    {
        using var approved = JsonDocument.Parse("""{"sessionId":"00000000-0000-0000-0000-000000000001","title":"new"}""");
        var binding = CreateBinding(
            AgentApprovalGate.ComputeArgumentsHash(approved.RootElement),
            AgentApprovalDecisionKeys.Approved,
            consumed: null,
            expiresInMinutes: 5);
        using var tampered = JsonDocument.Parse("""{"sessionId":"00000000-0000-0000-0000-000000000002","title":"new"}""");
        Assert.IsFalse(AgentApprovalGate.TryValidateForConsume(
            binding,
            OperationId,
            RunId,
            "ai.chat.sessions.rename",
            1,
            tampered.RootElement,
            ActorId,
            TenantId,
            DateTimeOffset.UtcNow,
            out var error));
        Assert.AreEqual("ai.agent_approval.arguments_mismatch", error);
    }

    [TestMethod]
    public void Expired_or_consumed_bindings_are_rejected()
    {
        using var arguments = JsonDocument.Parse("{}");
        var hash = AgentApprovalGate.ComputeArgumentsHash(arguments.RootElement);
        var expired = CreateBinding(hash, AgentApprovalDecisionKeys.Approved, consumed: null, expiresInMinutes: -1);
        Assert.IsFalse(AgentApprovalGate.TryValidateForConsume(
            expired,
            OperationId,
            RunId,
            "ai.chat.sessions.rename",
            1,
            arguments.RootElement,
            ActorId,
            TenantId,
            DateTimeOffset.UtcNow,
            out var expiredError));
        Assert.AreEqual("ai.agent_approval.expired", expiredError);

        var consumed = CreateBinding(hash, AgentApprovalDecisionKeys.Approved, consumed: DateTimeOffset.UtcNow, expiresInMinutes: 5);
        Assert.IsFalse(AgentApprovalGate.TryValidateForConsume(
            consumed,
            OperationId,
            RunId,
            "ai.chat.sessions.rename",
            1,
            arguments.RootElement,
            ActorId,
            TenantId,
            DateTimeOffset.UtcNow,
            out var consumedError));
        Assert.AreEqual("ai.agent_approval.already_consumed", consumedError);
    }

    [TestMethod]
    public void Pending_or_denied_decisions_are_rejected()
    {
        using var arguments = JsonDocument.Parse("{}");
        var hash = AgentApprovalGate.ComputeArgumentsHash(arguments.RootElement);
        var pending = CreateBinding(hash, AgentApprovalDecisionKeys.Pending, consumed: null, expiresInMinutes: 5);
        Assert.IsFalse(AgentApprovalGate.TryValidateForConsume(
            pending,
            OperationId,
            RunId,
            "ai.chat.sessions.rename",
            1,
            arguments.RootElement,
            ActorId,
            TenantId,
            DateTimeOffset.UtcNow,
            out var pendingError));
        Assert.AreEqual("ai.agent_approval.pending", pendingError);
    }

    private static AgentApprovalBinding CreateBinding(
        string hash,
        string decision,
        DateTimeOffset? consumed,
        int expiresInMinutes) => new(
        Guid.CreateVersion7(),
        RunId,
        OperationId,
        TenantId,
        "ai.chat.sessions.rename",
        1,
        hash,
        AgentApprovalGate.CurrentPolicyVersion,
        ActorId,
        decision,
        DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes),
        consumed,
        1);
}
