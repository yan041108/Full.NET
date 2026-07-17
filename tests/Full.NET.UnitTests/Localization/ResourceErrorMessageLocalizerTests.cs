using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class ResourceErrorMessageLocalizerTests
{
    [TestMethod]
    public void Longest_matching_prefix_supplies_and_formats_template()
    {
        IErrorResourceSource[] sources =
        [
            new DictionaryResourceSource(
                "identity.",
                new Dictionary<string, string>
                {
                    ["identity.password.maximum_length"] = "Identity fallback.",
                }),
            new DictionaryResourceSource(
                "identity.password.",
                new Dictionary<string, string>
                {
                    ["identity.password.maximum_length"] =
                        "Password must not exceed {MaxLength} characters.",
                }),
        ];
        var localizer = new ResourceErrorMessageLocalizer(
            sources,
            new NamedMessageFormatter());
        var error = new Error(
            Code: "identity.password.maximum_length",
            DefaultMessage: "Safe fallback.",
            Type: ErrorType.Validation,
            Arguments: new Dictionary<string, object?> { ["MaxLength"] = 128 });

        var message = localizer.Localize(
            error,
            CultureInfo.GetCultureInfo("en-US"));

        Assert.AreEqual("Password must not exceed 128 characters.", message);
    }

    [TestMethod]
    public void Missing_resource_or_argument_returns_safe_default_message()
    {
        var localizer = new ResourceErrorMessageLocalizer(
            [
                new DictionaryResourceSource(
                    "validation.",
                    new Dictionary<string, string>
                    {
                        ["validation.maximum_length"] =
                            "Length must not exceed {MaxLength}.",
                    }),
            ],
            new NamedMessageFormatter());

        var missingArgument = new Error(
            Code: "validation.maximum_length",
            DefaultMessage: "Safe fallback.",
            Type: ErrorType.Validation);
        var missingResource = new Error(
            Code: "validation.unknown",
            DefaultMessage: "Unknown fallback.",
            Type: ErrorType.Validation);

        Assert.AreEqual(
            "Safe fallback.",
            localizer.Localize(missingArgument, CultureInfo.GetCultureInfo("en-US")));
        Assert.AreEqual(
            "Unknown fallback.",
            localizer.Localize(missingResource, CultureInfo.GetCultureInfo("en-US")));
    }

    private sealed class DictionaryResourceSource(
        string prefix,
        IReadOnlyDictionary<string, string> resources) : IErrorResourceSource
    {
        public string Prefix => prefix;

        public bool TryGetTemplate(
            string code,
            CultureInfo culture,
            out string template) => resources.TryGetValue(code, out template!);
    }
}
