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

    [TestMethod]
    public void AddBackgroundServices_BindsLeaseDefaultsAndRejectsUnsafeRenewalWindow()
    {
        using var defaults = CreateProvider(
            new Dictionary<string, string?>());
        var defaultOptions = defaults
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<JobsWorkerOptions>>()
            .Value;
        Assert.AreEqual(300, defaultOptions.LeaseSeconds);
        Assert.AreEqual(60, defaultOptions.LeaseRenewalSeconds);

        using var invalidBounds = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:LeaseSeconds"] = "29",
                ["Jobs:Worker:LeaseRenewalSeconds"] = "4",
            });
        var boundsException = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            invalidBounds
                .GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>()
                .Validate);
        CollectionAssert.Contains(
            boundsException.Failures.ToArray(),
            "Jobs:Worker:LeaseSeconds must be between 30 and 3600.");
        CollectionAssert.Contains(
            boundsException.Failures.ToArray(),
            "Jobs:Worker:LeaseRenewalSeconds must be between 5 and 1200.");

        using var invalidWindow = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:LeaseSeconds"] = "30",
                ["Jobs:Worker:LeaseRenewalSeconds"] = "16",
            });
        var windowException = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            invalidWindow
                .GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>()
                .Validate);
        CollectionAssert.Contains(
            windowException.Failures.ToArray(),
            "Jobs:Worker:LeaseRenewalSeconds must not exceed half of LeaseSeconds.");
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
