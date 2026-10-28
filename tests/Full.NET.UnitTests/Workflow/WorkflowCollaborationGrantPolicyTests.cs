using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.UnitTests.Workflow;

/// <summary>双边摘要不授予第三租户权限，过期/撤销/旧修订及扩大投影均失败关闭。</summary>
[TestClass]
public sealed class WorkflowCollaborationGrantPolicyTests
{
    [TestMethod]
    public void Each_party_uses_its_actual_scope_and_the_same_fixed_policy_receipt()
    {
        var (grant, now) = Grant();
        Assert.IsTrue(Authorize(grant, now, false)); Assert.IsTrue(Authorize(grant, now, true));
        Assert.IsFalse(WorkflowCollaborationGrantPolicy.TryAuthorize(grant, "host", false, 2, now, out _, out _));
        Assert.IsFalse(WorkflowCollaborationGrantPolicy.TryAuthorize(grant, grant.SourceTenantScopeKey, true, 2, now, out _, out _));
        Assert.IsFalse(WorkflowCollaborationGrantPolicy.IsParticipant(grant, "tenant:" + Guid.CreateVersion7().ToString("N")));
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("proposed")]
    [DataRow("expired")]
    [DataRow("staleRevision")]
    [DataRow("scopeMismatch")]
    [DataRow("sameScope")]
    [DataRow("missingPeer")]
    [DataRow("changedHash")]
    [DataRow("changedActor")]
    [DataRow("malformedPolicy")]
    [DataRow("changedSummary")]
    public void A_stale_or_untrusted_agreement_never_returns_partially_authorized_policy(string defect)
    {
        var (grant, now) = Grant();
        grant = defect switch { "revoked" => grant with { RevokedAtUtc = now, RevokedById = grant.SourceActorId, RevokedByScopeKey = grant.SourceTenantScopeKey },
            "proposed" => grant with { StatusKey = "proposed" }, "expired" => grant with { ExpiresAtUtc = now },
            "staleRevision" => grant with { Revision = 3 }, "scopeMismatch" => grant with { SourceTenantScopeKey = "host" },
            "sameScope" => grant with { TargetTenantId = grant.SourceTenantId, TargetTenantScopeKey = grant.SourceTenantScopeKey },
            "missingPeer" => grant with { TargetActorId = null }, "changedHash" => grant with { PolicyHash = new string('0', 64) },
            "changedActor" => grant with { SourceActorId = Guid.CreateVersion7() }, "changedSummary" => grant with { TargetSummaryJson = "{\"different\":true}" }, _ => grant with { OfferJson = "{" } };
        Assert.IsFalse(WorkflowCollaborationGrantPolicy.TryAuthorize(grant, grant.SourceTenantScopeKey, false, 2, now, out var offer, out var accepted));
        Assert.IsNull(offer); Assert.IsNull(accepted);
    }

    [TestMethod]
    [DataRow("extraProperty")]
    [DataRow("duplicateField")]
    [DataRow("prototype")]
    public void A_source_offer_cannot_hide_open_properties_or_duplicate_field_authority(string defect)
    {
        var (_, now) = Grant();
        var json = Offer(now).Serialize();
        if (defect == "extraProperty") json = json[..^1] + ",\"script\":\"execute\"}";
        else json = json.Replace("[\"amount\"]", defect == "prototype" ? "[\"constructor\"]" : "[\"amount\",\"amount\"]", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json); Assert.IsFalse(WorkflowCollaborationOfferPolicy.TryRead(document.RootElement, out _));
    }

    [TestMethod]
    public void Target_cannot_return_an_unoffered_field_or_use_an_unlisted_starter()
    {
        var (_, now) = Grant(); var offer = Offer(now); var actor = Guid.CreateVersion7();
        var accepted = Acceptance(actor) with { OutputFields = new Dictionary<string, string> { ["decision"] = "secret" } };
        Assert.IsFalse(ReadAcceptance(accepted, offer));
        Assert.IsFalse(ReadAcceptance(Acceptance(actor) with { StarterUserId = Guid.CreateVersion7() }, offer));
        Assert.IsFalse(ReadAcceptance(Acceptance(actor) with { AllowedUserIds = [actor, actor] }, offer));
    }

