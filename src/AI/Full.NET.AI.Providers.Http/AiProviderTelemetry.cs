using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.AI.Providers.Http;

/// <summary>只观察 HTTP 传输尝试；不读取请求、响应或异常正文，不代替业务回执和收费账本。</summary>
internal static class AiProviderTelemetry
{
    internal const string Name = "Full.NET.Ai.Providers";
    private static readonly Lazy<ActivitySource?> Source = new(CreateSource);
    private static readonly Lazy<Instruments?> Metrics = new(CreateInstruments);

    internal static Observation Start(string provider, string operation) => new(
        provider is "openai_compatible" or "ollama" or "azure_openai" ? provider : "other",
        operation is "chat" or "embedding" or "connectivity" ? operation : "other");

    private static ActivitySource? CreateSource()
    {
        // 诊断监听器由宿主安装，初始化失败也不能改变网络策略或收费请求的行为。
        try { return new ActivitySource(Name); }
        catch (Exception) { return null; }
    }

    private static Instruments? CreateInstruments()
    {
        Meter? meter = null;
        try
        {
            meter = new Meter(Name);
            return new(meter, meter.CreateCounter<long>("fullnet.ai.provider.http.requests", "{request}"),
                meter.CreateHistogram<double>("fullnet.ai.provider.http.duration", "ms"));
        }
        catch (Exception)
        {
            try { meter?.Dispose(); } catch (Exception) { }
            return null;
        }
    }

    private sealed record Instruments(Meter Meter, Counter<long> Requests, Histogram<double> Duration);

    /// <summary>每个尝试只记录一次；诊断回调故障不能替换原响应、取消或策略异常。</summary>
    internal sealed class Observation : IDisposable
    {
        private readonly string provider;
        private readonly string operation;
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly Activity? parent = Activity.Current;
        private readonly Activity? activity;
        private int completed;
        internal string Outcome { get; set; } = "failed";

        internal Observation(string provider, string operation)
        {
            this.provider = provider; this.operation = operation;
            Activity? candidate = null;
            try
            {
                candidate = Source.Value?.CreateActivity("ai.provider.http", ActivityKind.Client);
                candidate?.SetTag("ai.provider", provider);
                candidate?.SetTag("ai.operation", operation);
                candidate?.Start();
                activity = candidate;
            }
            catch (Exception)
            {
                try { candidate?.Dispose(); } catch (Exception) { }
                RestoreParent(parent);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var outcome = Outcome is "succeeded" or "redirected" or "rejected" or "rate_limited" or "server_error"
                or "invalid_status" or "cancelled" or "policy_rejected" or "network_error" ? Outcome : "failed";
            TagList tags = new() { { "ai.provider", provider }, { "ai.operation", operation }, { "ai.outcome", outcome } };
            try
            {
                Instruments? instruments = null;
                // 发布回调可能递归进入观测；Lazy 的递归异常不能覆盖原请求结果。
                try { instruments = Metrics.Value; } catch (Exception) { }
                // 独立捕获两个同步测量回调，单个故障不阻止另一个指标或 Trace 收尾。
                try { instruments?.Requests.Add(1, tags); } catch (Exception) { }
                try { instruments?.Duration.Record(elapsed, tags); } catch (Exception) { }
                activity?.SetTag("ai.outcome", outcome);
                activity?.SetStatus(outcome == "succeeded" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            }
            finally
            {
                try { activity?.Dispose(); } catch (Exception) { }
                RestoreParent(parent);
            }
        }

        private static void RestoreParent(Activity? parent)
        {
            // CurrentChanged 在写入 Current 后同步执行；避免重复通知，回调失败也保留已恢复上下文。
            try { if (!ReferenceEquals(Activity.Current, parent)) Activity.Current = parent; }
            catch (Exception) { }
        }
    }
}
