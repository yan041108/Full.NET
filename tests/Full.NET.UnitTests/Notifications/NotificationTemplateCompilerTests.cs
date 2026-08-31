using System.Text.Json;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationTemplateCompilerTests
{
    [TestMethod]
    public void Unknown_or_oversized_parameters_fail_closed_without_echoing_values()
    {
        var schema = MustNormalize(new NotificationTemplateParameterSchema(
            1,
            [new NotificationTemplateParameterDefinition("orderNo", "string", true, 8)]));
        using var extra = JsonDocument.Parse("""{"orderNo":"A1","ssn":"SECRET-VALUE"}""");
        var unknown = NotificationTemplateCompiler.ValidateAndSnapshotParameters(schema, extra.RootElement);
        Assert.IsFalse(unknown.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.TemplateParameterInvalid, unknown.Error!.Code);
        Assert.IsFalse(unknown.Error.Message.Contains("SECRET-VALUE", StringComparison.Ordinal));

        using var tooLong = JsonDocument.Parse("""{"orderNo":"TOO-LONG-VALUE"}""");
        var oversized = NotificationTemplateCompiler.ValidateAndSnapshotParameters(schema, tooLong.RootElement);
        Assert.IsFalse(oversized.IsSuccess);
        Assert.IsFalse(oversized.Error!.Message.Contains("TOO-LONG-VALUE", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Missing_required_parameter_fails_closed()
    {
        var schema = MustNormalize(new NotificationTemplateParameterSchema(
            1,
            [new NotificationTemplateParameterDefinition("orderNo", "string", true, 32)]));
        using var empty = JsonDocument.Parse("{}");
        var missing = NotificationTemplateCompiler.ValidateAndSnapshotParameters(schema, empty.RootElement);
        Assert.IsFalse(missing.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.TemplateParameterInvalid, missing.Error!.Code);
    }

    [TestMethod]
    public void Publish_hash_is_stable_for_canonical_schema_and_body()
    {
        var draft = NotificationTemplateCompiler.NormalizeDraft(
            "订单 {orderNo}",
            new NotificationTemplateBody("正文 {orderNo}"),
            new NotificationTemplateParameterSchema(
                1,
                [new NotificationTemplateParameterDefinition("orderNo", "string", true, 32)]));
        Assert.IsTrue(draft.IsSuccess);
        var first = NotificationTemplateCompiler.ComputeContentHash(
            draft.Value!.Subject,
            draft.Value.BodyJson,
            draft.Value.ParameterSchemaJson,
            "c0");
        var second = NotificationTemplateCompiler.ComputeContentHash(
            draft.Value.Subject,
            draft.Value.BodyJson,
            draft.Value.ParameterSchemaJson,
            "c0");
        Assert.AreEqual(64, first.Length);
        Assert.AreEqual(first, second);
        Assert.AreNotEqual(
            first,
            NotificationTemplateCompiler.ComputeContentHash(
                draft.Value.Subject,
                draft.Value.BodyJson,
                draft.Value.ParameterSchemaJson,
                "s2"));
    }

    [TestMethod]
    public void Same_idempotency_payload_matches_and_different_scene_conflicts()
    {
        var recipients = new[]
        {
            new NotificationRecipientInput("user", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
        };
        var existing = new NotificationIntentRecordSnapshot(
            Guid.Parse("11111111-1111-7111-8111-111111111111"),
            "order.paid",
            """{"orderNo":"A1"}""",
            recipients);
        Assert.IsTrue(NotificationTemplateCompiler.PayloadsMatch(
            existing.TemplateVersionId,
            "order.paid",
            """{"orderNo":"A1"}""",
            recipients,
            existing));
        Assert.IsFalse(NotificationTemplateCompiler.PayloadsMatch(
            existing.TemplateVersionId,
            "order.shipped",
            """{"orderNo":"A1"}""",
            recipients,
            existing));
    }

    [TestMethod]
    public void Unknown_placeholder_fails_before_publish()
    {
        var draft = NotificationTemplateCompiler.NormalizeDraft(
            "Hello {unknownName}",
            new NotificationTemplateBody("body"),
            new NotificationTemplateParameterSchema(
                1,
                [new NotificationTemplateParameterDefinition("orderNo", "string", true, 32)]));
        Assert.IsFalse(draft.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.TemplateValidationFailed, draft.Error!.Code);
    }

    private static NormalizedParameterSchema MustNormalize(NotificationTemplateParameterSchema schema)
    {
        var result = NotificationTemplateCompiler.NormalizeSchema(schema);
        Assert.IsTrue(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }
}
