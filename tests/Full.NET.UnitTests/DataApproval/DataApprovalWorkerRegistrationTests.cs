using Full.NET.Modules.DataApproval;
using Full.NET.Modules.DataApproval.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.DataApproval;

[TestClass]
public sealed class DataApprovalWorkerRegistrationTests
{
    [TestMethod]
    public void Background_registration_binds_and_validates_recovery_options()
    {
        using var configured = CreateProvider(new Dictionary<string, string?>
        {
            ["DataApproval:RequestRecoveryWorker:BatchSize"] = "7",
            ["DataApproval:ApplicationRecoveryWorker:BatchSize"] = "9",
        });
        Assert.AreEqual(7, configured.GetRequiredService<IOptions<DataApprovalRequestRecoveryWorkerOptions>>().Value.BatchSize);
        Assert.AreEqual(9, configured.GetRequiredService<IOptions<DataApprovalRequestApplicationRecoveryWorkerOptions>>().Value.BatchSize);

        using var invalid = CreateProvider(new Dictionary<string, string?>
        {
            ["DataApproval:RequestRecoveryWorker:BatchSize"] = "0",
        });
        Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);
    }

    private static ServiceProvider CreateProvider(IReadOnlyDictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(configuration);
        new DataApprovalModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
