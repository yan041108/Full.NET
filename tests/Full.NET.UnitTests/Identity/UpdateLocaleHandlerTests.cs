using System.Security.Claims;
using Full.NET.Abstractions.Results;
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

    [TestMethod]
    public async Task Concurrent_removal_returns_session_not_active_instead_of_conflict()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                IdentitySql.FindProfileByIdentity,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateProfile(), (IdentityProfileRecord?)null);
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
        await queryExecutor.Received(2).QuerySingleOrDefaultAsync<IdentityProfileRecord>(
            IdentitySql.FindProfileByIdentity,
            Arg.Is<object?>(parameters => HasSignedIdentity(parameters)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_disable_returns_session_not_active_instead_of_conflict()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                IdentitySql.FindProfileByIdentity,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateProfile(), CreateProfile(isActive: false));
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
        queryExecutor.QuerySingleOrDefaultAsync<IdentityProfileRecord>(
                IdentitySql.FindProfileByIdentity,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateProfile(1), CreateProfile(2));
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
            new LocaleNormalizer(Options.Create(new FullNetLocalizationOptions())));

    private static Command CreateCommand() => new(
        LocaleCatalog.English,
        1,
        new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString("D")),
                new Claim(IdentityClaimTypes.ActorScope, "host"),
            ],
            "unit-test")));

    private static bool HasSignedIdentity(object? parameters)
    {
        if (parameters is null)
        {
            return false;
        }

        var parameterType = parameters.GetType();
        return Equals(parameterType.GetProperty("UserId")?.GetValue(parameters), UserId)
            && Equals(parameterType.GetProperty("ScopeKey")?.GetValue(parameters), "host");
    }

    private static IdentityProfileRecord CreateProfile(
        int version = 1,
        bool isActive = true) => new()
    {
        Id = UserId,
        ScopeKey = "host",
        Username = "admin",
        DisplayName = "系统管理员",
        IsActive = isActive,
        PreferredLocale = LocaleCatalog.Chinese,
        ProfileVersion = version,
    };
}
