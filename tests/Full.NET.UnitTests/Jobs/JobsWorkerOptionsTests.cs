using Full.NET.Modules.Jobs;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsWorkerOptionsTests
{
    [TestMethod]
    public void AddBackgroundServices_BindsDefaultsAndRejectsUnsafeBounds()
    {
        using var defaults = CreateProvider(
            new Dictionary<string, string?>());
        var defaultOptions = defaults
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<JobsWorkerOptions>>()
            .Value;
        Assert.AreEqual(10, defaultOptions.BatchSize);
        Assert.AreEqual(2000, defaultOptions.PollMilliseconds);

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:BatchSize"] = "0",
                ["Jobs:Worker:PollMilliseconds"] = "99",
            });
        var startupValidator = invalid.GetRequiredService<
            Microsoft.Extensions.Options.IStartupValidator>();
        var exception = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            startupValidator.Validate);

        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:BatchSize must be between 1 and 50.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:PollMilliseconds must be between 100 and 60000.");
    }

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();
        new JobsModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
