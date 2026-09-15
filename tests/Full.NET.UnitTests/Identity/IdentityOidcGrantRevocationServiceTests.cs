using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcGrantRevocationServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("01981a75-f500-7000-8000-000000000011");
    private static readonly Guid ApplicationId =
        Guid.Parse("01981a75-f500-7000-8000-000000000020");
    private const string ClientId = "fixture-oidc-a-public";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Revoke_by_user_id_revokes_tokens_then_authorizations()
    {
        var command = new RecordingCommandExecutor(
            new Dictionary<string, Queue<int>>(StringComparer.Ordinal)
            {
                ["identity.revoke_oidc_tokens_by_subject"] = new Queue<int>([3]),
                ["identity.revoke_oidc_authorizations_by_subject"] = new Queue<int>([2]),
            });
        var service = CreateService(command);

        var result = await service.RevokeByUserIdAsync(UserId, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "identity.revoke_oidc_tokens_by_subject",
                "identity.revoke_oidc_authorizations_by_subject",
            },
            command.Statements.Select(statement => statement.Name).ToArray());
        Assert.AreEqual(3, result.TokensRevoked);
        Assert.AreEqual(2, result.AuthorizationsRevoked);
        Assert.AreEqual(UserId.ToString("D"), ReadSqlParameter<string>(command.Parameters[0], "Subject"));
        Assert.AreEqual(Statuses.Revoked, ReadSqlParameter<string>(command.Parameters[0], "RevokedStatus"));
        Assert.AreEqual(Now, ReadSqlParameter<DateTimeOffset>(command.Parameters[0], "UpdatedAtUtc"));
        Assert.AreEqual(UserId.ToString("D"), ReadSqlParameter<string>(command.Parameters[1], "Subject"));
    }

    [TestMethod]
    public async Task Revoke_by_user_id_returns_zero_counts_when_nothing_matches()
    {
        var command = new RecordingCommandExecutor();
        var service = CreateService(command);

        var result = await service.RevokeByUserIdAsync(UserId, CancellationToken.None);

        Assert.AreEqual(0, result.TokensRevoked);
        Assert.AreEqual(0, result.AuthorizationsRevoked);
        Assert.AreEqual(2, command.Statements.Count);
    }

    [TestMethod]
    public async Task Revoke_by_user_and_client_scopes_to_application_filter()
    {
        var query = Substitute.For<IQueryExecutor>();
        query
            .QuerySingleOrDefaultAsync<IdentityOidcApplication>(
                IdentityOidcSql.FindApplicationByClientId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityOidcApplication { Id = ApplicationId, ClientId = ClientId });
        var command = new RecordingCommandExecutor(
            new Dictionary<string, Queue<int>>(StringComparer.Ordinal)
            {
                ["identity.revoke_oidc_tokens_by_filter"] = new Queue<int>([2]),
                ["identity.revoke_oidc_authorizations_by_filter"] = new Queue<int>([1]),
            });
        var service = CreateService(command, query);

        var result = await service.RevokeByUserAndClientAsync(
            UserId,
            ClientId,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "identity.revoke_oidc_tokens_by_filter",
                "identity.revoke_oidc_authorizations_by_filter",
            },
            command.Statements.Select(statement => statement.Name).ToArray());
        Assert.AreEqual(2, result.TokensRevoked);
        Assert.AreEqual(1, result.AuthorizationsRevoked);
        Assert.AreEqual(UserId.ToString("D"), ReadSqlParameter<string>(command.Parameters[0], "Subject"));
        Assert.AreEqual(ApplicationId, ReadSqlParameter<Guid>(command.Parameters[0], "ApplicationId"));
        Assert.AreEqual(ApplicationId, ReadSqlParameter<Guid>(command.Parameters[1], "ApplicationId"));
    }

    [TestMethod]
    public async Task Revoke_by_user_and_client_returns_zero_when_application_missing()
    {
        var query = Substitute.For<IQueryExecutor>();
        query
            .QuerySingleOrDefaultAsync<IdentityOidcApplication>(
                IdentityOidcSql.FindApplicationByClientId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((IdentityOidcApplication?)null);
        var command = new RecordingCommandExecutor();
        var service = CreateService(command, query);

        var result = await service.RevokeByUserAndClientAsync(
            UserId,
            ClientId,
            CancellationToken.None);

        Assert.AreEqual(0, result.TokensRevoked);
        Assert.AreEqual(0, result.AuthorizationsRevoked);
        Assert.AreEqual(0, command.Statements.Count);
    }

    private static IdentityOidcGrantRevocationService CreateService(
        RecordingCommandExecutor command,
        IQueryExecutor? query = null) =>
        new(
            query ?? Substitute.For<IQueryExecutor>(),
            command,
            new FixedClock());

    private sealed class RecordingCommandExecutor(
        IReadOnlyDictionary<string, Queue<int>>? results = null)
        : ICommandExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public List<object> Parameters { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            Parameters.Add(parameters
                ?? throw new InvalidOperationException("Parameters are required."));
            var affectedRows = results is null
                ? 0
                : results[statement.Name].Dequeue();
            return Task.FromResult(affectedRows);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}