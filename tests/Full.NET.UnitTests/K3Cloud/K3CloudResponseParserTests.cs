using Full.NET.Modules.K3Cloud.Domain;

namespace Full.NET.UnitTests.K3Cloud;

[TestClass]
public sealed class K3CloudResponseParserTests
{
    [TestMethod]
    public void TryParseLoginSuccess_returns_true_for_login_result_type_1()
    {
        var succeeded = K3CloudResponseParser.TryParseLoginSuccess(
            """{"LoginResultType":1}""",
            out var message);

        Assert.IsTrue(succeeded);
        Assert.Contains("succeeded", message, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void TryParseSaveResult_extracts_bill_id_and_number()
    {
        var succeeded = K3CloudResponseParser.TryParseSaveResult(
            """
            {
              "Result": {
                "ResponseStatus": { "IsSuccess": true },
                "Id": 100001,
                "Number": "SO-001"
              }
            }
            """,
            out var billId,
            out var billNo,
            out _);

        Assert.IsTrue(succeeded);
        Assert.AreEqual("100001", billId);
        Assert.AreEqual("SO-001", billNo);
    }

    [TestMethod]
    public void BuildSubmitPayload_uses_bill_id_and_number()
    {
        var payload = K3CloudResponseParser.BuildSubmitPayload("100001", "SO-001");
        Assert.Contains("\"Ids\":\"100001\"", payload);
        Assert.Contains("\"SO-001\"", payload);
    }
}
