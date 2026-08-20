using Full.NET.Modules.Jobs;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

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
        Assert.AreEqual(1, defaultOptions.MaxConcurrency);
        Assert.AreEqual(1, defaultOptions.MaxAttempts);
        Assert.AreEqual(30, defaultOptions.RetryDelaySeconds);
        Assert.AreEqual("fixed", defaultOptions.RetryBackoffMode);
        Assert.AreEqual(86400, defaultOptions.RetryMaxDelaySeconds);
        Assert.AreEqual(0, defaultOptions.RetryJitterPercent);
        Assert.AreEqual(30, defaultOptions.BacklogSampleSeconds);

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:BatchSize"] = "0",
                ["Jobs:Worker:PollMilliseconds"] = "99",
                ["Jobs:Worker:MaxConcurrency"] = "17",
                ["Jobs:Worker:MaxAttempts"] = "0",
                ["Jobs:Worker:RetryDelaySeconds"] = "0",
                ["Jobs:Worker:RetryBackoffMode"] = "random",
                ["Jobs:Worker:RetryMaxDelaySeconds"] = "0",
                ["Jobs:Worker:RetryJitterPercent"] = "51",
                ["Jobs:Worker:BacklogSampleSeconds"] = "4",
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
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:MaxConcurrency must be between 1 and 16.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:MaxAttempts must be between 1 and 10.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:RetryDelaySeconds must be between 1 and 86400.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:RetryBackoffMode must be 'fixed' or 'exponential'.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:RetryMaxDelaySeconds must be between 1 and 86400.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:RetryJitterPercent must be between 0 and 50.");
        CollectionAssert.Contains(
            exception.Failures.ToArray(),
            "Jobs:Worker:BacklogSampleSeconds must be between 5 and 3600.");

        using var invalidUpperBounds = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:MaxAttempts"] = "11",
                ["Jobs:Worker:RetryDelaySeconds"] = "86401",
                ["Jobs:Worker:BacklogSampleSeconds"] = "3601",
            });
        var upperBoundsException = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            invalidUpperBounds
                .GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>()
                .Validate);
        CollectionAssert.Contains(
            upperBoundsException.Failures.ToArray(),
            "Jobs:Worker:MaxAttempts must be between 1 and 10.");
        CollectionAssert.Contains(
            upperBoundsException.Failures.ToArray(),
            "Jobs:Worker:RetryDelaySeconds must be between 1 and 86400.");
        CollectionAssert.Contains(
            upperBoundsException.Failures.ToArray(),
            "Jobs:Worker:BacklogSampleSeconds must be between 5 and 3600.");

        using var exceedsBatch = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:BatchSize"] = "2",
                ["Jobs:Worker:MaxConcurrency"] = "3",
            });
        var exceedsBatchException = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            exceedsBatch
                .GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>()
                .Validate);
        CollectionAssert.Contains(
            exceedsBatchException.Failures.ToArray(),
            "Jobs:Worker:MaxConcurrency must not exceed BatchSize.");

        using var retryMaximumBelowBase = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Jobs:Worker:RetryDelaySeconds"] = "60",
                ["Jobs:Worker:RetryMaxDelaySeconds"] = "30",
            });
        var retryMaximumException = Assert.ThrowsExactly<
            Microsoft.Extensions.Options.OptionsValidationException>(
            retryMaximumBelowBase
                .GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>()
                .Validate);
        CollectionAssert.Contains(
            retryMaximumException.Failures.ToArray(),
            "Jobs:Worker:RetryMaxDelaySeconds must not be less than RetryDelaySeconds.");
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
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        new JobsModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
