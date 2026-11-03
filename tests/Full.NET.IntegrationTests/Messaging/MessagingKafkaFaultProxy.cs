using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>专用明文 Kafka 测试链路代理；按客户端阻断原始请求，不改写帧、Broker 响应或位点。</summary>
internal sealed class MessagingKafkaFaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<TcpClient, string> _clients = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Task _accept;
    private Gate? _gate;
    private ProduceGate? _produceGate;
    internal ushort UpstreamPort { get; set; }
    internal ushort Port { get; }
    internal bool Disconnected { get; private set; }

    internal MessagingKafkaFaultProxy(bool dockerReachable = false)
    {
        _listener = new(dockerReachable ? IPAddress.Any : IPAddress.Loopback, 0);
        _listener.Start(); Port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port; _accept = AcceptAsync();
    }

    internal Gate ArmOffsetCommit(string group)
    {
        var gate = new Gate(group);
        if (Interlocked.CompareExchange(ref _gate, gate, null) is not null) throw new InvalidOperationException("已有 OffsetCommit 故障。");
        return gate;
    }

    /// <summary>放行目标生产者首个Produce请求，阻断第二个完整请求，供真实部分投递故障测试使用。</summary>
    internal ProduceGate ArmSecondProduce(string clientId, string topic, byte[] topicId)
    {
        if (topicId.Length != 16) throw new ArgumentException("Kafka Topic ID 必须为16字节。", nameof(topicId));
        var gate = new ProduceGate(clientId, topic, topicId.ToArray());
        if (Interlocked.CompareExchange(ref _produceGate, gate, null) is not null) throw new InvalidOperationException("已有 Produce 故障。");
        return gate;
    }

    internal void SetDisconnected(bool disconnected)
    {
        Disconnected = disconnected;
        if (disconnected) foreach (var client in _clients.Keys) client.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (Disconnected) { client.Dispose(); continue; }
                // 小帧转发不能额外引入 Nagle/延迟 ACK；故障 Gate 仍按完整原帧执行。
                client.NoDelay = true;
                _connections.Add(ForwardAsync(client));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    private async Task ForwardAsync(TcpClient downstream)
    {
        using (downstream)
        using (var upstream = new TcpClient { NoDelay = true })
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            _clients[downstream] = "unknown";
            try
            {
                await upstream.ConnectAsync(IPAddress.Loopback, UpstreamPort, lifetime.Token);
                var request = RequestsAsync(downstream, upstream, lifetime.Token);
                var response = upstream.GetStream().CopyToAsync(downstream.GetStream(), lifetime.Token);
                await Task.WhenAny(request, response);
                lifetime.Cancel(); upstream.Dispose(); downstream.Dispose();
                try { await Task.WhenAll(request, response); }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { _clients.TryRemove(downstream, out _); }
        }
    }

    private async Task RequestsAsync(TcpClient downstream, TcpClient upstream, CancellationToken token)
    {
        var length = new byte[4];
        while (!token.IsCancellationRequested)
        {
            await downstream.GetStream().ReadExactlyAsync(length, token);
            var size = BinaryPrimitives.ReadInt32BigEndian(length);
            if (size is < 10 or > 32 * 1024 * 1024) throw new IOException("Kafka 测试帧长度超出预算。");
            // 一次转发完整线格式，避免帧头四字节单独写入时产生可测的小包等待；解析仍以原帧体为基准。
            var packet = new byte[4 + size]; length.CopyTo(packet, 0);
            var frame = packet.AsMemory(4); await downstream.GetStream().ReadExactlyAsync(frame, token);
            var clientLength = BinaryPrimitives.ReadInt16BigEndian(frame.Span.Slice(8, 2));
            var clientId = clientLength >= 0 && clientLength <= frame.Length - 10 ? Encoding.UTF8.GetString(frame.Span.Slice(10, clientLength)) : "unknown";
            _clients[downstream] = clientId;
            var gate = _gate;
            if (gate is not null && BinaryPrimitives.ReadInt16BigEndian(frame.Span) == 8
                && frame.Span.IndexOf(Encoding.UTF8.GetBytes(gate.Group)) >= 0
                && Interlocked.CompareExchange(ref _gate, null, gate) == gate)
            {
                gate.ClientId = clientId; gate.Reached.TrySetResult();
                await gate.Release.Task.WaitAsync(token);
                if (gate.Drop) return;
            }
            var produce = _produceGate;
            if (produce is not null && BinaryPrimitives.ReadInt16BigEndian(frame.Span) == 0 && clientId == produce.ClientId)
            {
                Interlocked.Increment(ref produce.ClientRequests);
                Volatile.Write(ref produce.ApiVersion, BinaryPrimitives.ReadInt16BigEndian(frame.Span.Slice(2, 2)));
                var version = BinaryPrimitives.ReadInt16BigEndian(frame.Span.Slice(2, 2));
                var target = version == 13 ? ContainsTopicProduceV13(frame.Span, clientLength, produce.TopicId)
                    : version < 13 && frame.Span.IndexOf(Encoding.UTF8.GetBytes(produce.Topic)) >= 0;
                if (target && Interlocked.Increment(ref produce.Seen) == 2)
                {
                    produce.Reached.TrySetResult();
                    await produce.Release.Task.WaitAsync(token);
                }
            }
            await upstream.GetStream().WriteAsync(packet, token);
        }
    }

    // v13使用Topic UUID；完整检查所有Topic/Partition边界，未知扩展字段不得猜测并拦截。
    private static bool ContainsTopicProduceV13(ReadOnlySpan<byte> frame, int clientLength, ReadOnlySpan<byte> topicId)
    {
        var offset = 10 + clientLength;
        if (clientLength < 0 || offset < 10 || offset >= frame.Length || frame[offset++] != 0) return false;
        if (!ReadUnsigned(frame, ref offset, out var transactionalLength)) return false;
        var stringLength = transactionalLength == 0 ? 0 : transactionalLength - 1;
        if (stringLength > (uint)(frame.Length - offset)) return false;
        offset += (int)stringLength;
        if (frame.Length - offset < 6) return false;
        offset += 6;
        if (!ReadUnsigned(frame, ref offset, out var topicCount) || topicCount is < 2 or > 1025) return false;
        var matched = false;
        for (var topic = 1u; topic < topicCount; topic++)
        {
            if (frame.Length - offset < 16) return false;
            matched |= frame.Slice(offset, 16).SequenceEqual(topicId);
            offset += 16;
            if (!ReadUnsigned(frame, ref offset, out var partitions) || partitions is < 2 or > 1025) return false;
            for (var partition = 1u; partition < partitions; partition++)
            {
                if (frame.Length - offset < 4) return false;
                offset += 4;
                if (!ReadUnsigned(frame, ref offset, out var records)) return false;
                var recordBytes = records == 0 ? 0u : records - 1;
                if (recordBytes > (uint)(frame.Length - offset)) return false;
                offset += (int)recordBytes;
                if (!ReadUnsigned(frame, ref offset, out var partitionTags) || partitionTags != 0) return false;
            }
            if (!ReadUnsigned(frame, ref offset, out var topicTags) || topicTags != 0) return false;
        }
        return ReadUnsigned(frame, ref offset, out var requestTags) && requestTags == 0 && offset == frame.Length && matched;
    }

    private static bool ReadUnsigned(ReadOnlySpan<byte> frame, ref int offset, out uint value)
    {
        value = 0;
        for (var shift = 0; shift <= 28 && offset < frame.Length; shift += 7)
        {
            var b = frame[offset++];
            if (shift == 28 && (b & 0xf0) != 0) return false;
            value |= (uint)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _gate?.Release.TrySetResult(); _produceGate?.Release.TrySetResult(); _stop.Cancel(); _listener.Stop();
        foreach (var client in _clients.Keys) client.Dispose();
        await _accept; await Task.WhenAll(_connections); _stop.Dispose();
    }

    internal sealed class Gate(string group)
    {
        internal string Group { get; } = group;
        internal string? ClientId { get; set; }
        internal bool Drop { get; set; }
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class ProduceGate(string clientId, string topic, byte[] topicId)
    {
        internal string ClientId { get; } = clientId;
        internal string Topic { get; } = topic;
        internal byte[] TopicId { get; } = topicId;
        internal int Seen;
        internal int ClientRequests;
        internal int ApiVersion;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
