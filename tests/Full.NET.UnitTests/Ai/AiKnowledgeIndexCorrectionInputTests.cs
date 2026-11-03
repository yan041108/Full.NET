using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>坏或不完整的修订身份不能被解释为自动输入，比较与恢复均认完整冻结身份。</summary>
[TestClass]
public sealed class AiKnowledgeIndexCorrectionInputTests
{
    [TestMethod]
    public void Automatic_is_explicitly_empty_and_human_identity_requires_every_field()
    {
        Assert.IsTrue(AiKnowledgeIndexCorrectionInput.IsValid(new())); Assert.IsNull(AiKnowledgeIndexCorrectionInput.Identity(new()));
        var source = Guid.CreateVersion7(); var revision = Guid.CreateVersion7(); var row = Human(source, revision);
        Assert.AreEqual(new AiKnowledgeCorrectionIdentity(source, revision, 1, new string('a', 64), new string('b', 64)), AiKnowledgeIndexCorrectionInput.Identity(row));
        foreach (var invalid in new[]
        {
            new AiKnowledgeIndexDispatchRecord { CorrectionSourceId = source },
            new AiKnowledgeIndexDispatchRecord { CorrectionRevisionId = revision },
            Human(Guid.Empty, revision), Human(source, Guid.Empty), Human(source, revision, 0),
            Human(source, revision, hash: new string('A', 64)), Human(source, revision, hash: new string('a', 63))
        }) { Assert.IsFalse(AiKnowledgeIndexCorrectionInput.IsValid(invalid)); Assert.IsNull(AiKnowledgeIndexCorrectionInput.Identity(invalid)); }
    }
    [TestMethod]
    public void Source_comparison_detects_revision_head_and_payload_drift()
    {
        var source = Guid.CreateVersion7(); var revision = Guid.CreateVersion7(); var original = Human(source, revision);
        Assert.IsTrue(AiKnowledgeIndexEmbeddingService.SameSource(original, Human(source, revision)));
        Assert.IsFalse(AiKnowledgeIndexEmbeddingService.SameSource(original, Human(source, Guid.CreateVersion7())));
        Assert.IsFalse(AiKnowledgeIndexEmbeddingService.SameSource(original, Human(Guid.CreateVersion7(), revision)));
        Assert.IsFalse(AiKnowledgeIndexEmbeddingService.SameSource(original, Human(source, revision, 2)));
        Assert.IsFalse(AiKnowledgeIndexEmbeddingService.SameSource(original, Human(source, revision, hash: new string('c', 64))));
    }
    private static AiKnowledgeIndexDispatchRecord Human(Guid source, Guid revision, int head = 1, string? hash = null) => new()
    { CorrectionSourceId = source, CorrectionRevisionId = revision, CorrectionHeadVersion = head, CorrectionSourceHash = new string('a', 64), CorrectionPayloadHash = hash ?? new string('b', 64) };
}
