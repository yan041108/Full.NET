using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.TestEmbeddings;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>已知官方词表的拒绝必须先于预算与模型创建，不改写已有指纹。</summary>
[TestClass]
public sealed class AiEmbeddingInputLimitTests
{
    internal static AiModelConfigRecord Model(string name, string endpoint = "https://api.openai.com/v1") =>
        new() { Id = Guid.CreateVersion7(), Version = 1, IsEnabled = true, ProviderKey = "openai_compatible", ModelId = name, EndpointBaseUrl = endpoint };
    private static readonly Lazy<char> ThreeTokenGlyph = new(() =>
    {
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(default);
        // 从固定词表实测选择有效 BMP 汉字，避免把字符/UTF-8 字节数误当 Token。
        for (var value = 0x3400; value < 0x3600; value++)
        {
            var glyph = (char)value;
            if (tokenizer.CountTokens(glyph.ToString()) == 3 && tokenizer.CountTokens(new string(glyph, 3000)) == 9000) return glyph;
        }
        throw new AssertFailedException("固定词表未找到预期测试字形。");
    });
    internal static string Excess() => new(ThreeTokenGlyph.Value, 3000);

    [TestMethod]
    [DataRow("text-embedding-3-small", 8190)] [DataRow("text-embedding-3-small", 8191)] [DataRow("text-embedding-3-small", 8192)]
    [DataRow("text-embedding-3-large", 8190)] [DataRow("text-embedding-3-large", 8191)] [DataRow("text-embedding-3-large", 8192)]
    public void Embedding_limit_exact_boundary_uses_fixed_tokenizer(string name, int count)
    {
        var text = new string(ThreeTokenGlyph.Value, count / 3) + (count % 3 == 0 ? "" : count % 3 == 1 ? " a" : " a b");
        Assert.AreEqual(count, AiKnowledgeCl100kTokenizer.Get(default).CountTokens(text, considerNormalization: false));
        Assert.AreEqual(count <= 8191, AiKnowledgeEmbeddingInputPolicy.Validate(Model(name), [text], default) is null);
    }

    [TestMethod]
    [DataRow("https://API.OPENAI.COM/v1")] [DataRow("https://api.openai.com:443/v1/")]
    [DataRow("https://api.openai.com/v1/")] [DataRow("https://api.openai.com:443/v1")]
    public void Embedding_limit_canonical_official_origins_cannot_bypass(string endpoint)
    {
        var model = Model("text-embedding-3-small", endpoint);
        Assert.IsTrue(AiKnowledgeEmbeddingInputPolicy.Supports(model));
        Assert.IsNotNull(AiKnowledgeEmbeddingInputPolicy.Validate(model, [Excess()], default));
    }

    [TestMethod]
    [DataRow("https://gateway.example/v1", "openai_compatible", "text-embedding-3-small")]
    [DataRow("https://api.openai.com/v1", "ollama", "text-embedding-3-small")]
    [DataRow("https://api.openai.com/v1", "openai_compatible", "text-embedding-ada-002")]
    [DataRow("https://api.openai.com/%76%31", "openai_compatible", "text-embedding-3-small")]
    [DataRow("https://api.openai.com.evil/v1", "openai_compatible", "text-embedding-3-small")]
    [DataRow("https://api.openai.com/v1?gateway=1", "openai_compatible", "text-embedding-3-small")]
    public void Embedding_limit_unattested_gateway_does_not_claim_exact_token_support(string endpoint, string provider, string name)
    {
        var model = new AiModelConfigRecord { Id = Guid.CreateVersion7(), Version = 1, ProviderKey = provider, ModelId = name, EndpointBaseUrl = endpoint };
        Assert.IsFalse(AiKnowledgeEmbeddingInputPolicy.Supports(model));
        Assert.IsNull(AiKnowledgeEmbeddingInputPolicy.Validate(model, [Excess()], default));
    }

