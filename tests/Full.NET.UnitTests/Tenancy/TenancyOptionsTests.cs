using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class TenancyOptionsTests
{
    [TestMethod]
    public async Task Host_domain_configuration_is_validated_at_startup()
    {
        using (var nullDomainsHost = CreateHost(
                   [],
                   services => services.PostConfigure<TenancyOptions>(
                       options => options.HostDomains = null!)))
        {
            var exception = await Assert.ThrowsAsync<OptionsValidationException>(
                () => nullDomainsHost.StartAsync());

            StringAssert.Contains(
                string.Join(";", exception.Failures),
                "Tenancy:HostDomains");
        }

        string[][] invalidDomainSets =
        [
            [" "],
            ["https://admin.example.com"],
            ["admin.example.com:443"],
            ["admin.example.com/path"],
            ["*.example.com"],
            ["admin.example.com", "ADMIN.EXAMPLE.COM"],
        ];

        foreach (var domains in invalidDomainSets)
        {
            using var host = CreateHost(domains);

            var exception = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync());

            StringAssert.Contains(
                string.Join(";", exception.Failures),
                "Tenancy:HostDomains");
        }

        using var validHost = CreateHost(
            ["localhost", "127.0.0.1", "::1", "admin.example.com"]);

        await validHost.StartAsync();
        await validHost.StopAsync();
    }

    private static IHost CreateHost(
        IReadOnlyList<string> domains,
        Action<IServiceCollection>? configureServices = null)
    {
        var settings = domains
            .Select((domain, index) =>
                new KeyValuePair<string, string?>(
                    $"{TenancyOptions.SectionName}:HostDomains:{index}",
                    domain));

        return new HostBuilder()
            .ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                new TenancyModule().AddMigrationServices(
                    services,
                    context.Configuration);
                configureServices?.Invoke(services);
            })
            .Build();
    }
}
