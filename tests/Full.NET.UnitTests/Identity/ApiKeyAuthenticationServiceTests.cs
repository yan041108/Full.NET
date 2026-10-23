using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.ManageOpenAccessClients;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>单元测试（Identity）：Api Key Authentication Service。</summary>
[TestClass]
public sealed class ApiKeyAuthenticationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false, null, 0L, true)]
    [DataRow(true, null, 0L, true)]
    [DataRow(true, 10, 9L, true)]
    [DataRow(true, 10, 10L, false)]
    public async Task Authentication_reads_binding_once_and_preserves_quota_guard(bool bound, int? quota, long usage, bool allowed)
    {
        var query = Substitute.For<IQueryExecutor>();
        var (service, command) = CreateService(Now.AddMinutes(-1), query, bound, quota);
        query.QuerySingleOrDefaultAsync<OpenAccessClientUsageCountRow>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new OpenAccessClientUsageCountRow { SuccessCount = usage });

        var principal = await service.AuthenticateAsync("fnk_test-secret", new DefaultHttpContext());

        Assert.AreEqual(allowed, principal is not null);
        await query.Received(1).QuerySingleOrDefaultAsync<ApiKeyAuthenticationWithQuotaRow>(ApiKeySql.FindForAuthentication,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await query.DidNotReceiveWithAnyArgs().QuerySingleOrDefaultAsync<OpenAccessClientQuotaRow>(default!, default, default);
        Assert.AreEqual(bound && quota > 0 ? 2 : 1, query.ReceivedCalls().Count());
        await command.Received(bound ? 1 : 0).ExecuteAsync(IdentitySql.InsertAuthAudit, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AuthenticateAsync_WhenLastUseIsRecent_DoesNotWrite()
    {
        var (service, command) = CreateService(Now.AddMinutes(-1));

        var principal = await service.AuthenticateAsync(
            "fnk_test-secret",
            new DefaultHttpContext());

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

        var principal = await service.AuthenticateAsync(
            "fnk_test-secret",
            new DefaultHttpContext());

        Assert.IsNotNull(principal);
        await command.Received(1).ExecuteAsync(
            ApiKeySql.TouchLastUsed,
            Arg.Is<object?>(parameters => HasExpectedTouchParameters(parameters)),
            Arg.Any<CancellationToken>());
    }

    private static (ApiKeyAuthenticationService Service, ICommandExecutor Command)
        CreateService(DateTimeOffset? lastUsedAtUtc, IQueryExecutor? query = null, bool bound = false, int? quota = null)
    {
        query ??= Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        query.QuerySingleOrDefaultAsync<ApiKeyAuthenticationWithQuotaRow>(
                ApiKeySql.FindForAuthentication,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ApiKeyAuthenticationWithQuotaRow
            {
                ApiKeyId = Guid.CreateVersion7(),
                UserId = Guid.CreateVersion7(),
                Username = "hostadmin",
                DisplayName = "Host Administrator",
                KeyPrefix = "fnoa_unit-test",
                PermissionsJson = """["identity.users.read"]""",
                IsActive = true,
                UserIsActive = true,
                SecurityStamp = "stamp",
                LastUsedAtUtc = lastUsedAtUtc,
                OpenAccessClientId = bound ? Guid.CreateVersion7() : null,
                DailyRequestQuota = quota,
            });
        query.QuerySingleOrDefaultAsync<OpenAccessClientQuotaRow>(
                OpenAccessClientObservabilitySql.FindQuotaByApiKeyId,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((OpenAccessClientQuotaRow?)null);
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var accessSupport = new OpenAccessClientAccessSupport(
            query,
            command,
            clock,
            Substitute.For<Full.NET.Abstractions.Ids.IIdGenerator>(),
            Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        return (new ApiKeyAuthenticationService(query, command, accessSupport, clock), command);
    }

    private static bool HasExpectedTouchParameters(object? parameters)
    {
        if (parameters is null)
        {
            return false;
        }

        return Equals(ReadSqlParameter<DateTimeOffset>(parameters, "LastUsedAtUtc"), Now)
            && Equals(
                ReadSqlParameter<DateTimeOffset>(parameters, "LastUsedBeforeUtc"),
                Now.AddMinutes(-5));
    }
}
