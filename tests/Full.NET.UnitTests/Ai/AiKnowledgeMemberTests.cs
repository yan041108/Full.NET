using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

/// <summary>先保护成员输入边界，租户和真实撤权由双库 HTTP 验证。</summary>
[TestClass]
public sealed class AiKnowledgeMemberTests
{
    [TestMethod]
    public void Empty_replacement_revokes_all_explicit_members() =>
        Assert.IsNull(AiKnowledgeMembers.Validate(Guid.CreateVersion7(), [], 1));

    [TestMethod]
    public void Active_member_identifiers_are_bounded_unique_and_exclude_owner()
    {
        var owner = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();
        Assert.IsNull(AiKnowledgeMembers.Validate(owner, [member], 1));
        Assert.IsNotNull(AiKnowledgeMembers.Validate(owner, null, 1));
        Assert.IsNotNull(AiKnowledgeMembers.Validate(owner, [Guid.Empty], 1));
        Assert.IsNotNull(AiKnowledgeMembers.Validate(owner, [owner], 1));
        Assert.IsNotNull(AiKnowledgeMembers.Validate(owner, [member, member], 1));
        Assert.IsNotNull(AiKnowledgeMembers.Validate(owner, Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray(), 1));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Replacement_requires_positive_resource_version(int version) =>
        Assert.IsNotNull(AiKnowledgeMembers.Validate(Guid.CreateVersion7(), [], version));
}
