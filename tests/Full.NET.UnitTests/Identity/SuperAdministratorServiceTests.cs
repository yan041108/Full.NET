using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class SuperAdministratorServiceTests
{
    [TestMethod]
    public async Task Revoke_rejects_removing_the_last_active_super_administrator()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityRoleRecord(
                Guid.NewGuid(), null, "host", "host-administrator", "超级管理员",
                true, true, true, RoleDataScopeKinds.All, DateTimeOffset.UtcNow, null, 1));
        query.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveSuperAdministratorAssignment,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L, 1L);
        query.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveSuperAdministrators,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        var service = new SuperAdministratorService(
            query,
            command,
            new InlineTransaction(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());
        var userId = Guid.NewGuid();

        var result = await service.RevokeAsync(userId, userId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            "identity.super_administrator.last_remaining",
            result.Error?.Code);
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            IdentitySql.DeleteSuperAdministratorAssignment,
            default,
            default);
    }

    private sealed class InlineTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
