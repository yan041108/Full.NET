using System.Globalization;
using Full.NET.Hosting.Api;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class NamedMessageFormatterTests
{
    [TestMethod]
    public void Exact_named_arguments_are_replaced_without_interpreting_format_code()
    {
        var formatter = new NamedMessageFormatter();
        var formatted = formatter.TryFormat(
            "Length must not exceed {MaxLength}.",
            new Dictionary<string, object?> { ["MaxLength"] = 128 },
            CultureInfo.GetCultureInfo("en-US"),
            out var message);

        Assert.IsTrue(formatted);
        Assert.AreEqual("Length must not exceed 128.", message);

        Assert.IsFalse(formatter.TryFormat(
            "Length must not exceed {MaxLength:D4}.",
            new Dictionary<string, object?> { ["MaxLength"] = 128 },
            CultureInfo.GetCultureInfo("en-US"),
            out _));
    }

    [TestMethod]
    public void Missing_named_argument_rejects_the_template()
    {
        var formatter = new NamedMessageFormatter();

        Assert.IsFalse(formatter.TryFormat(
            "Range is {From} to {To}.",
            new Dictionary<string, object?> { ["From"] = 1 },
            CultureInfo.GetCultureInfo("en-US"),
            out _));
    }
}
