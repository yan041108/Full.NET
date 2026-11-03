using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.UnitTests.Ai;

/// <summary>消费者不扩张所有者授权，取消和晚到响应不能释放旧字段。</summary>
[TestClass]
public sealed class AiPersonalScheduleSourceReaderTests
{
    [TestMethod]
    public async Task Capture_rejects_a_different_owner_even_when_the_port_returns_a_snapshot()
    {
        var fixture = new Fixture(); fixture.Rows[0] = fixture.Rows[0] with { Proof = fixture.Proof with { OwnerUserId = Guid.CreateVersion7() } };
        Assert.IsNull(await fixture.Reader.CaptureAsync(fixture.Actor, [fixture.Id], default));
    }

    [TestMethod]
    public async Task Captured_collection_cannot_be_replaced_by_the_source_after_delivery()
    {
        var fixture = new Fixture(); var result = await fixture.Reader.CaptureAsync(fixture.Actor, [fixture.Id], default);
        Assert.IsNotNull(result); fixture.Rows[0] = fixture.Rows[0] with { Content = "迟到替换" };
        Assert.AreEqual("本人字段", result[0].Content);
    }

    [TestMethod]
    public async Task Current_source_denial_never_falls_back_to_previous_proof()
    {
        var fixture = new Fixture { VerifyResult = false };
        Assert.IsFalse(await fixture.Reader.RevalidateAsync(fixture.Actor, [fixture.Proof], default)); Assert.AreEqual(1, fixture.Verifications);
    }

    [TestMethod]
    public async Task Tenant_changed_during_verification_rejects_old_scope_result()
    {
        var fixture = new Fixture(); fixture.OnVerify = () => fixture.TenantId = Guid.CreateVersion7();
        Assert.IsFalse(await fixture.Reader.RevalidateAsync(fixture.Actor, [fixture.Proof], default));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Background_binding_must_match_actor_before_using_owner_port(bool correct)
    {
        var fixture = new Fixture();
        var original = new Full.NET.Modules.Identity.Contracts.SessionBindingSnapshot(correct ? fixture.Actor : Guid.CreateVersion7(),null,Guid.CreateVersion7(),"original-stamp","scope","scope");
        Assert.AreEqual(correct,await fixture.Reader.RevalidateAsync(fixture.Actor,[fixture.Proof],default,original));
        Assert.AreEqual(correct?1:0,fixture.BackgroundVerifications);
    }

    [TestMethod]
    public async Task Caller_transaction_is_rejected_before_calendar_contract_invocation()
    {
        var fixture = new Fixture { HasTransaction = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Reader.CaptureAsync(fixture.Actor, [fixture.Id], default));
        Assert.AreEqual(0, fixture.Captures);
    }

    [TestMethod]
    public async Task Cancellation_drains_controlled_pending_port_and_never_delivers_late_snapshot()
    {
        var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.CaptureWait = async () => { entered.SetResult(); await release.Task; };
        var pending = fixture.Reader.CaptureAsync(fixture.Actor, [fixture.Id], cancellation.Token);
        await entered.Task; cancellation.Cancel(); release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending); Assert.AreEqual(1, fixture.Captures);
    }

    [TestMethod]
    [DataRow("kind")][DataRow("purpose")][DataRow("id")][DataRow("version")][DataRow("hash")]
    public async Task Invalid_proof_shape_is_rejected_before_calling_the_owner(string field)
    {
        var fixture = new Fixture(); var proof = fixture.Proof;
        proof = field switch { "kind" => proof with { Kind = "unknown" }, "purpose" => proof with { Purpose = "export" },
            "id" => proof with { Id = Guid.Empty }, "version" => proof with { Version = 0 }, _ => proof with { ProjectionHash = "not-a-hash" } };
        Assert.IsFalse(await fixture.Reader.RevalidateAsync(fixture.Actor, [proof], default)); Assert.AreEqual(0, fixture.Verifications);
    }

    private sealed class Fixture : IPersonalScheduleKnowledgeSource, IDataTransactionState, ICurrentTenant
    {
        internal readonly Guid Id = Guid.CreateVersion7(); internal readonly Guid Actor = Guid.CreateVersion7();
        internal readonly List<PersonalScheduleKnowledgeSnapshot> Rows = [];
        internal readonly AiPersonalScheduleSourceReader Reader;
        internal PersonalScheduleKnowledgeProof Proof => Rows[0].Proof;
        internal Func<Task>? CaptureWait; internal Action? OnVerify;
        internal bool VerifyResult = true; internal int Captures; internal int Verifications; internal int BackgroundVerifications;
        internal Guid? TenantId;
        public bool HasTransaction { get; set; }
        public bool IsHost => TenantId is null; public bool IsAvailable => true;
        public Guid? IdScope => TenantId; Guid? ICurrentTenant.Id => TenantId;
        public string? Identifier => TenantId?.ToString("N"); public string? Name => Identifier;
        internal Fixture()
        {
            Rows.Add(new(new(PersonalScheduleKnowledgeSourceKeys.Kind, PersonalScheduleKnowledgeSourceKeys.Purpose,
                Id, null, Actor, 1, new string('A', 64)), "本人字段", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "pending"));
            Reader = new(this, this, this);
        }
        public async Task<IReadOnlyList<PersonalScheduleKnowledgeSnapshot>?> CaptureAsync(IReadOnlyCollection<Guid> scheduleIds, CancellationToken cancellationToken = default)
        {
            Captures++; if (CaptureWait is not null) await CaptureWait(); return Rows;
        }
        public Task<bool> VerifyAsync(IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs, CancellationToken cancellationToken = default)
        { Verifications++; OnVerify?.Invoke(); return Task.FromResult(VerifyResult); }
        public Task<bool> VerifyBackgroundAsync(Full.NET.Modules.Identity.Contracts.SessionBindingSnapshot original,
            IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs, CancellationToken cancellationToken = default)
        { BackgroundVerifications++; return VerifyAsync(proofs, cancellationToken); }
    }
}
