using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class ResourceErrorMessageLocalizerTests
{
    [TestMethod]
    public void Fallback_counter_contains_only_stable_code_and_locale_tags()
    {
        var measurements = new ConcurrentQueue<FallbackMeasurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ResourceErrorMessageLocalizer.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue(new FallbackMeasurement(
                instrument.Name,
                value,
                tags.ToArray())));
        listener.Start();
        var localizer = new ResourceErrorMessageLocalizer(
            [],
            new NamedMessageFormatter());

        var message = localizer.Localize(
            new Error(
                Code: "identity.missing",
                Message: "Safe fallback.",
                Type: ErrorType.Unexpected),
            CultureInfo.GetCultureInfo("en-US"));

        Assert.AreEqual("Safe fallback.", message);
        var matching = measurements.Where(measurement => measurement.Tags.Any(tag =>
            tag.Key == "code"
            && Equals(tag.Value, "identity.missing")))
            .ToArray();
        Assert.HasCount(1, matching);
        var measurement = matching[0];
        Assert.AreEqual("fullnet.localization.error.fallbacks", measurement.Name);
        Assert.AreEqual(1L, measurement.Value);
        CollectionAssert.AreEquivalent(
            new[] { "code", "locale" },
            measurement.Tags.Select(tag => tag.Key).ToArray());
        Assert.AreEqual(
            "identity.missing",
            measurement.Tags.Single(tag => tag.Key == "code").Value);
        Assert.AreEqual(
            "en-US",
            measurement.Tags.Single(tag => tag.Key == "locale").Value);
    }

    [TestMethod]
    public void Resource_prefix_must_end_with_segment_separator()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ResourceManagerErrorResourceSource(
                "identity",
                new System.Resources.ResourceManager(
                    "Full.NET.Modules.Identity.Resources.IdentityErrors",
                    typeof(IdentityModule).Assembly)));
    }

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
            Message: "Safe fallback.",
            Type: ErrorType.Validation,
            ValidationErrors: null,
            Arguments: new Dictionary<string, object?> { ["MaxLength"] = 128 },
            ValidationViolations: null);

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
            Message: "Safe fallback.",
            Type: ErrorType.Validation);
        var missingResource = new Error(
            Code: "validation.unknown",
            Message: "Unknown fallback.",
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

    private sealed record FallbackMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags);
}
