using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class MyInboxQueryServiceTests
{
    [TestMethod]
    public void NormalizeStatusFilter_accepts_read_and_unread_only()
    {
        Assert.AreEqual(InboxMessageStatuses.Read, MyInboxQueryService.NormalizeStatusFilter("read"));
        Assert.AreEqual(InboxMessageStatuses.Unread, MyInboxQueryService.NormalizeStatusFilter(" unread "));
        Assert.IsNull(MyInboxQueryService.NormalizeStatusFilter(null));
        Assert.IsNull(MyInboxQueryService.NormalizeStatusFilter(""));
        Assert.IsNull(MyInboxQueryService.NormalizeStatusFilter("archived"));
    }
}
