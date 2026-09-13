using Full.NET.Modules.Ai.Streaming;

namespace Full.NET.UnitTests.Ai;

/// <summary>已取消的传输不得在检查令牌前写出事件正文。</summary>
[TestClass]
public sealed class AiChatSseWriterTests
{
    [TestMethod]
    public async Task Cancelled_event_does_not_write_bytes_async()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AiChatSseWriter.WriteDeltaAsync(stream, "hello", cancellation.Token));
        Assert.AreEqual(0L, stream.Length);
    }
}
