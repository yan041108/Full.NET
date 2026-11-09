namespace Full.NET.Modules.Ai;

/// <summary>为 AI Worker 的数据库准入重试保留等待下限，并错开实例。</summary>
internal static class AiWorkerAdmissionBackoff
{
    internal static TimeSpan GetDelay(TimeSpan pollingDelay, TimeSpan retryAfter)
    {
        var minimum = retryAfter > pollingDelay ? retryAfter : pollingDelay;
        var jitterLimit = Math.Min(pollingDelay.TotalMilliseconds / 4, 1000);
        return minimum + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * jitterLimit);
    }
}
