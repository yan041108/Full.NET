using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class ApiKeyAuthenticationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AuthenticateAsync_WhenLastUseIsRecent_DoesNotWrite()
    {
        var (service, command) = CreateService(Now.AddMinutes(-1));

        var principal = await service.AuthenticateAsync("fnk_test-secret");

        Assert.IsNotNull(principal);
        await command.DidNotReceive().ExecuteAsync(
            ApiKeySql.TouchLastUsed,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AuthenticateAsync_WhenLastUseIsOutsideWindow_WritesWithCutoff()
    {
        var (service, command) = CreateService(Now.AddMinutes(-5));

        var principal = await service.AuthenticateAsync("fnk_test-secret");

        Assert.IsNotNull(principal);
        await command.Received(1).ExecuteAsync(
            ApiKeySql.TouchLastUsed,
            Arg.Is<object?>(parameters => HasExpectedTouchParameters(parameters)),
            Arg.Any<CancellationToken>());
    }

    private static (ApiKeyAuthenticationService Service, ICommandExecutor Command)
        CreateService(DateTimeOffset? lastUsedAtUtc)
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        query.QuerySingleOrDefaultAsync<ApiKeyAuthenticationRow>(
                ApiKeySql.FindForAuthentication,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ApiKeyAuthenticationRow
            {
                ApiKeyId = Guid.CreateVersion7(),
                UserId = Guid.CreateVersion7(),
                Username = "hostadmin",
                DisplayName = "Host Administrator",
                PermissionsJson = """["identity.users.read"]""",
                IsActive = true,
                UserIsActive = true,
                SecurityStamp = "stamp",
                LastUsedAtUtc = lastUsedAtUtc,
            });
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        return (new ApiKeyAuthenticationService(query, command, clock), command);
    }

    private static bool HasExpectedTouchParameters(object? parameters)
    {
        if (parameters is null)
        {
            return false;
        }

        var type = parameters.GetType();
        return Equals(type.GetProperty("LastUsedAtUtc")?.GetValue(parameters), Now)
            && Equals(
                type.GetProperty("LastUsedBeforeUtc")?.GetValue(parameters),
                Now.AddMinutes(-5));
    }
}
