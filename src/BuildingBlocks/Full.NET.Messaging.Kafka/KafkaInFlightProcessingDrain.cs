namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 在 Consumer Poll 循环退出前有界观察在途 Handler；超时后继续观察迟到故障，
/// 避免宿主关闭时遗留未观察任务。
/// </summary>
internal static class KafkaInFlightProcessingDrain
{
    public static async Task<bool> DrainAsync(
        Task processing,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(processing);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        try
        {
            await processing.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            _ = processing.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return false;
        }
        catch
        {
            // 任务已进入终态；读取异常即完成观察，Poll 路径仍传播原始退出原因。
            return true;
        }
    }
}
