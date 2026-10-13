using System.Collections;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.TestEmbeddings;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>管理测试的外部集合、派发和结算边界；未知消费不能补零或覆盖旧预留。</summary>
[TestClass]
public sealed class AiEmbeddingTestBoundaryTests
{
    [TestMethod] [DataRow(-1)] [DataRow(17)] [DataRow(int.MaxValue)]
    public async Task Embedding_boundary_count_rejects_before_indices_or_query(int count)
    {
        using var f = new Fixture(); var input = new InputList(["text"], count);
        var result = await f.Service.TestAsync(f.Model.Id, new("fallback", input));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Validation, result.Error!.Type);
        Assert.AreEqual(1, input.CountReads); Assert.AreEqual(0, input.IndexReads); f.AssertNoIo();
    }

    [TestMethod] [DataRow("null")] [DataRow("empty")] [DataRow("space")] [DataRow("long")] [DataRow("total")]
    public async Task Embedding_boundary_content_rejects_without_query_or_trim_allocation(string fault)
    {
        using var f = new Fixture(); string[] values = fault switch
        {
            "null" => [null!], "empty" => [""], "space" => ["  "], "long" => [new string(' ', 8000) + "x"],
            _ => Enumerable.Repeat(new string('x', 8000), 5).ToArray()
        };
        var result = await f.Service.TestAsync(f.Model.Id, new(null, new InputList(values, values.Length)));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.Validation, result.Error!.Type); f.AssertNoIo();
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Embedding_boundary_precancel_does_not_read_input_or_query(bool batch)
    {
        using var f = new Fixture(); var input = new InputList(["text"], 1);
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id,
            new("text", batch ? input : null), new CancellationToken(true)));
        Assert.AreEqual(0, input.CountReads); Assert.AreEqual(0, input.IndexReads); f.AssertNoIo();
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Embedding_boundary_collection_cancellation_stops_before_query(bool index)
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource(); var input = new InputList(["text"], 1);
        if (index) input.OnIndex = stop.Cancel; else input.OnCount = stop.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, new(null, input), stop.Token));
        Assert.AreEqual(index ? 1 : 0, input.IndexReads); f.AssertNoIo();
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Embedding_boundary_collection_exception_propagates_before_query(bool index)
    {
        using var f = new Fixture(); var marker = new IOException("集合读取故障"); var input = new InputList(["text"], 1);
        if (index) input.OnIndex = () => throw marker; else input.OnCount = () => throw marker;
        Assert.AreSame(marker, await Assert.ThrowsExactlyAsync<IOException>(() => f.Service.TestAsync(f.Model.Id, new(null, input))));
        f.AssertNoIo();
    }

    [TestMethod] [DataRow("single")] [DataRow("batch")] [DataRow("fallback")]
    public async Task Embedding_boundary_freezes_once_and_preserves_trim_and_batch_priority(string mode)
    {
        using var f = new Fixture(); var input = new InputList([" first ", " second "], mode == "fallback" ? 0 : 2);
        var result = await f.Service.TestAsync(f.Model.Id, new(" single ", mode == "single" ? null : input));
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(mode == "batch" ? new[] { "first", "second" } : new[] { "single" }, f.Sent!);
        Assert.AreEqual(mode == "single" ? 0 : 1, input.CountReads); Assert.AreEqual(mode == "batch" ? 2 : 0, input.IndexReads);
        Assert.AreEqual(0, input.EnumerationReads); Assert.AreEqual(7L, f.Settlements.Single().Usage.InputTokens);
    }

    [TestMethod]
    public async Task Embedding_boundary_query_wait_cannot_replace_frozen_text()
    {
        using var f = new Fixture(); string[] values = [" original "]; f.OnQuery = () => values[0] = "replacement";
        Assert.IsTrue((await f.Service.TestAsync(f.Model.Id, new(null, new InputList(values, 1)))).IsSuccess);
        CollectionAssert.AreEqual(new[] { "original" }, f.Sent!);
        Assert.AreEqual(Full.NET.Modules.Ai.Budgets.AiEmbeddingBudgetRequest.Create(f.Requests.Single().OperationId, f.Model, ["original"]).RequestHash,
            f.Requests.Single().RequestHash);
    }

    [TestMethod] [DataRow("uri")] [DataRow("disposed")]
    public async Task Embedding_boundary_binding_failure_settles_owned_reservation_as_known_zero(string fault)
    {
        using var f = new Fixture(fault == "uri" ? "invalid uri" : "https://gateway.test/v1"); if (fault == "disposed") f.Bindings.Dispose();
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess);
        Assert.AreEqual(0, f.Creates); f.AssertSettlement(0, 0, "failed");
    }

    [TestMethod] [DataRow("unsupported")] [DataRow("failure")] [DataRow("cancelled")]
    public async Task Embedding_boundary_factory_failure_has_no_dispatch_and_known_zero(string fault)
    {
        using var f = new Fixture(); f.CreateFault = fault == "unsupported" ? new NotSupportedException()
            : fault == "failure" ? new IOException() : new OperationCanceledException();
        if (fault == "cancelled") await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, new("text", null)));
        else Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess);
        Assert.AreEqual(0, f.Dispatches); f.AssertSettlement(0, 0, fault == "cancelled" ? "cancelled" : "failed");
    }

    [TestMethod]
    public async Task Embedding_boundary_cancel_after_reserve_prevents_factory_and_settles_zero()
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource(); f.OnReserve = stop.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, new("text", null), stop.Token));
        Assert.AreEqual(0, f.Creates); f.AssertSettlement(0, 0, "cancelled");
    }

    [TestMethod] [DataRow("duplicate")] [DataRow("mismatch")]
    public async Task Embedding_boundary_foreign_receipt_never_transfers_dispatch_or_settlement_ownership(string fault)
    {
        using var f = new Fixture(); f.ReservationFault = fault;
        var result = await f.Service.TestAsync(f.Model.Id, new("text", null));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual("ai.budget.operation_conflict", result.Error!.Code);
        Assert.AreEqual(0, f.Creates); Assert.AreEqual(0, f.Settlements.Count);
    }

    [TestMethod]
    public async Task Embedding_boundary_settlement_failure_propagates_even_after_success()
    {
        using var f = new Fixture(); var marker = new IOException("结算没有确认"); f.SettleFault = marker;
        Assert.AreSame(marker, await Assert.ThrowsExactlyAsync<IOException>(() => f.Service.TestAsync(f.Model.Id, new("text", null))));
        Assert.AreEqual(1, f.Settlements.Count);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Embedding_boundary_started_dispatch_without_receipt_stays_unknown(bool cancelled)
    {
        using var f = new Fixture(); f.DispatchFault = cancelled ? new OperationCanceledException() : new IOException();
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, new("text", null)));
        else Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess);
        f.AssertSettlement(null, null, cancelled ? "cancelled" : "failed");
    }

    [TestMethod] [DataRow("empty")] [DataRow("count")] [DataRow("dimensions")] [DataRow("zero")]
    [DataRow("nan")] [DataRow("infinity")]
    public async Task Embedding_boundary_malformed_vectors_preserve_reported_usage(string fault)
    {
        using var f = new Fixture(); float[][] vectors = fault switch
        {
            "empty" => [], "count" => [[1], [1]], "dimensions" => [[1], [1, 0]], "zero" => [[]],
            "nan" => [[float.NaN]], _ => [[float.PositiveInfinity]]
        };
        f.Returned = new(vectors.Select(v => new Embedding<float>(v))) { Usage = new() { InputTokenCount = 7 } };
        var request = fault == "dimensions" ? new TestAiModelEmbeddingRequest(null, ["first", "second"]) : new("text", null);
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, request)).IsSuccess); f.AssertSettlement(7, 0, "failed");
    }

    [TestMethod] [DataRow(-1L)] [DataRow(1_000_000_001L)] [DataRow(2_147_483_648L)] [DataRow(long.MaxValue)]
    public async Task Embedding_boundary_invalid_report_never_wraps_or_becomes_known(long tokens)
    {
        using var f = new Fixture(); f.Returned = new([new(new float[] { 1 })]) { Usage = new() { InputTokenCount = tokens } };
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess); f.AssertSettlement(null, null, "failed");
    }

    [TestMethod]
    public async Task Embedding_boundary_valid_report_over_reservation_is_recorded_and_rejected()
    {
        using var f = new Fixture(); f.Returned = new([new(new float[] { 1 })]) { Usage = new() { InputTokenCount = 100 } };
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess); f.AssertSettlement(100, 0, "failed");
    }

    [TestMethod]
    public async Task Embedding_boundary_nonzero_output_is_recorded_and_rejected()
    {
        using var f = new Fixture(); f.Returned = new([new(new float[] { 1 })]) { Usage = new() { InputTokenCount = 7, OutputTokenCount = 1 } };
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess); f.AssertSettlement(7, 1, "failed");
    }

    [TestMethod]
    public async Task Embedding_boundary_missing_usage_returns_dimensions_but_retains_unknown()
    {
        using var f = new Fixture(); f.Returned = new([new(new float[] { 1 })]);
        Assert.IsTrue((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess); f.AssertSettlement(null, 0, "succeeded");
    }

    [TestMethod]
    public async Task Embedding_boundary_dispose_failure_preserves_report_and_failed_outcome()
    {
        using var f = new Fixture(); f.DisposeFault = new IOException();
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess); f.AssertSettlement(7, 0, "failed");
    }

    [TestMethod]
    public async Task Embedding_boundary_success_uses_one_owned_receipt_and_original_fingerprint()
    {
        using var f = new Fixture(); Assert.IsTrue((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess);
        Assert.AreEqual(1, f.Requests.Count); Assert.AreEqual(1, f.Dispatches); f.AssertSettlement(7, 0, "succeeded");
        Assert.AreEqual(36L, f.Requests.Single().InputTokenLimit);
    }

    [TestMethod] [DataRow("factory")] [DataRow("generation")] [DataRow("dispose")]
    public async Task Embedding_boundary_reported_cancellation_does_not_return_success_or_drop_usage(string stage)
    {
        using var f = new Fixture(); using var stop = new CancellationTokenSource();
        if (stage == "factory") f.OnCreate = stop.Cancel; else if (stage == "generation") f.OnGenerate = stop.Cancel; else f.OnDispose = stop.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.TestAsync(f.Model.Id, new("text", null), stop.Token));
        Assert.AreEqual(stage == "factory" ? 0 : 1, f.Dispatches); f.AssertSettlement(stage == "factory" ? 0 : 7, 0, "cancelled");
    }

    [TestMethod]
    public async Task Embedding_boundary_null_factory_result_cannot_mark_dispatch_started()
    {
        using var f = new Fixture(); f.NullGenerator = true;
        Assert.IsFalse((await f.Service.TestAsync(f.Model.Id, new("text", null))).IsSuccess);
        Assert.AreEqual(0, f.Dispatches); f.AssertSettlement(0, 0, "failed");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly AiModelConfigRecord Model;
        internal readonly AiModelBindingScope Bindings = new();
        internal readonly List<AiOperationRequest> Requests = [];
        internal readonly List<(Guid Id, AiOperationUsage Usage, string Outcome, CancellationToken Token)> Settlements = [];
        internal readonly AiEmbeddingTestService Service;
        internal int Queries, Creates, Dispatches;
        internal Action? OnQuery, OnReserve, OnCreate, OnGenerate, OnDispose;
        internal bool NullGenerator;
        internal Exception? CreateFault, DispatchFault, DisposeFault, SettleFault;
        internal string? ReservationFault;
        internal string[]? Sent;
        internal GeneratedEmbeddings<Embedding<float>>? Returned;
        internal Fixture(string endpoint = "https://gateway.test/v1")
        {
            Model = AiEmbeddingInputLimitTests.Model("test", endpoint);
            var query = Substitute.For<IQueryExecutor>();
            query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { Queries++; OnQuery?.Invoke(); return Task.FromResult<AiModelConfigRecord?>(Model); });
            var budget = Substitute.For<IAiOperationBudgetStore>();
            budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<AiOperationRequest>()!; Requests.Add(request); OnReserve?.Invoke();
                return new AiOperationReservation(ReservationFault == "mismatch" ? Guid.CreateVersion7() : request.OperationId,
                    ReservationFault != "duplicate", "reserved", request.InputTokenLimit, null, null);
            });
            budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Settlements.Add((call.Arg<Guid>(), call.Arg<AiOperationUsage>()!, call.Arg<string>()!, call.Arg<CancellationToken>()));
                if (SettleFault is { } error) throw error; return Task.CompletedTask;
            });
            var generator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
            generator.GenerateAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Dispatches++; if (DispatchFault is { } error) throw error;
                Sent = call.Arg<IEnumerable<string>>()!.ToArray();
                OnGenerate?.Invoke();
                return Task.FromResult(Returned ?? new GeneratedEmbeddings<Embedding<float>>(Sent.Select(_ => new Embedding<float>(new float[] { 1 })))
                    { Usage = new() { InputTokenCount = 7 } });
            });
            generator.When(value => value.Dispose()).Do(_ => { OnDispose?.Invoke(); if (DisposeFault is { } error) throw error; });
            var factory = Substitute.For<IAiModelClientFactory>(); factory.ProviderKey.Returns(Model.ProviderKey);
            factory.CreateEmbeddingGeneratorAsync(Arg.Any<ModelBinding>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { Creates++; OnCreate?.Invoke(); if (CreateFault is { } error) throw error; return ValueTask.FromResult(NullGenerator ? null! : generator); });
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            Service = new(query, [factory], Bindings, budget, ids);
        }
        internal void AssertNoIo() { Assert.AreEqual(0, Queries); Assert.AreEqual(0, Requests.Count); Assert.AreEqual(0, Creates); Assert.AreEqual(0, Settlements.Count); }
        internal void AssertSettlement(long? input, long? output, string outcome)
        {
            var settled = Settlements.Single(); Assert.AreEqual(Requests.Single().OperationId, settled.Id);
            Assert.AreEqual(input, settled.Usage.InputTokens); Assert.AreEqual(output, settled.Usage.OutputTokens);
            Assert.AreEqual(outcome, settled.Outcome); Assert.AreEqual(CancellationToken.None, settled.Token);
        }
        public void Dispose() => Bindings.Dispose();
    }
    /// <summary>禁止枚举且第二次Count变化；验证入口只读取有限索引并独占复制文本。</summary>
    private sealed class InputList(string[] values, int count) : IReadOnlyList<string>
    {
        internal int CountReads, IndexReads, EnumerationReads; internal Action? OnCount, OnIndex;
        public int Count { get { CountReads++; OnCount?.Invoke(); return CountReads == 1 ? count : int.MaxValue; } }
        public string this[int index] { get { IndexReads++; OnIndex?.Invoke(); return values[index]; } }
        public IEnumerator<string> GetEnumerator() { EnumerationReads++; throw new AssertFailedException("输入边界不能枚举外部集合。"); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
