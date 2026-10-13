using System.Collections;
using Full.NET.Modules.Ai.Knowledge;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>三个知识入口共享选择快照；外部集合行为不能放宽数量、改变来源或触发提前收费。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod]
    [DataRow("paid", false)] [DataRow("paid", true)]
    [DataRow("preflight", false)] [DataRow("preflight", true)]
    [DataRow("retrieve", false)] [DataRow("retrieve", true)]
    public async Task Selection_snapshot_uses_one_count_and_indices_and_retains_original_after_authorization_wait(string entry, bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var id = f.R.F.Binding.KnowledgeBaseId;
        var values = new[] { id }; var selection = new SelectionList(values, 1);
        f.R.BeforeAuthorization = () => values[0] = Guid.Empty;
        var result = await SelectionInvoke(f, entry, selection);
        CollectionAssert.AreEqual(new[] { id }, result.KnowledgeBaseIds.ToArray());
        Assert.AreEqual(Guid.Empty, values[0]); Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(1, selection.IndexReads);
        Assert.AreEqual(0, selection.EnumerationReads); Assert.IsTrue(f.R.Query.SourceReads > 0);
        if (entry == "paid") { Assert.AreEqual(2, f.Budget.Requests.Count); Assert.AreEqual(1, f.Factory.EmbeddingCalls); Assert.AreEqual(1, f.Factory.ChatCalls); }
        else { Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls); }
    }

    [TestMethod]
    [DataRow("paid", false, -1)] [DataRow("paid", true, -1)] [DataRow("preflight", false, -1)] [DataRow("preflight", true, -1)] [DataRow("retrieve", false, -1)] [DataRow("retrieve", true, -1)]
    [DataRow("paid", false, 0)] [DataRow("paid", true, 0)] [DataRow("preflight", false, 0)] [DataRow("preflight", true, 0)] [DataRow("retrieve", false, 0)] [DataRow("retrieve", true, 0)]
    [DataRow("paid", false, 9)] [DataRow("paid", true, 9)] [DataRow("preflight", false, 9)] [DataRow("preflight", true, 9)] [DataRow("retrieve", false, 9)] [DataRow("retrieve", true, 9)]
    [DataRow("paid", false, int.MaxValue)] [DataRow("paid", true, int.MaxValue)] [DataRow("preflight", false, int.MaxValue)] [DataRow("preflight", true, int.MaxValue)] [DataRow("retrieve", false, int.MaxValue)] [DataRow("retrieve", true, int.MaxValue)]
    public async Task Selection_snapshot_rejects_count_before_indices_queries_or_budget(string entry, bool tenant, int count)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var modelReads = 0;
        f.R.Query.BeforeGenerationModel = _ => { modelReads++; return Task.CompletedTask; };
        var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId], count);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SelectionInvoke(f, entry, selection));
        Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(0, selection.IndexReads); Assert.AreEqual(0, modelReads); SelectionNoIo(f, selection);
    }

    [TestMethod]
    [DataRow("paid", false, false)] [DataRow("paid", true, false)] [DataRow("preflight", false, false)] [DataRow("preflight", true, false)] [DataRow("retrieve", false, false)] [DataRow("retrieve", true, false)]
    [DataRow("paid", false, true)] [DataRow("paid", true, true)] [DataRow("preflight", false, true)] [DataRow("preflight", true, true)] [DataRow("retrieve", false, true)] [DataRow("retrieve", true, true)]
    public async Task Selection_snapshot_rejects_empty_or_duplicate_identity_before_io(string entry, bool tenant, bool duplicate)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var modelReads = 0; var id = f.R.F.Binding.KnowledgeBaseId;
        f.R.Query.BeforeGenerationModel = _ => { modelReads++; return Task.CompletedTask; };
        var selection = new SelectionList(duplicate ? [id, id] : [Guid.Empty], duplicate ? 2 : 1);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SelectionInvoke(f, entry, selection));
        Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(duplicate ? 2 : 1, selection.IndexReads); Assert.AreEqual(0, modelReads); SelectionNoIo(f, selection);
    }

    [TestMethod]
    [DataRow("paid", false)] [DataRow("paid", true)] [DataRow("preflight", false)] [DataRow("preflight", true)] [DataRow("retrieve", false)] [DataRow("retrieve", true)]
    public async Task Selection_snapshot_precancel_does_not_touch_external_collection(string entry, bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId], 1);
        await Assert.ThrowsAsync<OperationCanceledException>(() => SelectionInvoke(f, entry, selection, new CancellationToken(true)));
        Assert.AreEqual(0, selection.CountReads); Assert.AreEqual(0, selection.IndexReads); SelectionNoIo(f, selection);
    }

    [TestMethod]
    [DataRow("paid", false)] [DataRow("paid", true)] [DataRow("preflight", false)] [DataRow("preflight", true)] [DataRow("retrieve", false)] [DataRow("retrieve", true)]
    public async Task Selection_snapshot_count_cancellation_stops_before_indices_or_queries(string entry, bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); using var stop = new CancellationTokenSource();
        var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId], 1) { OnCount = stop.Cancel };
        await Assert.ThrowsAsync<OperationCanceledException>(() => SelectionInvoke(f, entry, selection, stop.Token));
        Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(0, selection.IndexReads); SelectionNoIo(f, selection);
    }

    [TestMethod]
    [DataRow("paid", false)] [DataRow("paid", true)] [DataRow("preflight", false)] [DataRow("preflight", true)] [DataRow("retrieve", false)] [DataRow("retrieve", true)]
    public async Task Selection_snapshot_final_index_cancellation_stops_before_queries(string entry, bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); using var stop = new CancellationTokenSource();
        var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId], 1) { OnIndex = stop.Cancel };
        await Assert.ThrowsAsync<OperationCanceledException>(() => SelectionInvoke(f, entry, selection, stop.Token));
        Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(1, selection.IndexReads); SelectionNoIo(f, selection);
    }

    [TestMethod]
    [DataRow("paid", false, false)] [DataRow("paid", true, false)] [DataRow("preflight", false, false)] [DataRow("preflight", true, false)] [DataRow("retrieve", false, false)] [DataRow("retrieve", true, false)]
    [DataRow("paid", false, true)] [DataRow("paid", true, true)] [DataRow("preflight", false, true)] [DataRow("preflight", true, true)] [DataRow("retrieve", false, true)] [DataRow("retrieve", true, true)]
    public async Task Selection_snapshot_preserves_collection_exception_before_io(string entry, bool tenant, bool index)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var marker = new InvalidOperationException("选择器测试故障");
        var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId], 1);
        if (index) selection.OnIndex = () => throw marker; else selection.OnCount = () => throw marker;
        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => SelectionInvoke(f, entry, selection));
        Assert.AreSame(marker, actual); Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(index ? 1 : 0, selection.IndexReads); SelectionNoIo(f, selection);
    }

    [TestMethod]
    [DataRow("paid", false)] [DataRow("paid", true)] [DataRow("preflight", false)] [DataRow("preflight", true)] [DataRow("retrieve", false)] [DataRow("retrieve", true)]
    public async Task Selection_snapshot_allows_eight_then_requires_each_selected_source(string entry, bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant);
        var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId, .. Enumerable.Range(0, 7).Select(_ => Guid.CreateVersion7())], 8);
        // 选择本身合法；缺少任一来源仍由原权威查询整次拒绝，不能只接受可见子集。
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => SelectionInvoke(f, entry, selection));
        Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(8, selection.IndexReads); Assert.AreEqual(0, selection.EnumerationReads);
        Assert.IsTrue(f.R.Query.SourceReads > 0); Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }

    [TestMethod]
    [DataRow("paid", false)] [DataRow("paid", true)] [DataRow("preflight", false)] [DataRow("preflight", true)] [DataRow("retrieve", false)] [DataRow("retrieve", true)]
    public async Task Selection_snapshot_rechecks_transaction_after_external_index_before_queries(string entry, bool tenant)
    {
        using var f = await PaidAnswerFixture.CreateAsync(tenant); var modelReads = 0;
        f.R.Query.BeforeGenerationModel = _ => { modelReads++; return Task.CompletedTask; };
        var selection = new SelectionList([f.R.F.Binding.KnowledgeBaseId], 1)
        { OnIndex = () => { f.R.F.Transactions.HasTransaction.Returns(true); } };
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => SelectionInvoke(f, entry, selection));
            Assert.AreEqual(1, selection.CountReads); Assert.AreEqual(1, selection.IndexReads); Assert.AreEqual(0, modelReads); SelectionNoIo(f, selection);
        }
        finally { f.R.F.Transactions.HasTransaction.Returns(false); }
    }

    private static async Task<AiKnowledgeRetrievalSnapshot> SelectionInvoke(PaidAnswerFixture f, string entry, IReadOnlyList<Guid> ids, CancellationToken token = default)
        => entry == "paid" ? (await f.ExecuteAsync(ids: ids, token: token)).Prepared.Retrieval
            : entry == "preflight" ? await f.R.Service.PreflightAsync(ids, f.R.Actor, f.R.Model, token, f.Generation)
            : await f.R.Service.RetrieveAsync(ids, f.R.Actor, f.R.Model, AiKnowledgeRetrievalRuleTests.Vector(1, 0), token, f.Generation);
    private static void SelectionNoIo(PaidAnswerFixture f, SelectionList selection)
    {
        Assert.AreEqual(0, selection.EnumerationReads); Assert.AreEqual(0, f.R.PermissionCalls); Assert.AreEqual(0, f.R.Query.SourceReads); Assert.AreEqual(0, f.R.Query.VectorReads);
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Budget.Settlements.Count); Assert.AreEqual(0, f.Factory.EmbeddingCalls); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
    /// <summary>首次计数之后故意改变Count并禁止枚举，暴露无界复制及等待后回读原集合。</summary>
    private sealed class SelectionList(Guid[] values, int count) : IReadOnlyList<Guid>
    {
        internal int CountReads, IndexReads, EnumerationReads; internal Action? OnCount, OnIndex;
        public int Count { get { CountReads++; OnCount?.Invoke(); return CountReads == 1 ? count : int.MaxValue; } }
        public Guid this[int index] { get { IndexReads++; OnIndex?.Invoke(); return values[index]; } }
        public IEnumerator<Guid> GetEnumerator() { EnumerationReads++; throw new AssertFailedException("知识选择边界不能调用外部枚举器。"); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
