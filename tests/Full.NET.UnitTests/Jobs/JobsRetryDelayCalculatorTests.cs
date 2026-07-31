using Full.NET.Modules.Jobs.Execution;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsRetryDelayCalculatorTests
{
    [TestMethod]
    public void CalculateSeconds_FixedModePreservesConfiguredDelay()
    {
        var options = CreateOptions(
            retryDelaySeconds: 30,
            retryBackoffMode: "fixed",
            retryMaxDelaySeconds: 86400,
            retryJitterPercent: 0);

        Assert.AreEqual(
            30,
            JobsRetryDelayCalculator.CalculateSeconds(options, 1, 0.0));
        Assert.AreEqual(
            30,
            JobsRetryDelayCalculator.CalculateSeconds(options, 9, 1.0));
    }

    [TestMethod]
    public void CalculateSeconds_ExponentialModeGrowsAndCapsWithoutOverflow()
    {
        var options = CreateOptions(
            retryDelaySeconds: 30,
            retryBackoffMode: "exponential",
            retryMaxDelaySeconds: 100,
            retryJitterPercent: 0);

        Assert.AreEqual(
            30,
            JobsRetryDelayCalculator.CalculateSeconds(options, 1, 0.5));
        Assert.AreEqual(
            60,
            JobsRetryDelayCalculator.CalculateSeconds(options, 2, 0.5));
        Assert.AreEqual(
            100,
            JobsRetryDelayCalculator.CalculateSeconds(options, 3, 0.5));
        Assert.AreEqual(
            100,
            JobsRetryDelayCalculator.CalculateSeconds(options, 10, 0.5));
    }

    [TestMethod]
    public void CalculateSeconds_JitterIsSymmetricAndRemainsBounded()
    {
        var options = CreateOptions(
            retryDelaySeconds: 100,
            retryBackoffMode: "fixed",
            retryMaxDelaySeconds: 110,
            retryJitterPercent: 20);

        Assert.AreEqual(
            80,
            JobsRetryDelayCalculator.CalculateSeconds(options, 1, 0.0));
        Assert.AreEqual(
            100,
            JobsRetryDelayCalculator.CalculateSeconds(options, 1, 0.5));
        Assert.AreEqual(
            110,
            JobsRetryDelayCalculator.CalculateSeconds(options, 1, 1.0));

        var minimumOptions = CreateOptions(
            retryDelaySeconds: 1,
            retryBackoffMode: "fixed",
            retryMaxDelaySeconds: 1,
            retryJitterPercent: 20);
        Assert.AreEqual(
            1,
            JobsRetryDelayCalculator.CalculateSeconds(
                minimumOptions,
                1,
                0.0));
    }

    private static JobsWorkerOptions CreateOptions(
        int retryDelaySeconds,
        string retryBackoffMode,
        int retryMaxDelaySeconds,
        int retryJitterPercent)
    {
        return new JobsWorkerOptions
        {
            RetryDelaySeconds = retryDelaySeconds,
            RetryBackoffMode = retryBackoffMode,
            RetryMaxDelaySeconds = retryMaxDelaySeconds,
            RetryJitterPercent = retryJitterPercent,
        };
    }
}
