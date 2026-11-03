using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Full.NET.UnitTests.Identity;

/// <summary>协议拒绝只关联已登记客户端，不从被拒绝的请求推断身份或保存凭据。</summary>
[TestClass]
public sealed class IdentityOidcProtocolRejectionAuditTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Protocol_rejection_is_safe_deduplicated_and_committed(bool knownClient)
    {
        var apps = Substitute.For<IOpenIddictApplicationManager>();
        apps.FindByClientIdAsync("client", Arg.Any<CancellationToken>()).Returns(knownClient ? new object() : null);
        var commands = Substitute.For<ICommandExecutor>();
        AuthAuditEvent? recorded = null;
        commands.ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            recorded = (AuthAuditEvent)call[1]!;
            return 1;
        });
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var coordinator = new RecordingDbTransactionCoordinator();
        var handler = new IdentityOidcProtocolRejectionAuditHandler(apps, new OidcAuthenticationEventWriter(commands, ids, clock),
            new DapperCommandTransaction(coordinator));
        var transaction = new OpenIddictServerTransaction { Request = new OpenIddictRequest
            { ClientId = "client", RefreshToken = "secret-refresh", ClientSecret = "secret-client" } };
        var context = new OpenIddictServerEvents.ProcessErrorContext(transaction)
            { Error = "invalid_grant", ErrorDescription = "untrusted secret-refresh" };
        await handler.HandleAsync(context);
        await handler.HandleAsync(context);
        Assert.IsNotNull(recorded);
        Assert.AreEqual(AuthenticationEventCatalog.ProtocolRejected, recorded.EventType);
        Assert.AreEqual("identity.oidc_protocol.invalid_grant", recorded.ResultCode);
        Assert.AreEqual(knownClient ? "client" : null, recorded.ClientId);
        Assert.IsNull(recorded.UserId);
        Assert.IsNull(recorded.ActorUserId);
        Assert.IsFalse(recorded.ToString().Contains("secret-", StringComparison.Ordinal));
        Assert.AreEqual(1, coordinator.CommitCount);
        await commands.Received(1).ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Unknown_protocol_error_is_not_persisted_as_a_result_code()
    {
        Assert.AreEqual("identity.oidc_protocol.rejected", AuthenticationEventCatalog.ProtocolResult("secret=raw-token"));
        Assert.AreEqual("protocol", AuthenticationEventCatalog.Categories[AuthenticationEventCatalog.ProtocolRejected]);
        Assert.AreEqual("mfa", AuthenticationEventCatalog.Categories[AuthenticationEventCatalog.RecoveryCodeConsumed]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Storage_failure_rolls_back_and_does_not_mark_rejection_as_audited(bool throws)
    {
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throws ? throw new InvalidOperationException("storage timeout") : 0);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var coordinator = new RecordingDbTransactionCoordinator();
        var handler = new IdentityOidcProtocolRejectionAuditHandler(Substitute.For<IOpenIddictApplicationManager>(),
            new OidcAuthenticationEventWriter(commands, ids, clock), new DapperCommandTransaction(coordinator));
        var context = new OpenIddictServerEvents.ProcessErrorContext(new OpenIddictServerTransaction()) { Error = "invalid_request" };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await handler.HandleAsync(context));
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
        Assert.IsNull(context.Transaction.GetProperty<string>("fullnet.identity.protocol_rejection_audited"));
    }
}
