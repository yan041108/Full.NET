using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Agent Run Lease Boundary。</summary>
[TestClass]
public sealed class AiAgentRunLeaseBoundaryTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Lease_mutation_requires_live_expiry_deadline_and_current_status(bool renewal)
    {
        var sql = (renewal ? AiAgentRunSql.RenewLease : AiAgentRunSql.UpdateRunStatus).Text;
        StringAssert.Contains(sql, "LeaseExpiresAtUtc > @Now");
        StringAssert.Contains(sql, "DeadlineAtUtc > @Now");
        StringAssert.Contains(sql, "StatusKey IN ('running', 'retry_scheduled', 'awaiting_approval', 'reconciliation_required')");
    }
}
