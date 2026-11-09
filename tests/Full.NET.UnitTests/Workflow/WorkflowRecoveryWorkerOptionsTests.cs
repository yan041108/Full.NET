using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证 Recovery Worker 选项边界与满批立即续跑。</summary>
[TestClass]
public sealed class WorkflowRecoveryWorkerOptionsTests
{
    /// <summary>默认选项必须通过启动校验。</summary>
    [TestMethod]
    public void Validator_accepts_default_options()
    {
        var result = new WorkflowRecoveryWorkerOptionsValidator()
            .Validate(null, new WorkflowRecoveryWorkerOptions());
        Assert.IsFalse(result.Failed);
    }

    /// <summary>越界批大小、租约窗口和退避模式必须全部失败关闭。</summary>
    [TestMethod]
    public void Validator_rejects_out_of_range_batch_lease_and_backoff()
    {
        var invalid = new WorkflowRecoveryWorkerOptionsValidator().Validate(
            null,
            new WorkflowRecoveryWorkerOptions
            {
                BatchSize = 0,
                PollMilliseconds = 10,
                LeaseSeconds = 1,
                RenewWhenRemainingSeconds = 30,
                MaxAttempts = 0,
                RetryDelaySeconds = 0,
                RetryBackoffMode = "linear",
                RetryMaxDelaySeconds = 0,
            });

        Assert.IsTrue(invalid.Failed);
        StringAssert.Contains(invalid.FailureMessage, "BatchSize");
        StringAssert.Contains(invalid.FailureMessage, "PollMilliseconds");
        StringAssert.Contains(invalid.FailureMessage, "LeaseSeconds");
        StringAssert.Contains(invalid.FailureMessage, "RetryBackoffMode");
    }

    /// <summary>AddBackgroundServices 必须绑定默认值并拒绝不安全上界。</summary>
    [TestMethod]
    public void AddBackgroundServices_binds_defaults_and_rejects_unsafe_bounds()
    {
        using var defaults = CreateProvider(new Dictionary<string, string?>());
        var options = defaults.GetRequiredService<IOptions<WorkflowRecoveryWorkerOptions>>().Value;
        Assert.AreEqual(10, options.BatchSize);
        Assert.AreEqual(1000, options.PollMilliseconds);
        Assert.AreEqual(120, options.LeaseSeconds);
        Assert.AreEqual(8, options.MaxAttempts);

        using var invalid = CreateProvider(new Dictionary<string, string?>
        {
            ["Workflow:RecoveryWorker:BatchSize"] = "0",
        });
        var validator = invalid.GetRequiredService<IStartupValidator>();
        Assert.ThrowsExactly<OptionsValidationException>(validator.Validate);
    }

    private static ServiceProvider CreateProvider(IReadOnlyDictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(configuration);
        new WorkflowModule().AddBackgroundServices(services, configuration);
        return services.BuildServiceProvider();
    }
}

/// <summary>验证未满批才等待 Poll，满批立即再跑。</summary>
[TestClass]
public sealed class WorkflowRecoveryHostedProcessorTests
{
    [TestMethod]
    public async Task Capacity_rejection_waits_before_polling_and_keeps_unexpected_errors_visible()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = budget.Token;
        var attempts = 0;
        long firstAttempt = 0;
        var nextAttempt = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopes = Substitute.For<IServiceScopeFactory>();
        var unexpected = new InvalidOperationException("受控轮询故障");
        scopes.CreateScope().Returns(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt = Stopwatch.GetTimestamp();
                throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected, TimeSpan.FromMilliseconds(400));
            }
            nextAttempt.TrySetResult(Stopwatch.GetElapsedTime(firstAttempt));
            throw unexpected;
        });
        var logger = new RecoveryLog();
        using var processor = new WorkflowRecoveryHostedProcessor(
            scopes, new SystemClock(), Options.Create(new WorkflowRecoveryWorkerOptions { PollMilliseconds = 100 }), logger);
        await processor.StartAsync(token);
        try
        {
            var capacity = await logger.First.Task.WaitAsync(token);
            Assert.AreEqual(LogLevel.Warning, capacity.Level);
            StringAssert.Contains(capacity.Message, nameof(ServiceCapacityFailureKind.Rejected));
            Assert.IsNull(capacity.Exception);
            Assert.IsTrue(await nextAttempt.Task.WaitAsync(token) >= TimeSpan.FromMilliseconds(390));
            var next = await logger.Next.Task.WaitAsync(token);
            Assert.AreEqual(LogLevel.Error, next.Level);
            Assert.AreSame(unexpected, next.Exception);
        }
        finally
        {
            await processor.StopAsync(token);
        }
    }

    /// <summary>满批延迟必须为零，避免积压被 Poll 拉长。</summary>
    [TestMethod]
    public void GetDelayAfterBatch_is_zero_when_batch_is_full()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var processor = new WorkflowRecoveryHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Full.NET.Abstractions.Time.SystemClock(),
            Options.Create(new WorkflowRecoveryWorkerOptions
            {
                BatchSize = 7,
                PollMilliseconds = 250,
            }),
            NullLogger<WorkflowRecoveryHostedProcessor>.Instance);

        Assert.AreEqual(TimeSpan.Zero, processor.GetDelayAfterBatch(7));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), processor.GetDelayAfterBatch(6));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), processor.GetDelayAfterBatch(0));
    }

    private sealed class RecoveryLog : ILogger<WorkflowRecoveryHostedProcessor>
    {
        internal TaskCompletionSource<LogEntry> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<LogEntry> Next { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = new LogEntry(logLevel, formatter(state, exception), exception);
            if (!First.TrySetResult(entry)) Next.TrySetResult(entry);
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
