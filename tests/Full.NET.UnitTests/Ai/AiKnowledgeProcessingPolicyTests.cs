using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

// 知识库处理策略：未知分类或未批准的生成配置须在扣费与模型调用前拒绝。
/// <summary>单元测试分片：AiKnowledgeIndexPdfActivation。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod]
    [DataRow(false, "missing")]
    [DataRow(true, "missing")]
    [DataRow(false, "id")]
    [DataRow(true, "id")]
    [DataRow(false, "version")]
    [DataRow(true, "version")]
    [DataRow(false, "empty")]
    [DataRow(true, "empty")]
    [DataRow(false, "id_only")]
    [DataRow(true, "id_only")]
    [DataRow(false, "version_only")]
    [DataRow(true, "version_only")]
    public async Task Processing_policy_rejects_unapproved_generation_before_any_charge(bool tenant, string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        var row = f.R.Query.Rows[0] with
        {
            DataClassification = "internal",
            GenerationModelConfigId = fault is "missing" or "version_only" ? null : fault == "empty" ? Guid.Empty
                : fault == "id" ? Guid.CreateVersion7() : f.Generation.Id,
            GenerationModelVersion = fault is "missing" or "id_only" ? null : fault == "version" ? f.Generation.Version + 1 : f.Generation.Version,
        };
        f.R.Query.Rows = [row]; f.R.F.ReadRow = row;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.AreEqual(0, f.Budget.Requests.Count);
        Assert.AreEqual(0, f.Factory.EmbeddingCalls);
        Assert.AreEqual(0, f.Factory.ChatCalls);
    }

    [TestMethod]
    [DataRow(false, "")]
    [DataRow(true, "")]
    [DataRow(false, "unknown")]
    [DataRow(true, "unknown")]
    [DataRow(false, "INTERNAL")]
    [DataRow(true, "internal ")]
    public async Task Processing_policy_rejects_unknown_classification_before_any_charge(bool tenant, string classification)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        var row = f.R.Query.Rows[0] with { DataClassification = classification,
            GenerationModelConfigId = f.Generation.Id, GenerationModelVersion = f.Generation.Version };
        f.R.Query.Rows = [row]; f.R.F.ReadRow = row;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.AreEqual(0, f.Budget.Requests.Count);
        Assert.AreEqual(0, f.Factory.EmbeddingCalls);
        Assert.AreEqual(0, f.Factory.ChatCalls);
    }

    [TestMethod]
    [DataRow(false, "public")]
    [DataRow(true, "public")]
    [DataRow(false, "internal")]
    [DataRow(true, "internal")]
    [DataRow(false, "restricted")]
    [DataRow(true, "restricted")]
    public async Task Processing_policy_accepts_known_classification_with_exact_approval(bool tenant, string classification)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        var row = f.R.Query.Rows[0] with { DataClassification = classification };
        f.R.Query.Rows = [row]; f.R.F.ReadRow = row;
        var result = await f.ExecuteAsync();
        Assert.IsNotNull(result.Answer);
        Assert.AreEqual(2, f.Budget.Requests.Count);
        Assert.AreEqual(1, f.Factory.EmbeddingCalls);
        Assert.AreEqual(1, f.Factory.ChatCalls);
    }

    [TestMethod]
    [DataRow(false, "embedding_create", false)]
    [DataRow(true, "embedding_create", true)]
    [DataRow(false, "embedding_return", false)]
    [DataRow(true, "embedding_return", true)]
    [DataRow(false, "embedding_settle", false)]
    [DataRow(true, "embedding_settle", true)]
    [DataRow(false, "chat_create", false)]
    [DataRow(true, "chat_create", true)]
    [DataRow(false, "chat_return", false)]
    [DataRow(true, "chat_return", true)]
    [DataRow(false, "chat_settle", false)]
    [DataRow(true, "chat_settle", true)]
    public async Task Processing_policy_drift_during_wait_never_delivers_body(bool tenant, string stage, bool revokeApproval)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        var drifted = false;
        void Drift()
        {
            drifted = true;
            var row = f.R.Query.Rows[0];
            f.R.Query.Rows = [revokeApproval ? row with { GenerationModelConfigId = null, GenerationModelVersion = null }
                : row with { DataClassification = row.DataClassification == "internal" ? "restricted" : "internal" }];
        }
        f.Factory.BeforeCreate = kind => { if (stage == kind + "_create") Drift(); };
        f.Factory.BeforeReturn = kind => { if (stage == kind + "_return") Drift(); };
        f.Budget.BeforeSettle = request => { if (stage == request.Kind + "_settle") Drift(); };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.ExecuteAsync());
        Assert.IsTrue(drifted, "必须实际到达指定等待窗口，不能将前置夹具拒绝计为漂移验证。");
        Assert.AreEqual(stage.StartsWith("embedding", StringComparison.Ordinal) ? 1 : 2, f.Budget.Requests.Count);
        if (stage.StartsWith("embedding", StringComparison.Ordinal)) Assert.AreEqual(0, f.Factory.ChatCalls);
        if (stage == "embedding_create") Assert.AreEqual(0, f.Factory.EmbeddingCalls);
        if (stage == "chat_create") Assert.AreEqual(0, f.Factory.ChatCalls);
        Assert.AreEqual(f.Budget.Requests.Count, f.Budget.Settlements.Count);
    }
}
