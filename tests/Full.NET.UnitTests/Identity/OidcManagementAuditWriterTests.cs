using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>治理审计保留固定结果和资源摘要，操作者单独关联，不将资源原文伪装成结果码。</summary>
[TestClass]
public sealed class OidcManagementAuditWriterTests
{
    [TestMethod]
    public async Task Management_event_hashes_resource_and_uses_fixed_result_code()
    {
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var writer = new OidcManagementAuditWriter(commands, ids, clock);
        var actor = Guid.CreateVersion7();
        await writer.WriteAsync(actor, OidcManagementAuditWriter.ClientCreatedEventType, "client:resource", null, null, default);
        await commands.Received(1).ExecuteAsync(IdentitySql.InsertAuthAudit,
            Arg.Is<AuthAuditEvent>(e => e != null && e.ActorUserId == actor && e.AuthenticationMethod == "admin"
                && e.UsernameFingerprint == TokenHash.Compute("client:resource")
                && e.ResultCode == OidcManagementAuditWriter.ClientCreatedEventType + ".succeeded"), Arg.Any<CancellationToken>());
    }
}
