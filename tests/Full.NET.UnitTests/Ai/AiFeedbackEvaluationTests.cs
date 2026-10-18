using Full.NET.Abstractions.Time;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工候选结构、原会话与短期凭据边界；不把候选有效当成已获来源授权。</summary>
[TestClass]
public sealed class AiFeedbackEvaluationTests
{
    private static AiFeedbackEvaluationSample Sample() => new("zh-CN", "改写的问题", "改写的参考答案", "改写的依据", false);

    [TestMethod]
    [DataRow('中', false)][DataRow('中', true)]
    [DataRow('<', false)][DataRow('<', true)]
    [DataRow('"', false)][DataRow('"', true)]
    public void Maximum_valid_escaped_samples_round_trip(char character, bool scoped)
    {
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var codec = new AiFeedbackEvaluationToken(new EphemeralDataProtectionProvider(), clock);
        var actor = Guid.CreateVersion7(); Guid? tenant = scoped ? Guid.CreateVersion7() : null; var login = Guid.CreateVersion7();
        var sample = Sample() with { Question = new(character, 2048), ExpectedAnswer = new(character, 4096), Evidence = new(character, 8192) };
        var envelope = new AiFeedbackEvaluationEnvelope(1, actor, tenant, login, Guid.CreateVersion7(), 2, 3, new('a', 64), sample, clock.UtcNow.AddMinutes(15));
        Assert.IsTrue(AiFeedbackEvaluationPolicy.Valid(sample));
        var candidate = codec.Protect(envelope);
        Assert.IsTrue(candidate.CandidateToken.Length <= 128 * 1024);
        Assert.AreEqual(envelope, codec.Read(candidate.CandidateToken, actor, tenant, login));
    }

    [TestMethod]
    public void Canonical_sample_hash_is_stable_and_content_sensitive()
    {
        var sample = Sample(); Assert.IsTrue(AiFeedbackEvaluationPolicy.Valid(sample));
        Assert.AreEqual(AiFeedbackEvaluationPolicy.Hash(sample), AiFeedbackEvaluationPolicy.Hash(sample with { }));
        Assert.AreNotEqual(AiFeedbackEvaluationPolicy.Hash(sample), AiFeedbackEvaluationPolicy.Hash(sample with { ShouldRefuse = true }));
    }
    [TestMethod]
    [DataRow("question")][DataRow("answer")][DataRow("evidence")][DataRow("language")][DataRow("unicode")][DataRow("control")]
    public void Malformed_or_unbounded_samples_are_rejected(string field)
    {
        var sample = field switch
        {
            "question" => Sample() with { Question = new string('q', 2049) },
            "answer" => Sample() with { ExpectedAnswer = new string('a', 4097) },
            "evidence" => Sample() with { Evidence = new string('e', 8193) },
            "language" => Sample() with { LanguageTag = "zh-cn" },
            "unicode" => Sample() with { Question = "\ud800" },
            _ => Sample() with { Evidence = "x\0y" }
        };
        Assert.IsFalse(AiFeedbackEvaluationPolicy.Valid(sample));
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Candidate_is_encrypted_and_bound_to_original_actor_scope_and_login(bool scoped)
    {
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var codec = new AiFeedbackEvaluationToken(new EphemeralDataProtectionProvider(), clock);
        var actor = Guid.CreateVersion7(); Guid? tenant = scoped ? Guid.CreateVersion7() : null; var login = Guid.CreateVersion7();
        var envelope = new AiFeedbackEvaluationEnvelope(1, actor, tenant, login, Guid.CreateVersion7(), 2, 3, new('a', 64), Sample(), clock.UtcNow.AddMinutes(15));
        var candidate = codec.Protect(envelope);
        Assert.IsFalse(candidate.CandidateToken.Contains("改写", StringComparison.Ordinal));
        Assert.AreEqual(envelope, codec.Read(candidate.CandidateToken, actor, tenant, login));
        Assert.IsNull(codec.Read(candidate.CandidateToken, Guid.CreateVersion7(), tenant, login));
        Assert.IsNull(codec.Read(candidate.CandidateToken, actor, scoped ? null : Guid.CreateVersion7(), login));
        Assert.IsNull(codec.Read(candidate.CandidateToken, actor, tenant, Guid.CreateVersion7()));
        Assert.IsNull(codec.Read(candidate.CandidateToken + "invalid", actor, tenant, login));
        clock.UtcNow.Returns(envelope.ExpiresAtUtc);
        Assert.IsNull(codec.Read(candidate.CandidateToken, actor, tenant, login));
    }
}
