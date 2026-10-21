using Full.NET.Modules.Ai.Persistence;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiKnowledgeIndexPdfActivation。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Pdf_status_uses_current_reader_and_rechecks_permission_after_proof(bool tenant, bool revoke)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate()); f.ReadRow = ReadRow(f); f.Denied = true;
        var reader = Guid.CreateVersion7(); var denied = false; var reads = 0; var permissions = new List<string>();
        var auth = Substitute.For<ICurrentSessionAuthorization>(); auth.UsePermissionSetChecks();
        auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsFalse(f.Transactions.HasTransaction); var permission = call.ArgAt<string>(0); permissions.Add(permission);
            return denied ? null : new AuthorizedSessionActor(reader, f.Tenant.Id, Guid.CreateVersion7());
        });
        var roles = Substitute.For<IActiveRoleDirectory>(); var orgs = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        roles.ListActiveRoleIdsForUserAsync(reader, Arg.Any<CancellationToken>()).Returns(_ =>
        { Assert.IsFalse(f.Transactions.HasTransaction); return Array.Empty<Guid>(); });
        orgs.ListActiveUnitIdsForUserAsync(reader, Arg.Any<CancellationToken>()).Returns(_ =>
        { Assert.IsFalse(f.Transactions.HasTransaction); return Array.Empty<Guid>(); });
        f.AtRead = () => { reads++; if (revoke && f.Transactions.HasTransaction) denied = true; };
        var service = new AiKnowledgeIndexStatusService(f.Query, f.Transactions, f.Tenant, new(roles, orgs, f.Tenant),
            new(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>()), null!,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), f.Store);
        var before = f.Authorizations;
        var result = await service.ReadAsync(f.Binding.KnowledgeBaseId, f.Binding.DocumentId, f.Binding.VersionId, reader, default);
        Assert.AreEqual(before, f.Authorizations); Assert.IsTrue(reads >= 3);
        CollectionAssert.AreEquivalent(AiKnowledgeIndexStatusService.Permissions, permissions.Distinct().ToArray());
        if (revoke) { Assert.IsFalse(result.IsSuccess); Assert.AreEqual("authorization.permission_denied", result.Error!.Code); }
        else { Assert.IsTrue(result.IsSuccess); Assert.AreEqual("ready", result.Value!.State); }
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Pdf_reader_verifies_completed_generation_without_original_session_liveness(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate());
        var row = ReadRow(f); f.ReadRow = row; f.Denied = true;
        var authorizations = f.Authorizations;
        Assert.IsTrue(await f.Store.IsReadyForReadAsync(row, new(), default));
        Assert.AreEqual(authorizations, f.Authorizations);
    }
    [TestMethod] [DataRow("visibility")] [DataRow("payload")] [DataRow("manifest")]
    public async Task Pdf_reader_refuses_current_visibility_or_actual_evidence_loss(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var row = ReadRow(f); f.ReadRow = row;
        if (fault == "visibility") f.ReadRow = null;
        if (fault == "payload") f.Payloads[0].Payload[0] ^= 1;
        if (fault == "manifest") f.ManifestRow = f.ManifestRow with { Payload = new byte[8] };
        Assert.IsFalse(await f.Store.IsReadyForReadAsync(row, new(), default));
    }
    [TestMethod]
    public async Task Pdf_reader_rechecks_visibility_after_evidence_wait()
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var row = ReadRow(f); f.ReadRow = row;
        f.AtRead = () => f.ReadRow = row with { KnowledgeBaseVersion = 2 };
        Assert.IsFalse(await f.Store.IsReadyForReadAsync(row, new(), default));
    }
    private static AiKnowledgeIndexStatusRecord ReadRow(Fixture f) => new()
    {
        KnowledgeBaseId = f.Binding.KnowledgeBaseId, DocumentId = f.Binding.DocumentId, VersionId = f.Binding.VersionId,
        OwnerUserId = f.Binding.OwnerUserId, DocumentVersion = 1, KnowledgeBaseVersion = 1,
        DataClassification = "internal",
        TaskId = f.TaskId, ActiveTaskId = f.TaskId, ActiveLeaseToken = f.Lease, StateKey = "completed", IsCurrent = 1,
        FileId = f.Source.FileId, ContentHash = f.Source.ContentHash, ContentType = f.Source.ContentType, SizeBytes = f.Source.SizeBytes,
        ModelConfigId = f.Source.ModelConfigId, ModelConfigVersion = f.Source.ModelConfigVersion,
        ProviderKey = f.Source.ModelProviderKey, ModelId = f.Source.ModelId,
        ActiveManifestHash = f.Pointer!.ManifestHash, ActivePlanHash = f.Pointer.PlanHash, ActiveReceiptHash = f.Pointer.ReceiptHash
    };
}
