using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Notifications;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class NotificationsApiSqlServerTests
{
    [TestMethod]
    public async Task Host_announcement_and_inbox_management_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            configureTestServices: NotificationProfileBindingAssertions.ConfigureTestServices);

        await NotificationsHostAnnouncementAssertions.VerifyAsync(factory);
        await NotificationsInboxMessageAssertions.VerifyAsync(factory);
        await NotificationTenantInboxAssertions.VerifyAsync(factory);
        await NotificationTemplateIntentAssertions.VerifyAsync(factory);
        await NotificationProfileBindingAssertions.VerifyAsync(factory);
        await NotificationDeliveryWorkerAssertions.VerifyAsync(factory);
    }
}
