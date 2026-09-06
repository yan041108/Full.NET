using System.Text;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageDingTalkApprovalSync;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class DingTalkApprovalSyncCallbackVerifierTests
{
    [TestMethod]
    public void Valid_signature_maps_completed_status()
    {
        Environment.SetEnvironmentVariable("FULLNET_TEST_DINGTALK_SYNC_CALLBACK_SECRET", "callback-secret");
        var body = Encoding.UTF8.GetBytes(
            """
            {
              "processInstanceId": "proc-001",
              "status": "COMPLETED",
              "result": "agree",
              "eventTime": "2026-09-06T10:00:01Z"
            }
            """);
        var verifier = CreateVerifier();
        var signature = DingTalkApprovalSyncCallbackVerifier.Sign(body, "callback-secret");
        var result = verifier.Verify(
            body,
            new Dictionary<string, string>
            {
                [DingTalkApprovalSyncCallbackVerifier.SignatureHeaderName] = signature,
            });

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("proc-001", result.Value!.ProcessInstanceId);
        Assert.AreEqual("COMPLETED", result.Value.ExternalStatusKey);
        Environment.SetEnvironmentVariable("FULLNET_TEST_DINGTALK_SYNC_CALLBACK_SECRET", null);
    }

    [TestMethod]
    public void MapExternalStatus_translates_dingtalk_status_keys()
    {
        Assert.AreEqual(
            DingTalkApprovalSyncStatusKeys.Running,
            DingTalkApprovalSyncService.MapExternalStatus("RUNNING"));
        Assert.AreEqual(
            DingTalkApprovalSyncStatusKeys.Completed,
            DingTalkApprovalSyncService.MapExternalStatus("COMPLETED"));
        Assert.IsNull(DingTalkApprovalSyncService.MapExternalStatus("UNKNOWN"));
    }

    private static DingTalkApprovalSyncCallbackVerifier CreateVerifier()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Providers:DingTalk:Workflow:CallbackSecretReference"] =
                    "env://FULLNET_TEST_DINGTALK_SYNC_CALLBACK_SECRET",
            })
            .Build();
        return new DingTalkApprovalSyncCallbackVerifier(configuration);
    }
}