    [TestMethod]
    public void Target_cannot_expand_the_execution_slice_reserved_by_the_source()
    {
        var (_, now) = Grant(); var offer = Offer(now); var actor = Guid.CreateVersion7();
        Assert.IsFalse(ReadAcceptance(Acceptance(actor) with { MaximumActivities = offer.MaximumTargetActivations + 1 }, offer));
        Assert.IsFalse(ReadAcceptance(Acceptance(actor) with { MaximumActivities = 0 }, offer));
        Assert.IsTrue(ReadAcceptance(Acceptance(actor) with { MaximumActivities = 32 }, offer));
    }

    [TestMethod]
    public void Equivalent_field_order_and_timezone_do_not_change_the_consent_digest()
    {
        var (_, now) = Grant(); var actor = Guid.CreateVersion7(); var source = Guid.CreateVersion7(); var target = Guid.CreateVersion7(); var id = Guid.CreateVersion7();
        var offer = Offer(now) with { InputFieldKeys = ["amount", "title"] }; var accepted = Acceptance(actor);
        Assert.AreEqual(WorkflowCollaborationGrantPolicy.Hash(id, source, target, actor, actor, offer, accepted),
            WorkflowCollaborationGrantPolicy.Hash(id, source, target, actor, actor, offer with { InputFieldKeys = ["title", "amount"], ExpiresAtUtc = offer.ExpiresAtUtc.ToOffset(TimeSpan.FromHours(8)) }, accepted));
    }

    private static string SummaryHash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("{}")));
    private static bool Authorize(WorkflowCollaborationGrantFact grant, DateTimeOffset now, bool target) => WorkflowCollaborationGrantPolicy.TryAuthorize(
        grant, target ? grant.TargetTenantScopeKey : grant.SourceTenantScopeKey, target, 2, now, out _, out _);
    private static bool ReadAcceptance(WorkflowCollaborationAcceptancePolicy value, WorkflowCollaborationOfferPolicy offer) =>
        WorkflowCollaborationAcceptancePolicy.TryRead(JsonSerializer.SerializeToElement(value, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy), offer, out _);
    private static WorkflowCollaborationOfferPolicy Offer(DateTimeOffset now) => new(1, Guid.CreateVersion7(), Guid.CreateVersion7(), "peer.review", ["amount"], ["decision"], 256, WorkflowCollaborationOfferPolicy.NormalizeTime(now.AddDays(7)));
    private static WorkflowCollaborationAcceptancePolicy Acceptance(Guid actor) => new(1, Guid.CreateVersion7(), actor, 256, [actor],
        new Dictionary<string, string> { ["amount"] = "received" }, new Dictionary<string, string> { ["answer"] = "decision" }, SummaryHash);
    private static (WorkflowCollaborationGrantFact Grant, DateTimeOffset Now) Grant()
    {
        var now = DateTimeOffset.UtcNow; var source = Guid.CreateVersion7(); var target = Guid.CreateVersion7(); var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7(); var id = Guid.CreateVersion7();
        var offer = Offer(now); var accepted = Acceptance(second);
        var grant = new WorkflowCollaborationGrantFact(id, source, target, WorkflowCollaborationGrantPolicy.ScopeKey(source), WorkflowCollaborationGrantPolicy.ScopeKey(target),
            2, "active", first, second, offer.Serialize(), JsonSerializer.Serialize(accepted, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy),
            WorkflowCollaborationGrantPolicy.Hash(id, source, target, first, second, offer, accepted), offer.ExpiresAtUtc, now.AddMinutes(-2), now.AddMinutes(-1), null, null, null, offer.SourceDefinitionId, offer.SourceFormVersionId, accepted.TargetDefinitionVersionId, "{}", SummaryHash);
        return (grant, now);
    }
}

internal static class WorkflowCollaborationOfferTestJson
{
    internal static string Serialize(this WorkflowCollaborationOfferPolicy value) => JsonSerializer.Serialize(value, WorkflowJsonSerializerContext.Default.WorkflowCollaborationOfferPolicy);
}
