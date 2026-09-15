using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Retention;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// 验证 OIDC 保留清理在双数据库中仅删除超期且已失效的令牌与孤儿授权。
/// </summary>
internal static class IdentityOidcRetentionAssertions
{
    public static readonly IReadOnlyDictionary<string, string?> Settings =
        new Dictionary<string, string?>
        {
            ["Identity:Oidc:Enable"] = "true",
            ["Identity:Oidc:Issuer"] = "https://localhost/identity",
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "true",
            ["Identity:Oidc:Clients:0:ClientId"] = "integration-oidc-client",
            ["Identity:Oidc:Clients:0:RedirectUris:0"] = "https://localhost/signin-oidc",
        };

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var currentTenant = services.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();

        try
        {
            var now = services.GetRequiredService<IClock>().UtcNow;
            var subject = $"oidc-retention-{Guid.NewGuid():N}";
            await InsertFixturesAsync(
                services.GetRequiredService<ICommandExecutor>(),
                now,
                subject,
                cancellationToken);

            var runner = services.GetRequiredService<IdentityOidcRetentionRunner>();
            var result = await runner.RunOnceAsync(
                new IdentityOidcRetentionOptions
                {
                    Enabled = true,
                    RetentionDays = 30,
                },
                cancellationToken);

            Assert.AreEqual(1, result.TokensDeleted);
            Assert.AreEqual(1, result.AuthorizationsDeleted);
            Assert.AreEqual(
                new RetentionCounts { StaleCount = 0, FreshCount = 2 },
                await ReadCountsAsync(
                    services.GetRequiredService<IQueryExecutor>(),
                    subject,
                    now.AddDays(-30),
                    cancellationToken));
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task InsertFixturesAsync(
        ICommandExecutor command,
        DateTimeOffset now,
        string subject,
        CancellationToken cancellationToken)
    {
        var stale = now.AddDays(-60);
        var fresh = now;
        await command.ExecuteAsync(
            IdentityOidcSql.InsertToken,
            CreateTokenParameters(Guid.CreateVersion7(), subject, stale, OpenIddictConstants.Statuses.Revoked),
            cancellationToken);
        await command.ExecuteAsync(
            IdentityOidcSql.InsertToken,
            CreateTokenParameters(Guid.CreateVersion7(), subject, fresh, OpenIddictConstants.Statuses.Revoked),
            cancellationToken);
        await command.ExecuteAsync(
            IdentityOidcSql.InsertAuthorization,
            CreateAuthorizationParameters(Guid.CreateVersion7(), subject, stale, OpenIddictConstants.Statuses.Revoked),
            cancellationToken);
        await command.ExecuteAsync(
            IdentityOidcSql.InsertAuthorization,
            CreateAuthorizationParameters(Guid.CreateVersion7(), subject, fresh, OpenIddictConstants.Statuses.Revoked),
            cancellationToken);
    }

    private static Dictionary<string, object?> CreateTokenParameters(
        Guid id,
        string subject,
        DateTimeOffset creationDateUtc,
        string status) =>
        IdentitySqlParameters.Create(
            ("Id", id),
            ("ApplicationId", null),
            ("AuthorizationId", null),
            ("CreationDateUtc", creationDateUtc),
            ("ExpirationDateUtc", null),
            ("Payload", null),
            ("PropertiesJson", null),
            ("RedemptionDateUtc", null),
            ("ReferenceId", null),
            ("Status", status),
            ("Subject", subject),
            ("Type", OpenIddictConstants.TokenTypeIdentifiers.RefreshToken),
            ("Version", 1L),
            ("CreatedAtUtc", creationDateUtc),
            ("UpdatedAtUtc", creationDateUtc));

    private static Dictionary<string, object?> CreateAuthorizationParameters(
        Guid id,
        string subject,
        DateTimeOffset creationDateUtc,
        string status) =>
        IdentitySqlParameters.Create(
            ("Id", id),
            ("ApplicationId", null),
            ("CreationDateUtc", creationDateUtc),
            ("PropertiesJson", null),
            ("ScopesJson", null),
            ("Status", status),
            ("Subject", subject),
            ("Type", OpenIddictConstants.AuthorizationTypes.Permanent),
            ("Version", 1L),
            ("CreatedAtUtc", creationDateUtc),
            ("UpdatedAtUtc", creationDateUtc));

    private static async Task<RetentionCounts> ReadCountsAsync(
        IQueryExecutor query,
        string subject,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var counts = await query.QuerySingleOrDefaultAsync<RetentionCounts>(
            new SqlStatement(
                "test.identity.oidc.retention.read_fixture_counts",
                """
                SELECT
                    (SELECT COUNT(*) FROM fn_identity_oidc_token
                     WHERE Subject = @Subject AND CreationDateUtc < @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_identity_oidc_authorization
                       WHERE Subject = @Subject AND CreationDateUtc < @CutoffUtc)
                        AS StaleCount,
                    (SELECT COUNT(*) FROM fn_identity_oidc_token
                     WHERE Subject = @Subject AND CreationDateUtc >= @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_identity_oidc_authorization
                       WHERE Subject = @Subject AND CreationDateUtc >= @CutoffUtc)
                        AS FreshCount
                """,
                SqlDataScope.HostOnly),
            new { Subject = subject, CutoffUtc = cutoffUtc },
            cancellationToken);
        return counts
            ?? throw new InvalidOperationException(
                "OIDC retention fixture counts were not returned.");
    }

    private sealed record RetentionCounts
    {
        public long StaleCount { get; set; }

        public long FreshCount { get; set; }
    }
}