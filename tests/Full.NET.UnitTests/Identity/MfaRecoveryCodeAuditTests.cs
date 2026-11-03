using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>恢复码的成功事件与消费原子提交，拒绝事件保留且不包含恢复材料。</summary>
[TestClass]
public sealed class MfaRecoveryCodeAuditTests
{
    [TestMethod]
    [DataRow("short", false)]
    [DataRow("not-the-right-code", false)]
    [DataRow("valid-recovery-code", true)]
    public async Task Consumption_records_true_outcome_without_secret(string code, bool success)
    {
        var f = new Fixture();
        var result = await f.Service.ConsumeAsync(f.User, code);
        Assert.AreEqual(success, result.IsSuccess);
        Assert.HasCount(1, f.Events);
        var audit = f.Events.Single();
        Assert.AreEqual(AuthenticationEventCatalog.RecoveryCodeConsumed, audit.EventType);
        Assert.AreEqual(success, audit.Succeeded);
        Assert.AreEqual(f.User, audit.UserId);
        Assert.AreEqual(success ? f.User : (Guid?)null, audit.ActorUserId);
        Assert.IsFalse(audit.ToString().Contains(code, StringComparison.Ordinal));
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Concurrent_consumption_loser_records_rejection()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(MfaRecoveryCodeSql.Consume, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await f.Service.ConsumeAsync(f.User, "valid-recovery-code");
        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(f.Events.Single().Succeeded);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Audit_failure_rolls_back_consumption_or_regeneration(bool regenerate)
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            if (regenerate) await f.Service.RegenerateAsync(f.User);
            else await f.Service.ConsumeAsync(f.User, "valid-recovery-code");
        });
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task Regeneration_audits_once_without_plaintext_codes()
    {
        var f = new Fixture();
        var result = await f.Service.RegenerateAsync(f.User);
        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(10, result.Value!.RecoveryCodes);
        var audit = f.Events.Single();
        Assert.AreEqual(AuthenticationEventCatalog.RecoveryCodesRegenerated, audit.EventType);
        foreach (var code in result.Value.RecoveryCodes) Assert.IsFalse(audit.ToString().Contains(code, StringComparison.Ordinal));
    }

    private sealed class Fixture
    {
        internal readonly Guid User = Guid.CreateVersion7();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal readonly List<AuthAuditEvent> Events = [];
        internal readonly MfaRecoveryCodeService Service;
        internal Fixture()
        {
            var queries = Substitute.For<IQueryExecutor>();
            queries.QueryAsync<MfaRecoveryCodeRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new[] { new MfaRecoveryCodeRecord(Guid.CreateVersion7(), SHA256.HashData(Encoding.UTF8.GetBytes("valid-recovery-code")), 1) });
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call[1] is AuthAuditEvent audit) Events.Add(audit);
                return 1;
            });
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            var random = Substitute.For<IRandomTokenGenerator>();
            random.Generate(16).Returns(_ => Guid.NewGuid().ToString("N"));
            Service = new MfaRecoveryCodeService(queries, Commands, new DapperCommandTransaction(Coordinator), clock,
                ids, random, new AuthenticationSecurityEventWriter(Commands, ids, clock));
        }
    }
}
