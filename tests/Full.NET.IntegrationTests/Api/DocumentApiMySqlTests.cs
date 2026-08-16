using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class DocumentApiMySqlTests
{
    [TestMethod]
    public async Task Host_document_items_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentHostItemAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task DocumentFilesReferenceClaim_race_is_atomic_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentFilesReferenceClaimAssertions.VerifyClaimDeleteConcurrencyAsync(factory);
    }

    [TestMethod]
    public async Task DocumentFilesReferenceClaim_unsynchronized_race_is_atomic_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentFilesReferenceClaimAssertions.VerifyClaimDeleteUnsynchronizedConcurrencyAsync(factory);
    }

    [TestMethod]
    public async Task DocumentFilesReferenceClaim_http_race_is_atomic_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentFilesReferenceClaimAssertions.VerifyClaimDeleteHttpConcurrencyAsync(factory);
    }

    [TestMethod]
    public async Task Host_document_categories_and_tags_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentHostCategoryTagAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Host_document_authorization_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentAuthorizationAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_share_security_is_atomic_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentShareSecurityAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_share_concurrency_is_atomic_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentShareConcurrencyAssertions.VerifyAsync(factory);
    }

    [TestMethod]
    public async Task Document_admin_net_parity_follows_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DocumentAdminNetParityAssertions.VerifyAsync(factory);
    }
}
