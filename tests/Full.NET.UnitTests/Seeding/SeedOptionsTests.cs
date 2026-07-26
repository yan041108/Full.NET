using Full.NET.Seeding.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Seeding;

[TestClass]
public sealed class SeedOptionsTests
{
    [TestMethod]
    public void Startup_validation_rejects_an_invalid_default_locale()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SeedOptions.SectionName}:DefaultLocale"] = "not a locale!",
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddFullNetSeeding(configuration)
            .BuildServiceProvider();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        StringAssert.Contains(exception.Message, SeedErrorCodes.OptionsInvalid);
    }
}
