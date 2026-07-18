using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.UpdateLocale;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class UpdateLocaleHandlerTests
{
    private static readonly Guid UserId =
        Guid.Parse("01981b1a-e200-7000-8000-000000000011");
    private static readonly Guid SessionId =
        Guid.Parse("01981b1a-e200-7000-8000-000000000012");
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Revoked_session_cannot_update_persistent_locale()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSession(revokedAtUtc: Now));
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                IdentitySql.UpdateLocalePreference,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var handler = CreateHandler(queryExecutor, commandExecutor);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.SessionNotActive, result.Error?.Code);
        await commandExecutor.DidNotReceive().ExecuteAsync(
            IdentitySql.UpdateLocalePreference,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_removal_returns_session_not_active_instead_of_conflict()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSession(), (RefreshSessionRecord?)null);
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                IdentitySql.UpdateLocalePreference,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var handler = CreateHandler(queryExecutor, commandExecutor);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.SessionNotActive, result.Error?.Code);
        Assert.AreEqual(ErrorType.Unauthorized, result.Error?.Type);
        await queryExecutor.Received(2).QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            Arg.Is<object?>(parameters => HasSessionId(parameters)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_disable_returns_session_not_active_instead_of_conflict()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSession(), CreateSession(isActive: false));
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                IdentitySql.UpdateLocalePreference,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var handler = CreateHandler(queryExecutor, commandExecutor);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.SessionNotActive, result.Error?.Code);
        Assert.AreEqual(ErrorType.Unauthorized, result.Error?.Type);
    }

    [TestMethod]
    public async Task Active_profile_version_race_returns_profile_conflict()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSession(), CreateSession());
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                IdentitySql.UpdateLocalePreference,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var handler = CreateHandler(queryExecutor, commandExecutor);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.ProfileVersionConflict, result.Error?.Code);
        Assert.AreEqual(ErrorType.Conflict, result.Error?.Type);
    }

    private static Handler CreateHandler(
        IQueryExecutor queryExecutor,
        ICommandExecutor commandExecutor) => new(
            queryExecutor,
            commandExecutor,
            new LocaleNormalizer(Options.Create(new FullNetLocalizationOptions())),
            new FixedClock(Now));

    private static Command CreateCommand() => new(
        LocaleCatalog.English,
        1,
        new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString("D")),
                new Claim(IdentityClaimTypes.ActorScope, "host"),
                new Claim(IdentityClaimTypes.SessionId, SessionId.ToString("D")),
                new Claim(IdentityClaimTypes.SecurityStamp, "security-stamp"),
            ],
            "unit-test")));

    private static bool HasSessionId(object? parameters)
    {
        if (parameters is null)
        {
            return false;
        }

        var parameterType = parameters.GetType();
        return Equals(parameterType.GetProperty("SessionId")?.GetValue(parameters), SessionId);
    }

    private static RefreshSessionRecord CreateSession(
        DateTimeOffset? revokedAtUtc = null,
        bool isActive = true) => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        ScopeKey = "host",
        IsActive = isActive,
        SecurityStamp = "security-stamp",
        ExpiresAtUtc = Now.AddHours(1),
        RevokedAtUtc = revokedAtUtc,
        PreferredLocale = LocaleCatalog.Chinese,
        ProfileVersion = 1,
    };

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
