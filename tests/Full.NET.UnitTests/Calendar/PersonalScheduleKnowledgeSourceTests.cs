using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Calendar.Features.Knowledge;
using Full.NET.Modules.Calendar.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Calendar;

/// <summary>本人日程来源的完整批次、当前授权、版本摘要与事务边界回归。</summary>
[TestClass]
public sealed class PersonalScheduleKnowledgeSourceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Capture_and_verify_bind_complete_owner_projection_in_trusted_scope(bool scoped)
    {
        var fixture = new Fixture(scoped); var second = Guid.CreateVersion7();
        fixture.Rows.Add(fixture.Row(second, "第二项\n包含分隔符"));
        var captured = await fixture.Source.CaptureAsync([second, fixture.Id]);
        Assert.IsNotNull(captured); Assert.AreEqual(2, captured.Count); Assert.AreEqual(second, captured[0].Proof.Id);
        Assert.AreEqual(2, fixture.Queries); Assert.AreEqual(3, fixture.Authorizations);
        Assert.IsTrue(await fixture.Source.VerifyAsync(captured.Select(row => row.Proof).ToArray()));
        Assert.AreEqual(scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, fixture.Statement!.Scope);
        Assert.AreEqual(fixture.Actor.UserId, fixture.Parameters!["OwnerUserId"]);
        Assert.IsFalse(fixture.Parameters.ContainsKey("TenantId"), "租户参数由正式执行器注入，不能由来源请求覆盖。");
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("zero")]
    [DataRow("ninth")]
    public async Task Invalid_complete_selection_is_rejected_before_any_authorization_or_sql(string kind)
    {
        var fixture = new Fixture();
        Guid[] ids = kind switch { "empty" => [], "duplicate" => [fixture.Id, fixture.Id], "zero" => [Guid.Empty],
            _ => Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray() };
        Assert.IsNull(await fixture.Source.CaptureAsync(ids)); Assert.AreEqual(0, fixture.Queries); Assert.AreEqual(0, fixture.Authorizations);
    }

    [TestMethod]
    [DataRow("kind")]
    [DataRow("purpose")]
    [DataRow("owner")]
    [DataRow("tenant")]
    [DataRow("version")]
    [DataRow("hash")]
    public async Task Forged_or_stale_proof_never_releases_source(string field)
    {
        var fixture = new Fixture(); var proof = PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof;
        proof = field switch { "kind" => proof with { Kind = "document-v1" }, "purpose" => proof with { Purpose = "export" },
            "owner" => proof with { OwnerUserId = Guid.CreateVersion7() }, "tenant" => proof with { TenantId = Guid.CreateVersion7() },
            "version" => proof with { Version = proof.Version + 1 }, _ => proof with { ProjectionHash = new string('A', 64) } };
        Assert.IsFalse(await fixture.Source.VerifyAsync([proof]));
    }

    [TestMethod]
    [DataRow("permission")]
    [DataRow("session")]
    [DataRow("actor")]
    public async Task Authorization_changed_while_reading_rejects_the_entire_batch(string change)
    {
        var fixture = new Fixture();
        fixture.BeforeAuthorize = count => { if (count == 2) fixture.CurrentActor = change switch
            { "permission" => null, "session" => fixture.Actor with { SessionId = Guid.CreateVersion7() },
                _ => fixture.Actor with { UserId = Guid.CreateVersion7() } }; };
        Assert.IsNull(await fixture.Source.CaptureAsync([fixture.Id]));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Version_or_same_version_payload_changed_during_authorization_wait_is_rejected(bool version)
    {
        var fixture = new Fixture();
        fixture.BeforeAuthorize = count => { if (count == 2) fixture.Rows[0] = fixture.Row(fixture.Id,
            version ? "原日程" : "同版本篡改正文", version ? 2 : 1); };
        Assert.IsNull(await fixture.Source.CaptureAsync([fixture.Id])); Assert.AreEqual(2, fixture.Queries);
    }

    [TestMethod]
    public async Task Missing_one_of_the_sources_is_not_silently_truncated()
    {
        var fixture = new Fixture();
        Assert.IsNull(await fixture.Source.CaptureAsync([fixture.Id, Guid.CreateVersion7()]));
    }

    [TestMethod]
    public async Task Request_collection_is_frozen_before_authorization_wait()
    {
        var fixture = new Fixture(); var ids = new[] { fixture.Id };
        fixture.BeforeAuthorize = count => { if (count == 1) ids[0] = Guid.CreateVersion7(); };
        Assert.IsNotNull(await fixture.Source.CaptureAsync(ids));
        CollectionAssert.AreEqual(new[] { fixture.Id }, (Guid[])fixture.Parameters!["Ids"]!);
    }

    [TestMethod]
    public async Task Caller_transaction_is_rejected_before_any_cross_module_authorization()
    {
        var fixture = new Fixture { HasTransaction = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Source.CaptureAsync([fixture.Id]));
        Assert.AreEqual(0, fixture.Authorizations); Assert.AreEqual(0, fixture.Queries);
    }

    [TestMethod]
    public async Task Cancellation_after_sql_never_delivers_a_snapshot()
    {
        var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        fixture.BeforeQuery = _ => cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Source.CaptureAsync([fixture.Id], cancellation.Token));
    }

    [TestMethod]
    public void Projection_preserves_legal_zero_duration_and_normalizes_offsets()
    {
        var fixture = new Fixture(); var start = DateTimeOffset.Parse("2026-10-30T12:00:00+08:00", System.Globalization.CultureInfo.InvariantCulture);
        var local = fixture.Row(fixture.Id, "含换行\n和中文", start: start, end: start);
        var utc = fixture.Row(fixture.Id, "含换行\n和中文", start: start.ToUniversalTime(), end: start.ToUniversalTime());
        Assert.AreEqual(PersonalScheduleKnowledgeProjection.Capture(local).Proof,
            PersonalScheduleKnowledgeProjection.Capture(utc).Proof);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Background_revalidates_original_session_without_borrowing_interactive_authority(bool scoped)
    {
        var fixture = new Fixture(scoped) { ForbidInteractive = true };
        var proof = PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof;
        Assert.IsTrue(await fixture.Source.VerifyBackgroundAsync(fixture.Binding, [proof]));
        Assert.AreEqual(3, fixture.BackgroundAuthorizations); Assert.AreEqual(0, fixture.Authorizations); Assert.AreEqual(2, fixture.Queries);
        Assert.AreEqual(fixture.Actor.UserId, fixture.Parameters!["OwnerUserId"]);
    }

    [TestMethod] [DataRow("expired")] [DataRow("permission")] [DataRow("session")] [DataRow("version")] [DataRow("payload")]
    public async Task Background_wait_drift_denies_the_complete_proof(string change)
    {
        var fixture = new Fixture { ForbidInteractive = true };
        var proof = PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof;
        fixture.BeforeBackground = count =>
        {
            if (count != 2) return;
            if (change is "expired" or "permission") fixture.CurrentActor = null;
            else if (change == "session") fixture.CurrentActor = fixture.Actor with { SessionId = Guid.CreateVersion7() };
            else fixture.Rows[0] = fixture.Row(fixture.Id, "已改变", change == "version" ? 2 : 1);
        };
        Assert.IsFalse(await fixture.Source.VerifyBackgroundAsync(fixture.Binding, [proof])); Assert.AreEqual(0, fixture.Authorizations);
    }

    [TestMethod] [DataRow("kind")] [DataRow("owner")] [DataRow("tenant")] [DataRow("session")] [DataRow("stamp")]
    public async Task Invalid_background_binding_denies_before_authorization_or_query(string fault)
    {
        var fixture = new Fixture(); var proof = PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof;
        var binding = fault switch { "kind" => fixture.Binding with { SessionKind = "api-key" },
            "owner" => fixture.Binding with { UserId = Guid.CreateVersion7() }, "tenant" => fixture.Binding with { TenantId = Guid.CreateVersion7() },
            "session" => fixture.Binding with { SessionId = Guid.Empty }, _ => fixture.Binding with { SecurityStamp = "" } };
        Assert.IsFalse(await fixture.Source.VerifyBackgroundAsync(binding, [proof])); Assert.AreEqual(0, fixture.Queries); Assert.AreEqual(0, fixture.BackgroundAuthorizations);
    }

    [TestMethod]
    public async Task Missing_background_port_does_not_fall_back_to_interactive_permissions()
    {
        var fixture = new Fixture { ForbidInteractive = true }; var port = new PersonalScheduleKnowledgeSource(fixture, fixture, fixture, fixture);
        Assert.IsFalse(await port.VerifyBackgroundAsync(fixture.Binding, [PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof]));
        Assert.AreEqual(0, fixture.Queries); Assert.AreEqual(0, fixture.Authorizations);
    }
    [TestMethod]
    public async Task Background_caller_transaction_is_rejected_before_owner_contract_io()
    {
        var fixture = new Fixture { HasTransaction = true, ForbidInteractive = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Source.VerifyBackgroundAsync(fixture.Binding,
            [PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof]));
        Assert.AreEqual(0, fixture.Queries); Assert.AreEqual(0, fixture.BackgroundAuthorizations);
    }
    [TestMethod]
    public async Task Worker_without_http_authorizer_can_only_use_the_original_background_session()
    {
        var fixture=new Fixture {ForbidInteractive=true};var source=new PersonalScheduleKnowledgeSource(fixture,fixture,fixture,background:fixture);
        Assert.IsNull(await source.CaptureAsync([fixture.Id]));Assert.AreEqual(0,fixture.Queries);
        Assert.IsTrue(await source.VerifyBackgroundAsync(fixture.Binding,[PersonalScheduleKnowledgeProjection.Capture(fixture.Rows[0]).Proof]));
        Assert.AreEqual(0,fixture.Authorizations);Assert.AreEqual(3,fixture.BackgroundAuthorizations);
    }

    private sealed class Fixture : IQueryExecutor, IDataTransactionState, ICurrentSessionAuthorization, IBackgroundSessionAuthorization, ICurrentTenant
    {
        internal readonly Guid Id = Guid.CreateVersion7();
        internal readonly AuthorizedSessionActor Actor;
        internal AuthorizedSessionActor? CurrentActor;
        internal readonly List<PersonalScheduleRecord> Rows = [];
        internal readonly PersonalScheduleKnowledgeSource Source;
        internal Action<int>? BeforeAuthorize;
        internal Action<int>? BeforeQuery;
        internal int Queries;
        internal int Authorizations;
        internal int BackgroundAuthorizations;
        internal bool ForbidInteractive;
        internal Action<int>? BeforeBackground;
        internal SessionBindingSnapshot Binding => new(Actor.UserId, Actor.TenantId, Actor.SessionId, "original-stamp", "original-scope", "original-scope");
        internal SqlStatement? Statement;
        internal Dictionary<string, object?>? Parameters;
        public bool HasTransaction { get; set; }
        public Guid? TenantId { get; }
        Guid? ICurrentTenant.Id => TenantId;
        public bool IsAvailable => TenantId.HasValue;
        public bool IsHost => TenantId is null;
        public string? Identifier => TenantId?.ToString("N");
        public string? Name => Identifier;

        internal Fixture(bool scoped = false)
        {
            TenantId = scoped ? Guid.CreateVersion7() : null;
            Actor = new(Guid.CreateVersion7(), TenantId, Guid.CreateVersion7()); CurrentActor = Actor;
            Rows.Add(Row(Id, "原日程")); Source = new(this, this, this, this, this);
        }

        internal PersonalScheduleRecord Row(Guid id, string content, int version = 1, DateTimeOffset? start = null, DateTimeOffset? end = null) =>
            new() { Id = id, OwnerUserId = Actor.UserId, TenantId = TenantId, Content = content, Version = version,
                StartAtUtc = start ?? DateTimeOffset.UnixEpoch, EndAtUtc = end ?? DateTimeOffset.UnixEpoch.AddHours(1) };

        public Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("来源必须同时要求读取和固定助手用途。");

        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(ForbidInteractive, "后台不得借用当前交互用户。");
            CollectionAssert.AreEquivalent(new[] { CalendarPermissions.Read, PersonalScheduleKnowledgePermissions.Read }, permissionCodes.ToArray());
            Authorizations++; BeforeAuthorize?.Invoke(Authorizations); return Task.FromResult(CurrentActor);
        }
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("后台必须同时复核读取和用途权限。");
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(Binding, binding); CollectionAssert.AreEquivalent(new[] { CalendarPermissions.Read, PersonalScheduleKnowledgePermissions.Read }, permissionCodes.ToArray());
            BackgroundAuthorizations++; BeforeBackground?.Invoke(BackgroundAuthorizations); return Task.FromResult(CurrentActor);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Statement = statement; Parameters = (Dictionary<string, object?>)parameters!;
            Queries++; BeforeQuery?.Invoke(Queries);
            var ids = (Guid[])Parameters["Ids"]!;
            return Task.FromResult<IReadOnlyList<T>>(Rows.Where(row => ids.Contains(row.Id)).Cast<T>().ToArray());
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("来源必须完整批量读取，不能逐对象调用。");
    }
}