    [TestMethod] [DataRow("https://api.openai.com/v1")] [DataRow("https://gateway.example/v1")]
    public void Embedding_limit_cancellation_precedes_tokenizer_or_compatibility_path(string endpoint)
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeEmbeddingInputPolicy.Validate(Model("text-embedding-3-small", endpoint), ["文本"], stop.Token));
    }

    [TestMethod] [DataRow("text-embedding-3-small")] [DataRow("text-embedding-3-large")]
    public void Embedding_limit_preserves_historical_request_hash_and_byte_reservation(string name)
    {
        var model = Model(name); string[] inputs = ["hello", "世界"];
        Assert.IsNull(AiKnowledgeEmbeddingInputPolicy.Validate(model, inputs, default));
        var request = Full.NET.Modules.Ai.Budgets.AiEmbeddingBudgetRequest.Create(Guid.CreateVersion7(), model, inputs);
        Assert.AreEqual(75L, request.InputTokenLimit);
        Assert.AreEqual("A4F3D71202FF7C196885499BDB0590EF66F258FFB34D9718967945D53673736F", request.RequestHash);
        CollectionAssert.AreEqual(new[] { "hello", "世界" }, inputs);
    }

    [TestMethod]
    [DataRow("text-embedding-3-small", "too_many")] [DataRow("text-embedding-3-large", "too_many")]
    [DataRow("text-embedding-3-small", "invalid_utf16")] [DataRow("text-embedding-3-large", "invalid_utf16")]
    [DataRow("text-embedding-3-small", "good")] [DataRow("text-embedding-3-large", "good")]
    public async Task Embedding_limit_test_endpoint_refuses_before_budget_and_factory(string name, string fault)
    {
        var model = Model(name); var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(model);
        var budget = Substitute.For<IAiOperationBudgetStore>();
        budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            new AiOperationReservation(call.Arg<AiOperationRequest>()!.OperationId, true, "reserved", 100, null, null));
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var factory = Substitute.For<IAiModelClientFactory>(); factory.ProviderKey.Returns("openai_compatible");
        var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
        generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratedEmbeddings<Embedding<float>>([new(new float[] { 1, 0 })]) { Usage = new() { InputTokenCount = 1 } });
        factory.CreateEmbeddingGeneratorAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(generator));
        using var bindings = new AiModelBindingScope();
        var text = fault == "too_many" ? Excess() : fault == "invalid_utf16" ? "\ud800" : "正常文本";
        if (fault == "too_many") Assert.IsTrue(AiKnowledgeCl100kTokenizer.Get(default).CountTokens(text) > 8191);
        var result = await new AiEmbeddingTestService(query, [factory], bindings, budget, ids)
            .TestAsync(model.Id, new(text, null));
        if (fault == "good")
        {
            Assert.IsTrue(result.IsSuccess);
            await budget.Received(1).ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
            await generator.Received(1).GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>());
        }
        else
        {
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(0, budget.ReceivedCalls().Count()); Assert.AreEqual(0, ids.ReceivedCalls().Count());
            await factory.DidNotReceive().CreateEmbeddingGeneratorAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>());
        }
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Embedding_limit_index_admission_refuses_before_batch_identity_or_reservation(bool scoped)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var budget = Substitute.For<IAiOperationBudgetStore>(); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var tenant = new CurrentTenantAccessor(); if (scoped) tenant.SetTenant(new(Guid.CreateVersion7(), "test", "测试")); else tenant.SetHost();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var binding = new AiKnowledgeIndexTaskBinding(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        var model = Model("text-embedding-3-small"); var taskId = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new AiKnowledgeIndexDispatchRecord { TaskId = taskId, DocumentId = binding.DocumentId, VersionId = binding.VersionId,
                DocumentVersion = 1, KnowledgeBaseVersion = 1,
                DataClassification = "internal", ModelProviderKey = model.ProviderKey, ModelId = model.ModelId,
                ModelEndpointBaseUrl = model.EndpointBaseUrl, ModelOrganizationId = model.OrganizationId,
                ModelConfigId = model.Id, ModelConfigVersion = 1, LeaseToken = lease, LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) });
        query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(model);
        var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
        var service = new AiKnowledgeIndexBudgetService(query, tasks, new(query, command, tasks, ids), budget);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.ReserveAsync(binding, taskId, lease, 0, [Excess()], default));
        Assert.AreEqual(0, budget.ReceivedCalls().Count()); Assert.AreEqual(0, command.ReceivedCalls().Count()); Assert.AreEqual(0, ids.ReceivedCalls().Count());
    }
}

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Embedding_limit_paid_query_refuses_before_either_operation(bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var old = f.R.Model;
        f.R.Model = new() { Id = old.Id, Version = old.Version, TenantId = old.TenantId, IsEnabled = true,
            ProviderKey = old.ProviderKey, ModelId = old.ModelId, EndpointBaseUrl = "https://api.openai.com/v1" };
        f.R.Query.Model = f.R.Model;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync(AiEmbeddingInputLimitTests.Excess()));
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Budget.Settlements.Count);
        Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
}
