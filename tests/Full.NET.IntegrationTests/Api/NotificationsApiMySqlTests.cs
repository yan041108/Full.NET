using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Notifications;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class NotificationsApiMySqlTests
{
    [TestMethod]
    public async Task Host_announcement_and_inbox_management_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await NotificationsHostAnnouncementAssertions.VerifyAsync(factory);
        await NotificationsInboxMessageAssertions.VerifyAsync(factory);
    }
}
