using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Notifications;

namespace Full.NET.IntegrationTests.Api;

/// <summary>在真实 SQL Server 上验证 Notifications 完整 API 纵向切片。</summary>
[TestClass]
public sealed class NotificationsApiSqlServerTests
{
    /// <summary>验证公告、收件箱、平台配置、本人端点与投递 Worker 契约。</summary>
    /// <returns>表示异步测试执行的任务。</returns>
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
        await NotificationRecipientEndpointAssertions.VerifyAsync(factory);
        await NotificationDeliveryWorkerAssertions.VerifyAsync(factory);
    }
}
