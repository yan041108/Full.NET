using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class ElasticsearchBulkOutcomeTests
{
    [TestMethod]
    public void Http200WithMixedItemsMustNotCompleteWholeBatch()
    {
        const string response = """
            {"errors":true,"items":[{"index":{"status":201}},{"index":{"status":429}},{"index":{"status":400}}]}
            """;

        var outcomes = ElasticsearchBulkOutcome.Parse(200, response, 3);

        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Succeeded, BulkItemOutcome.Retry, BulkItemOutcome.Isolate },
            outcomes);
    }

    [TestMethod]
    public void MissingOrUnexpectedItemsFailClosed()
    {
        const string missing = """
            {"errors":false,"items":[{"index":{"status":201}}]}
            """;

        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry, BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, missing, 2));
        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, "{}", 1));
    }

    [TestMethod]
    public void HttpFailureAndInvalidJsonFailClosed()
    {
        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(503, "service unavailable", 1));
        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, "not json", 1));
    }

    [TestMethod]
    public void MissingTargetIndexMustRetryRatherThanIsolateValidDocument()
    {
        const string response = """
            {"errors":true,"items":[{"index":{"status":404,"error":{"type":"index_not_found_exception"}}}]}
            """;
        CollectionAssert.AreEqual(new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, response, 1));
    }

    [TestMethod]
    public void NonNumericStatusAndContradictorySummaryFailClosed()
    {
        const string nonNumeric = """
            {"errors":true,"items":[{"index":{"status":"201"}}]}
            """;
        const string contradiction = """
            {"errors":false,"items":[{"index":{"status":400}}]}
            """;

        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, nonNumeric, 1));
        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, contradiction, 1));
    }

    [TestMethod]
    public void MultipleActionsOrSuccessWithErrorPayloadFailClosed()
    {
        const string multipleActions = """
            {"errors":false,"items":[{"index":{"status":201},"delete":{"status":400}}]}
            """;
        const string impossibleSuccess = """
            {"errors":false,"items":[{"index":{"status":201,"error":{"type":"rejected"}}}]}
            """;

        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, multipleActions, 1));
        CollectionAssert.AreEqual(
            new[] { BulkItemOutcome.Retry },
            ElasticsearchBulkOutcome.Parse(200, impossibleSuccess, 1));
    }
}
