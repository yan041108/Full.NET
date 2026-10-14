using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>通过真实清理和续租入口验证日志脱敏与业务失败隔离。</summary>
[TestClass]
public sealed class AiFailureLogBoundaryTests
{
    [TestMethod]
    [DataRow(0, false)] [DataRow(0, true)]
    [DataRow(1, false)] [DataRow(1, true)]
    [DataRow(2, false)] [DataRow(2, true)]
    public async Task Failure_log_worker_continues_next_iteration_with_safe_category_when_logger_fails(int worker, bool loggerThrows)
    {
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 2) second.TrySetResult();
            throw new SecretProviderFailure();
        });
        var runLog = new FailureCaptureLogger<AiAgentRunHostedProcessor> { ThrowOnLog = loggerThrows };
        var indexLog = new FailureCaptureLogger<AiKnowledgeIndexHostedProcessor> { ThrowOnLog = loggerThrows };
        var cleanupLog = new FailureCaptureLogger<AiKnowledgeCleanupHostedProcessor> { ThrowOnLog = loggerThrows };
        using BackgroundService hosted = worker switch
        {
            0 => new AiAgentRunHostedProcessor(scopes, Options.Create(new AiAgentRuntimeOptions { PollMilliseconds = 10 }), runLog),
            1 => new AiKnowledgeIndexHostedProcessor(scopes, Options.Create(new AiKnowledgeIndexWorkerOptions { Enabled = true, PollMilliseconds = 10 }), indexLog),
            _ => new AiKnowledgeCleanupHostedProcessor(scopes, Options.Create(new AiKnowledgeCleanupWorkerOptions { Enabled = true, PollMilliseconds = 10 }), cleanupLog),
        };
        await hosted.StartAsync(default);
        try { await second.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await hosted.StopAsync(deadline.Token);
        }
        var entries = worker == 0 ? runLog.Entries : worker == 1 ? indexLog.Entries : cleanupLog.Entries;
        Assert.IsTrue(attempts >= 2);
        Assert.IsTrue(entries.Count >= 1);
        foreach (var entry in entries)
        {
            Assert.IsNull(entry.Exception);
            Assert.IsFalse(entry.Text.Contains(nameof(SecretProviderFailure), StringComparison.Ordinal));
            Assert.IsFalse(entry.Text.Contains("secret-prompt", StringComparison.Ordinal));
            Assert.AreEqual("other", entry.State["FailureCategory"]);
        }
    }

    private sealed class SecretProviderFailure() : Exception("secret-prompt");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failure_log_renewal_failure_cancels_generation_without_exposing_exception(bool loggerThrows)
    {
        var commands = Substitute.For<ICommandExecutor>();
        var failure = new InvalidOperationException("secret-provider-token-and-prompt");
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<int>(failure));
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantContextWriter, CurrentTenantAccessor>();
        services.AddScoped(_ => commands);
        await using var provider = services.BuildServiceProvider();
        var logger = new FailureCaptureLogger<AiChatGenerationLeaseMonitor> { ThrowOnLog = loggerThrows };
        var clock = Substitute.For<IClock>(); var now = DateTimeOffset.UtcNow; clock.UtcNow.Returns(now);
        var monitor = new AiChatGenerationLeaseMonitor(provider.GetRequiredService<IServiceScopeFactory>(), clock, logger);
        using var request = new CancellationTokenSource();
        await monitor.WatchAsync(new AiChatScope(null), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            now.AddMinutes(1), request, default);
        Assert.IsTrue(request.IsCancellationRequested);
        Assert.HasCount(1, logger.Entries);
        Assert.IsNull(logger.Entries[0].Exception);
        Assert.IsFalse(logger.Entries[0].Text.Contains(failure.Message, StringComparison.Ordinal));
        await commands.Received(1).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failure_log_cleanup_retains_false_result_and_clears_tenant_when_logger_fails(bool loggerThrows)
    {
        var services = new ServiceCollection();
        var tenant = new CurrentTenantAccessor();
        services.AddScoped<ICurrentTenantContextWriter>(_ => tenant);
        await using var provider = services.BuildServiceProvider();
        var logger = new FailureCaptureLogger<AiChatCleanupScope> { ThrowOnLog = loggerThrows };
        var cleanup = new AiChatCleanupScope(provider.GetRequiredService<IServiceScopeFactory>(), logger);
        Assert.IsFalse(await cleanup.RunAsync(null, (_, _) => Task.FromException(new InvalidOperationException("secret-sql"))));
        Assert.IsFalse(tenant.IsHost);
        Assert.HasCount(1, logger.Entries);
        Assert.IsNull(logger.Entries[0].Exception);
        Assert.IsFalse(logger.Entries[0].Text.Contains("secret-sql", StringComparison.Ordinal));
    }
}

internal sealed record FailureLogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Text, Dictionary<string, object?> State);

internal sealed class FailureCaptureLogger<T> : ILogger<T>
{
    internal List<FailureLogEntry> Entries { get; } = [];
    internal List<Dictionary<string, object?>> Scopes { get; } = [];
    internal bool ThrowOnLog { get; init; }
    internal bool ThrowOnScope { get; init; }
    internal bool ThrowOnScopeDispose { get; init; }
    internal bool ThrowOnEnabled { get; init; }
    internal bool Enabled { get; init; } = true;
    internal Action? BeforeLog { get; init; }
    internal Exception? CaptureFailure { get; private set; }
    public bool IsEnabled(LogLevel logLevel) => ThrowOnEnabled ? throw new InvalidOperationException("logger-enabled") : Enabled;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (ThrowOnScope) throw new InvalidOperationException("logger-scope");
        try { Scopes.Add(((IEnumerable<KeyValuePair<string, object?>>)(object)state).ToDictionary()); }
        catch (Exception failure) { CaptureFailure = failure; throw; }
        return new Scope(ThrowOnScopeDispose);
    }
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        BeforeLog?.Invoke();
        try
        {
            Entries.Add(new(logLevel, eventId, exception, formatter(state, exception),
                ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary()));
        }
        catch (Exception failure) { CaptureFailure = failure; throw; }
        if (ThrowOnLog) throw new InvalidOperationException("logger-sink");
    }
    private sealed class Scope(bool throws) : IDisposable
    {
        public void Dispose() { if (throws) throw new InvalidOperationException("logger-dispose"); }
    }
}
