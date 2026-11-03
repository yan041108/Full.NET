using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.SearchKnowledge;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实关系选择继续通过完整发布、读取策略、可信作用域与交付复核。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Relations_read_no_relationship_without_dispatch_or_diagnostic_permissions(bool scoped)
    {
        var r = await RetrievalFixture.Create(scoped); r.BeforePermission = permission =>
        { Assert.AreNotEqual(AiChatPermissions.Send, permission); Assert.AreNotEqual(AiKnowledgeSearchPermissions.Compare, permission); Assert.AreNotEqual(AiKnowledgeSearchPermissions.Diagnostics, permission); };
        var result = await r.Service.RetrieveRelationsAsync(ContextSource(r.Query.Rows.Single()), r.Actor, r.Model, 0, default);
        Assert.AreEqual("none", result.Relations!.State); Assert.AreEqual(1, result.Relations.Blocks.Count);
        Assert.AreEqual(17, result.CandidateCount); Assert.AreEqual(1, r.Query.VectorReads);
    }
    [TestMethod] [DataRow(false, "source")] [DataRow(true, "source")] [DataRow(false, "read")] [DataRow(true, "read")]
    [DataRow(false, "policy")] [DataRow(true, "policy")]
    public async Task Relations_admission_refuses_before_any_vector_or_body_read(bool scoped, string fault)
    {
        var policy = fault == "policy" ? new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["context"] = [] }) : null;
        var r = await RetrievalFixture.Create(scoped, policy); var before = r.F.PayloadReads; var source = ContextSource(r.Query.Rows.Single());
        if (fault == "source") source = source with { PublicationId = Guid.CreateVersion7() };
        if (fault == "read") r.DeniedPermission = AiKnowledgeSearchPermissions.Read;
        var service = new AiKnowledgeSearchRelationsService(r.Query, r.F.Tenant, r.F.Transactions, r.Permissions, r.Service);
        var result = await service.ReadAsync(new(source, r.Model.Id, r.Model.Version, 0), r.Actor, default);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Value); Assert.AreEqual(0, r.Query.VectorReads); Assert.AreEqual(before, r.F.PayloadReads);
        Assert.AreEqual(fault == "read" ? "authorization.permission_denied" : fault == "source" ? AiKnowledgeErrorCodes.SearchSourceChanged : AiKnowledgeErrorCodes.SearchUnavailable, result.Error!.Code);
    }
    [TestMethod] [DataRow("permission")] [DataRow("source")] [DataRow("tenant")]
    public async Task Relations_late_changes_never_release_buffered_provenance(string fault)
    {
        var r = await RetrievalFixture.Create(false); var source = ContextSource(r.Query.Rows.Single());
        r.Query.BeforeVectors = _ => { if (fault == "permission") r.Denied = true;
            else if (fault == "source") r.Query.Rows = [r.Query.Rows[0] with { PublicationId = Guid.CreateVersion7() }];
            else r.F.Tenant.SetTenant(new(Guid.CreateVersion7(), "new", "另一租户")); return Task.CompletedTask; };
        await Assert.ThrowsAsync<Exception>(() => r.Service.RetrieveRelationsAsync(source, r.Actor, r.Model, 0, default));
    }
    [TestMethod] [DataRow(-1)] [DataRow(256)]
    public async Task Relations_invalid_anchor_is_a_public_validation_error_before_source_reads(int anchor)
    {
        var r = await RetrievalFixture.Create(false); var service = new AiKnowledgeSearchRelationsService(r.Query, r.F.Tenant, r.F.Transactions, r.Permissions, r.Service);
        var result = await service.ReadAsync(new(ContextSource(r.Query.Rows.Single()), r.Model.Id, r.Model.Version, anchor), r.Actor, default);
        Assert.AreEqual(AiKnowledgeErrorCodes.SearchInputInvalid, result.Error!.Code); Assert.AreEqual(0, r.Query.VectorReads); Assert.AreEqual(0, r.Query.SourceReads);
    }
}
