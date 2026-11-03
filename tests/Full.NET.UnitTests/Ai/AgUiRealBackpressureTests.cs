using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>使用真实 Kestrel 与不读取的 TCP 接收端核对 SSE 写入期限。</summary>
[TestClass]
public sealed class AgUiRealBackpressureTests
{
    [TestMethod]
    public async Task Stalled_tcp_receiver_releases_stream_on_write_deadline()
    {
        var reader = new ProtocolPayloadReader();
        var finished = new TaskCompletionSource<(bool Success, bool Value, double Seconds)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxResponseBufferSize = 16 * 1024;
        });
        await using var app = builder.Build();
        app.MapGet("/stream", async context =>
        {
            var watch = Stopwatch.StartNew();
            var result = await new AgUiStreamService(reader).StreamOwnedRunAsync(reader.RunId, "host", Guid.CreateVersion7(), 0, context);
            finished.TrySetResult((result.IsSuccess, result.IsSuccess && result.Value, watch.Elapsed.TotalSeconds));
        });
        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ReceiveBufferSize = 1024 };
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await socket.ConnectAsync(IPAddress.Loopback, address.Port, deadline.Token);
            await socket.SendAsync(Encoding.ASCII.GetBytes("GET /stream HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), SocketFlags.None, deadline.Token);
            // 不读取任何响应，确保受限接收窗口最终向 Kestrel 传播实际 TCP 背压。
            var observed = await finished.Task.WaitAsync(deadline.Token);
            Assert.IsTrue(observed.Success);
            Assert.IsFalse(observed.Value, "只能由写入截止结束，不能假称历史正常排空。");
            Assert.IsTrue(observed.Seconds >= 8 && observed.Seconds < 30, $"Observed seconds: {observed.Seconds:F3}");
            Assert.IsTrue(reader.Pages > 0 && reader.Pages < 20, $"Observed pages: {reader.Pages}");
            Console.WriteLine($"TCP backpressure: deadlineSeconds={observed.Seconds:F3}, pages={reader.Pages}, payloadEvents=2000");
        }
        finally { await app.StopAsync(); }
    }

    // 这是有界协议载荷夹具，不冒充数据库、真实模型或容量测试。
    private sealed class ProtocolPayloadReader : IAgentRunAgUiReader
    {
        private const int Total = 2000;
        private static readonly string Payload = "{\"data\":\"" + new string('x', 60000) + "\"}";
        internal readonly Guid RunId = Guid.CreateVersion7();
        private readonly Guid sessionId = Guid.CreateVersion7();
        internal int Pages;
        public ValueTask<bool> CanReadAsync(Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<AgentRunAgUiSnapshot?> TryGetOwnedSnapshotAsync(Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AgentRunAgUiSnapshot?>(new(RunId, sessionId, "completed", "fullnet-single-text-v1", Total));
        public ValueTask<IReadOnlyList<AgentRunPersistedEvent>> ListEventsAfterAsync(Guid runId, long afterSequence, int limit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Pages);
            var count = (int)Math.Min(limit, Total - afterSequence);
            return ValueTask.FromResult<IReadOnlyList<AgentRunPersistedEvent>>(
                Enumerable.Range(1, count).Select(offset => new AgentRunPersistedEvent(afterSequence + offset, "run.protocol_fixture", 1, Payload, DateTimeOffset.UtcNow)).ToArray());
        }
        public ValueTask<AgentRunAgUiProgress?> TryGetProgressAsync(Guid runId, string scopeKey, CancellationToken cancellationToken = default) => ValueTask.FromResult<AgentRunAgUiProgress?>(null);
    }
}
