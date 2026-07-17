using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class PermissionSnapshotReaderTests
{
    [TestMethod]
    public async Task Read_intersects_database_grants_with_the_code_catalog()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<string>(
                IdentitySql.GetUserPermissionCodes,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns([
                "tenancy.tenants.switch",
                "removed.permission",
                "platform.dashboard.read",
                "tenancy.tenants.switch",
            ]);
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
        var reader = new PermissionSnapshotReader(query, catalog);

        var permissions = await reader.ReadAsync(
            Guid.Parse("01981a3f-00c0-7000-8000-000000000010"),
            "host",
            null,
            default);

        CollectionAssert.AreEqual(
            new[] { "platform.dashboard.read", "tenancy.tenants.switch" },
            permissions.ToArray());
    }
}
