using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>安全事件写入失败不得被调用方当作成功。</summary>
[TestClass]
public sealed class AuthenticationSecurityEventWriterTests
{
    [TestMethod]
    public async Task Storage_failure_and_missing_insert_fail_closed()
    {
        var command = Substitute.For<ICommandExecutor>();
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var writer = new AuthenticationSecurityEventWriter(command, ids, clock);

        command.ExecuteAsync(IdentitySql.InsertAuthAudit,
                Arg.Any<AuthAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("storage unavailable")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            writer.WriteAsync(Guid.NewGuid(), null, "mfa.test", "identity.test",
                false, "totp", CancellationToken.None));

        command.ExecuteAsync(IdentitySql.InsertAuthAudit,
                Arg.Any<AuthAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            writer.WriteAsync(Guid.NewGuid(), null, "mfa.test", "identity.test",
                false, "totp", CancellationToken.None));
    }
}
