using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiKnowledgeIndexDocxActivation。</summary>
public sealed partial class AiKnowledgeIndexDocxActivationTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Docx_retrieval_binds_full_manifest_and_returns_logical_citations(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate()); var row = ReadRow(f); f.Denied = true;
        var (service, query, model) = Retrieval(f, row);
        var result = await service.RetrieveAsync([row.KnowledgeBaseId], row.OwnerUserId, model, AiKnowledgeRetrievalRuleTests.Vector(1, 0), default);
        Assert.HasCount(17, result.Chunks); Assert.HasCount(1, result.Sources);
        foreach (var chunk in result.Chunks.Select(c => c.Chunk))
        {
            Assert.IsNull(chunk.PageNumber); Assert.IsNotNull(chunk.DocxLocation);
            Assert.AreEqual(chunk.Sequence + 1, chunk.DocxLocation.ParagraphNumber);
        }
        Assert.AreEqual(1, query.ManifestReads); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow("bytes")] [DataRow("hash")] [DataRow("missing")]
    public async Task Docx_retrieval_refuses_changed_captured_manifest_even_when_later_proof_is_valid(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var row = ReadRow(f);
        var (service, query, model) = Retrieval(f, row);
        query.Manifest = fault == "missing" ? null : f.ManifestRow with
        { Payload = fault == "bytes" ? new byte[8] : f.ManifestRow.Payload, PayloadHash = fault == "hash" ? new string('E', 64) : f.ManifestRow.PayloadHash };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.RetrieveAsync([row.KnowledgeBaseId], row.OwnerUserId, model,
            AiKnowledgeRetrievalRuleTests.Vector(1, 0), default));
    }

    private static (AiKnowledgeRetrievalService, DocxRetrievalQuery, AiModelConfigRecord) Retrieval(Fixture f, AiKnowledgeIndexStatusRecord row)
    {
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var model = new AiModelConfigRecord { Id = f.Source.ModelConfigId, Version = 1, IsEnabled = true,
            ProviderKey = f.Source.ModelProviderKey, ModelId = f.Source.ModelId, EndpointBaseUrl = f.Source.ModelEndpointBaseUrl };
        var query = new DocxRetrievalQuery(f.Query, row, f.Payloads, model) { Manifest = f.ManifestRow };
        var auth = Substitute.For<ICurrentSessionAuthorization>();
        auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        { Assert.IsFalse(f.Transactions.HasTransaction); return new AuthorizedSessionActor(row.OwnerUserId, f.Tenant.Id, Guid.CreateVersion7()); });
        var roles = Substitute.For<IActiveRoleDirectory>(); var orgs = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        roles.ListActiveRoleIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        orgs.ListActiveUnitIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var permissions = new AiKnowledgePermissionGuard(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>());
        return (new(query, f.Transactions, f.Tenant, new(roles, orgs, f.Tenant), permissions, null!, null!,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), f.Store), query, model);
    }

    private sealed class DocxRetrievalQuery(IQueryExecutor inner, AiKnowledgeIndexStatusRecord row, AiKnowledgeIndexVectorRecord[] vectors,
        AiModelConfigRecord model) : IQueryExecutor
    {
        internal AiKnowledgeIndexTextManifestRecord? Manifest;
        internal int ManifestReads;
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(AiModelConfigRecord)) return Task.FromResult((T?)(object)model);
            if (typeof(T) == typeof(AiKnowledgeIndexTextManifestRecord)) { ManifestReads++; return Task.FromResult((T?)(object?)Manifest); }
            return inner.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
            => typeof(T) == typeof(AiKnowledgeIndexStatusRecord) ? Task.FromResult((IReadOnlyList<T>)(object)new[] { row })
                : typeof(T) == typeof(AiKnowledgeIndexVectorRecord) ? Task.FromResult((IReadOnlyList<T>)(object)vectors)
                : inner.QueryAsync<T>(sql, parameters, cancellationToken);
    }
}
