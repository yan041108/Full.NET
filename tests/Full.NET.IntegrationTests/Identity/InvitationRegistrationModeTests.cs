using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>邀请在开放及仅邀请模式下均保留权威目标、挑战用途和待入驻绑定。</summary>
[TestClass]
public sealed class InvitationRegistrationModeTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, IdentityRegistrationMode.Open)]
    [DataRow(DatabaseProvider.MySql, IdentityRegistrationMode.Open)]
    [DataRow(DatabaseProvider.SqlServer, IdentityRegistrationMode.InvitationOnly)]
    [DataRow(DatabaseProvider.MySql, IdentityRegistrationMode.InvitationOnly)]
    public async Task Invited_registration_preserves_target_and_one_time_binding(DatabaseProvider provider, IdentityRegistrationMode mode)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await InvitedRegistrationAssertions.VerifyAsync(factory, mode: mode);
    }
}
