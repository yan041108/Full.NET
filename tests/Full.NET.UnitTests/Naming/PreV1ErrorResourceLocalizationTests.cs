using System.Globalization;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Resources;

namespace Full.NET.UnitTests.Naming;

[TestClass]
public sealed class PreV1ErrorResourceLocalizationTests
{
    [TestMethod]
    public void Canonical_tenancy_error_codes_localize_from_module_resources()
    {
        var localizer = new ResourceErrorMessageLocalizer(
            [new TenancyErrorResourceSource()],
            new NamedMessageFormatter());

        var message = localizer.Localize(
            new Full.NET.Abstractions.Results.Error(
                Code: TenancyErrorCodes.IdentifierExists,
                Message: "fallback",
                Type: Full.NET.Abstractions.Results.ErrorType.Conflict),
            CultureInfo.GetCultureInfo("en-US"));

        Assert.AreEqual(
            "A tenant with this identifier already exists.",
            message);
    }
}
