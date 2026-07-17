using System.Text.Json;
using Full.NET.Abstractions.Results;

namespace Full.NET.UnitTests.Results;

[TestClass]
public sealed class ErrorCompatibilityTests
{
    [TestMethod]
    public void Legacy_constructor_init_property_and_four_value_deconstruct_are_preserved()
    {
        IReadOnlyDictionary<string, string[]> errors =
            new Dictionary<string, string[]> { ["Name"] = ["Required."] };
        var error = new Error(
            Code: "validation.failed",
            Message: "Legacy message.",
            Type: ErrorType.Validation,
            ValidationErrors: errors) with
        {
            Message = "Updated legacy message.",
        };

        var (code, message, type, validationErrors) = error;

        Assert.AreEqual("validation.failed", code);
        Assert.AreEqual("Updated legacy message.", message);
        Assert.AreEqual(ErrorType.Validation, type);
        Assert.AreSame(errors, validationErrors);
        Assert.AreEqual(error.Message, error.DefaultMessage);
        Assert.IsNotNull(typeof(Error).GetConstructor(
        [
            typeof(string),
            typeof(string),
            typeof(ErrorType),
            typeof(IReadOnlyDictionary<string, string[]>),
        ]));
    }

    [TestMethod]
    public void Extended_constructor_is_unambiguous_and_keeps_additive_contract()
    {
        var arguments = new Dictionary<string, object?> { ["MinLength"] = 12 };
        var violations = new[]
        {
            new ValidationViolation(
                "Password",
                "identity.password.minimum_length",
                arguments),
        };
        var error = new Error(
            Code: "identity.bootstrap.invalid-password",
            Message: "Safe fallback.",
            Type: ErrorType.Validation,
            ValidationErrors: null,
            Arguments: arguments,
            ValidationViolations: violations);
        var legacyNull = new Error(
            "identity.bootstrap.invalid-password",
            "Safe fallback.",
            ErrorType.Validation,
            null);

        Assert.AreSame(arguments, error.Arguments);
        Assert.AreSame(violations, error.ValidationViolations);
        Assert.IsNull(legacyNull.Arguments);
    }

    [TestMethod]
    public void Json_shape_keeps_message_and_omits_default_message_alias()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var error = new Error(
            Code: "identity.bootstrap.invalid-password",
            Message: "Safe fallback.",
            Type: ErrorType.Validation,
            ValidationErrors: null,
            Arguments: new Dictionary<string, object?> { ["MinLength"] = 12 },
            ValidationViolations:
            [
                new ValidationViolation(
                    "Password",
                    "identity.password.minimum_length",
                    new Dictionary<string, object?> { ["MinLength"] = 12 }),
            ]);

        var json = JsonSerializer.Serialize(error, options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual("Safe fallback.", root.GetProperty("message").GetString());
        Assert.IsTrue(root.TryGetProperty("arguments", out _));
        Assert.IsTrue(root.TryGetProperty("validationViolations", out _));
        Assert.IsFalse(root.TryGetProperty("defaultMessage", out _));

        var roundTripped = JsonSerializer.Deserialize<Error>(json, options);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(error.Message, roundTripped.Message);
        Assert.AreEqual(error.DefaultMessage, roundTripped.DefaultMessage);
        Assert.HasCount(1, roundTripped.ValidationViolations!);
    }
}
