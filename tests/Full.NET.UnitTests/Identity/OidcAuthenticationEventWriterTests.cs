using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>OIDC 认证事件区分目标用户和已认证操作者，并保留会话关联。</summary>
[TestClass]
public sealed class OidcAuthenticationEventWriterTests
{
    [TestMethod]
    public async Task Failed_center_login_does_not_claim_an_authenticated_actor()
    {
        var userId = Guid.NewGuid();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<AuthAuditEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var writer = new OidcAuthenticationEventWriter(command, ids, clock);

        await writer.WriteAsync(userId, "ADMIN", OidcAuthenticationEventWriter.CenterLogin,
            "identity.invalid-password", false, CancellationToken.None);

        await command.Received(1).ExecuteAsync(IdentitySql.InsertAuthAudit,
            Arg.Is<AuthAuditEvent>(audit => audit != null && audit.UserId == userId
                && audit.ActorUserId == null
                && audit.ResultCode == "identity.invalid-password"
                && !audit.Succeeded), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Application_session_event_links_center_client_and_application_session()
    {
        var userId = Guid.NewGuid();
        var centerSessionId = Guid.NewGuid();
        var applicationSessionId = Guid.NewGuid();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<AuthAuditEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var writer = new OidcAuthenticationEventWriter(command, ids, clock);

        await writer.WriteAsync(userId, null,
            OidcAuthenticationEventWriter.ApplicationSessionCreated,
            "identity.oidc_application_session_created", true, CancellationToken.None,
            centerSessionId, "admin-ui", applicationSessionId);

        await command.Received(1).ExecuteAsync(IdentitySql.InsertAuthAudit,
            Arg.Is<AuthAuditEvent>(audit => audit != null && audit.UserId == userId
                && audit.ActorUserId == userId
                && audit.CenterSessionId == centerSessionId
                && audit.ApplicationSessionId == applicationSessionId
                && audit.ClientId == "admin-ui"
                && audit.AuthenticationMethod == "sso"), Arg.Any<CancellationToken>());
    }
}
