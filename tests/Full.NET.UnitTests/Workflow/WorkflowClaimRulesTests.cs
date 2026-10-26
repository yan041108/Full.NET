using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>旧候选快照不能替代当前来源授权，释放不重置循环预算或变更实际所有者。</summary>
[TestClass]
public sealed class WorkflowClaimRulesTests
{
    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    public void Claim_requires_both_frozen_and_current_membership(bool frozen, bool current, bool expected)
    {
        var actor = Guid.CreateVersion7(); var other = Guid.CreateVersion7();
        Assert.AreEqual(expected, WorkflowClaimRules.CanClaim(actor, frozen ? [actor] : [other], current ? [actor] : [other],
            "available", 1, 1, 0));
    }

    [TestMethod]
    public void Stale_owned_or_exhausted_pool_cannot_be_claimed()
    {
        var actor = Guid.CreateVersion7();
        Assert.IsFalse(WorkflowClaimRules.CanClaim(actor, [actor], [actor], "claimed", 1, 1, 0));
        Assert.IsFalse(WorkflowClaimRules.CanClaim(actor, [actor], [actor], "available", 2, 1, 0));
        Assert.IsFalse(WorkflowClaimRules.CanClaim(actor, [actor], [actor], "available", 1, 1, 32));
        Assert.IsFalse(WorkflowClaimRules.CanClaim(actor, [actor, actor], [actor], "available", 1, 1, 0));
    }

    [TestMethod]
    public void Release_is_owned_explicit_and_bounded()
    {
        var actor = Guid.CreateVersion7();
        Assert.IsTrue(WorkflowClaimRules.CanRelease(new(true), "claimed", actor, actor, 1, 2, 2));
        Assert.IsFalse(WorkflowClaimRules.CanRelease(new(false), "claimed", actor, actor, 1, 2, 2));
        Assert.IsFalse(WorkflowClaimRules.CanRelease(new(true), "claimed", Guid.CreateVersion7(), actor, 1, 2, 2));
        Assert.IsFalse(WorkflowClaimRules.CanRelease(new(true), "claimed", actor, actor, 32, 2, 2));
        Assert.IsFalse(WorkflowClaimRules.CanRelease(new(true), "claimed", actor, actor, 1, 3, 2));
    }
}
