using System.Text.Json;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>审批新增通知摘要保持原有版本线格式并纳入静态 JSON 闭包。</summary>
[TestClass]
public sealed class EnterpriseRequestApprovalProgressSerializationTests
{
    [TestMethod]
    public void Final_notification_is_serialized_with_business_receipt_and_string_versions()
    {
        var now = new DateTimeOffset(2026, 10, 10, 1, 0, 0, TimeSpan.Zero);
        var notification = new NotificationIntentDeliverySnapshot(Guid.NewGuid(), now, 3, 1, 0, 2, 0, 0, 0, now.AddMinutes(1));
        var response = new EnterpriseRequestApprovalProgressResponse(Guid.NewGuid(), "Approved", 3,
            EnterpriseRequestApprovalDeliveryState.Finalized, Guid.NewGuid(), Guid.NewGuid(), 2, now, null, now, notification);
        var json = JsonSerializer.Serialize(response, EnterpriseRequestApprovalProgressJsonContext.Default.EnterpriseRequestApprovalProgressResponse);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("3", document.RootElement.GetProperty("requestVersion").GetString());
        Assert.AreEqual("2", document.RootElement.GetProperty("submittedVersion").GetString());
        Assert.AreEqual("finalized", document.RootElement.GetProperty("deliveryState").GetString());
        Assert.AreEqual(2, document.RootElement.GetProperty("finalNotification").GetProperty("failedDeliveryCount").GetInt32());
        var roundTrip = JsonSerializer.Deserialize(json, EnterpriseRequestApprovalProgressJsonContext.Default.EnterpriseRequestApprovalProgressResponse);
        Assert.AreEqual(response, roundTrip);
    }

    [TestMethod]
    public void Legacy_response_without_notification_keeps_existing_constructor_and_wire_values()
    {
        var response = new EnterpriseRequestApprovalProgressResponse(Guid.NewGuid(), "Draft", 1,
            EnterpriseRequestApprovalDeliveryState.NotSubmitted, null, null, null, null, null, null);
        var json = JsonSerializer.Serialize(response, EnterpriseRequestApprovalProgressJsonContext.Default.EnterpriseRequestApprovalProgressResponse);
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("1", document.RootElement.GetProperty("requestVersion").GetString());
        Assert.AreEqual("not_submitted", document.RootElement.GetProperty("deliveryState").GetString());
        Assert.IsNull(JsonSerializer.Deserialize(json, EnterpriseRequestApprovalProgressJsonContext.Default.EnterpriseRequestApprovalProgressResponse)!.FinalNotification);
    }
}
