using System.Collections.Concurrent;
using System.Diagnostics;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>仅显式诊断运行记录有界语句身份和行数，不记录 SQL、正文、凭据或参数值。</summary>
internal static class FaqLifecycleQueryTrace
{
    private static readonly ConcurrentQueue<string> Entries = new();
    internal static bool Enabled => Environment.GetEnvironmentVariable("FULLNET_K04_FAQ_TRACE") == "1";
    internal static void Attach(IServiceCollection services)
    {
        if (!Enabled) return;
        var query = services.Last(d => d.ServiceType == typeof(IQueryExecutor)); services.Remove(query);
        services.Add(new ServiceDescriptor(typeof(IQueryExecutor), sp => new Reads((IQueryExecutor)Create(query, sp)), query.Lifetime));
        var command = services.Last(d => d.ServiceType == typeof(ICommandExecutor)); services.Remove(command);
        services.Add(new ServiceDescriptor(typeof(ICommandExecutor), sp => new Writes((ICommandExecutor)Create(command, sp)), command.Lifetime));
    }
    private static object Create(ServiceDescriptor descriptor, IServiceProvider services) => descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(services) ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);
    private static void Add(SqlStatement sql, object? parameters, string outcome, long started)
    {
        var task = parameters is IReadOnlyDictionary<string, object?> p && p.TryGetValue("TaskId", out var id) && id is Guid value ? value.ToString("N") : "-";
        Entries.Enqueue($"{DateTimeOffset.UtcNow:O} {task} {sql.Name} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} {outcome}");
        while (Entries.Count > 4000) Entries.TryDequeue(out _);
    }
    internal static void Dump(Guid task)
    {
        if (!Enabled) return;
        var marker = task.ToString("N");
        Console.WriteLine("FAQ bounded query trace: " + marker);
        foreach (var line in Entries.Where(line => line.Contains(marker, StringComparison.Ordinal)).TakeLast(180)) Console.WriteLine(line);
    }
    internal static void Save()
    {
        if (Enabled && Environment.GetEnvironmentVariable("FULLNET_K04_FAQ_TRACE_OUTPUT") is { Length: > 0 } output)
            File.WriteAllLines(output, Entries);
    }
    private sealed class Reads(IQueryExecutor inner) : IQueryExecutor
    {
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var row = await inner.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
                var present = row is not null && (row is not Guid id || id != Guid.Empty);
                var suffix = row is AiKnowledgeIndexDispatchRecord dispatch
                    ? $" docVersion={dispatch.DocumentVersion} kbVersion={dispatch.KnowledgeBaseVersion} leaseUntil={dispatch.LeaseExpiresAtUtc:O} faq={dispatch.FaqRevisionId.HasValue}"
                    : "";
                Add(sql, parameters, $"{typeof(T).Name} present={present}{suffix}",started); return row;
            }
            catch (Exception error) { Add(sql, parameters, $"error={error.GetType().Name}",started); throw; }
        }
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var started = Stopwatch.GetTimestamp();
            try { var rows = await inner.QueryAsync<T>(sql, parameters, cancellationToken); Add(sql, parameters, $"{typeof(T).Name} count={rows.Count}",started); return rows; }
            catch (Exception error) { Add(sql, parameters, $"error={error.GetType().Name}",started); throw; }
        }
    }
    private sealed class Writes(ICommandExecutor inner) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var started = Stopwatch.GetTimestamp();
            try { var count = await inner.ExecuteAsync(sql, parameters, cancellationToken); Add(sql, parameters, $"affected={count}",started); return count; }
            catch (DataCommandException error) { Add(sql, parameters, $"error={error.Kind}",started); throw; }
            catch (Exception error) { Add(sql, parameters, $"error={error.GetType().Name}",started); throw; }
        }
    }
}
