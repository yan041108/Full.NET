using Full.NET.Localization;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class LocaleNormalizerTests
{
    [TestMethod]
    [DataRow("zh-CN", "zh-CN")]
    [DataRow("zh-Hans", "zh-CN")]
    [DataRow("zh-SG", "zh-CN")]
    [DataRow("en-US", "en-US")]
    [DataRow("en-GB", "en-US")]
    public void Normalize_maps_canonical_and_alias_locales(
        string requestedLocale,
        string expectedLocale)
    {
        var normalizer = CreateNormalizer();

        var actual = normalizer.Normalize(requestedLocale);

        Assert.AreEqual(expectedLocale, actual);
        Assert.IsTrue(normalizer.IsSupported(requestedLocale));
    }

    [TestMethod]
    [DataRow("not a locale!")]
    [DataRow("fr-FR")]
    [DataRow("")]
    public void Normalize_falls_back_to_default_for_invalid_or_unknown_locale(
        string requestedLocale)
    {
        var normalizer = CreateNormalizer();

        Assert.AreEqual("zh-CN", normalizer.Normalize(requestedLocale));
        Assert.IsFalse(normalizer.IsSupported(requestedLocale));
    }

    [TestMethod]
    public void Normalize_falls_back_to_default_for_missing_locale()
    {
        var normalizer = CreateNormalizer();

        Assert.AreEqual("zh-CN", normalizer.Normalize(null));
        Assert.IsFalse(normalizer.IsSupported(null));
    }

    [TestMethod]
    public void Options_defaults_match_the_governance_catalog()
    {
        var options = new FullNetLocalizationOptions();

        Assert.AreEqual("zh-CN", options.DefaultLocale);
        CollectionAssert.AreEqual(
            new[] { "zh-CN", "en-US" },
            options.SupportedLocales.ToArray());
    }

    [TestMethod]
    public void Options_validator_accepts_the_governance_defaults()
    {
        var result = new FullNetLocalizationOptionsValidator()
            .Validate(null, new FullNetLocalizationOptions());

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Options_validator_rejects_an_empty_supported_locale_list()
    {
        var options = new FullNetLocalizationOptions
        {
            SupportedLocales = [],
        };

        var result = new FullNetLocalizationOptionsValidator().Validate(null, options);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Options_validator_rejects_case_insensitive_duplicates()
    {
        var options = new FullNetLocalizationOptions
        {
            SupportedLocales = ["zh-CN", "ZH-cn"],
        };

        var result = new FullNetLocalizationOptionsValidator().Validate(null, options);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Options_validator_rejects_a_missing_default_locale()
    {
        var options = new FullNetLocalizationOptions
        {
            SupportedLocales = ["en-US"],
        };

        var result = new FullNetLocalizationOptionsValidator().Validate(null, options);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Options_validator_rejects_invalid_culture_names()
    {
        var options = new FullNetLocalizationOptions
        {
            DefaultLocale = "not a locale!",
            SupportedLocales = ["not a locale!"],
        };

        var result = new FullNetLocalizationOptionsValidator().Validate(null, options);

        Assert.IsTrue(result.Failed);
    }

    private static LocaleNormalizer CreateNormalizer() =>
        new(Options.Create(new FullNetLocalizationOptions()));
}
