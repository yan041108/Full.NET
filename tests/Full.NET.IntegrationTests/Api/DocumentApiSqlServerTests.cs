using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class DocumentApiSqlServerTests
{
    [TestMethod]
    public async Task Host_document_items_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentHostItemAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task DocumentFilesReferenceClaim_race_is_atomic_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentFilesReferenceClaimAssertions.VerifyClaimDeleteConcurrencyAsync(factory);
    }

    [TestMethod]
    public async Task DocumentFilesReferenceClaim_unsynchronized_race_is_atomic_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentFilesReferenceClaimAssertions.VerifyClaimDeleteUnsynchronizedConcurrencyAsync(factory);
    }

    [TestMethod]
    public async Task DocumentFilesReferenceClaim_http_race_is_atomic_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentFilesReferenceClaimAssertions.VerifyClaimDeleteHttpConcurrencyAsync(factory);
    }

    [TestMethod]
    public async Task Host_document_categories_and_tags_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentHostCategoryTagAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_document_authorization_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentAuthorizationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_share_security_is_atomic_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentShareSecurityAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_share_concurrency_is_atomic_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentShareConcurrencyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_admin_net_parity_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DocumentAdminNetParityAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_share_rate_limit_follows_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?>
            {
                ["RateLimiting:EnableGlobalApiLimit"] = "false",
                ["Document:AnonymousShareAccessRateLimitPermitLimitPerMinute"] = "2",
            });

        await DocumentShareRateLimitAssertions.VerifyAsync(factory);
    }
}
