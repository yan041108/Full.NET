using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Full.NET.AiRetrieval.Probe;

/// <summary>使用静态 REST/JSON 而非 SDK，独立证明候选协议的 Native AOT 可达行为。</summary>
internal static class ProbeQdrant
{
    internal static async Task<(string Version, double Recall)> RunAsync(ProbeChunk[] chunks,
        ProbeCase[] cases, List<ProbeObservation> observations)
    {
        using var client = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("PROBE_QDRANT")!), Timeout = TimeSpan.FromSeconds(15) };
        using var root = await SendAsync(client, HttpMethod.Get, "", null);
        var version = root.RootElement.GetProperty("version").GetString()!;
        var collection = "fullnet_r03_" + Guid.NewGuid().ToString("N");
        async Task CreateCollectionAsync()
        {
            using (await SendAsync(client, HttpMethod.Put, $"collections/{collection}", new JsonObject
            {
                ["vectors"] = new JsonObject { ["size"] = ProbeCorpus.Dimension, ["distance"] = "Cosine" }
            })) { }
            foreach (var key in new[] { "scope", "source", "model" })
            using (await SendAsync(client, HttpMethod.Put, $"collections/{collection}/index?wait=true",
                new JsonObject { ["field_name"] = key, ["field_schema"] = "keyword" })) { }
            using (await SendAsync(client, HttpMethod.Put, $"collections/{collection}/index?wait=true",
                new JsonObject { ["field_name"] = "generation", ["field_schema"] = "integer" })) { }
        }
        await CreateCollectionAsync();

        async Task UpsertAsync(int generation)
        {
            var points = new JsonArray();
            for (var i = 0; i < chunks.Length; i++) points.Add((JsonNode)new JsonObject
            {
                ["id"] = i + 1,
                ["vector"] = Floats(ProbeVectorSearch.Embed(chunks[i].Text)),
                ["payload"] = new JsonObject { ["chunk"] = chunks[i].ChunkId, ["scope"] = chunks[i].TenantScope,
                    ["source"] = chunks[i].SourceVersion, ["model"] = ProbeCorpus.Model, ["generation"] = generation }
            });
            using var write = await SendAsync(client, HttpMethod.Put, $"collections/{collection}/points?wait=true", new JsonObject { ["points"] = points });
        }
        await UpsertAsync(1);
        observations.Add(new("write", true, "synthetic vectors and payload indexes persisted"));
        async Task<string[]> QueryAsync(ProbeCase testCase, int generation = 1)
        {
            var must = new JsonArray(
                Match("scope", new JsonObject { ["value"] = testCase.TenantScope }),
                Match("source", new JsonObject { ["any"] = new JsonArray(testCase.Allowed.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) }),
                Match("model", new JsonObject { ["value"] = ProbeCorpus.Model }),
                Match("generation", new JsonObject { ["value"] = generation }));
            using var query = await SendAsync(client, HttpMethod.Post, $"collections/{collection}/points/query",
                new JsonObject { ["query"] = Floats(ProbeVectorSearch.Embed(testCase.Question)),
                    ["filter"] = new JsonObject { ["must"] = must }, ["limit"] = 5, ["with_payload"] = true,
                    ["params"] = new JsonObject { ["exact"] = true } });
            return query.RootElement.GetProperty("result").GetProperty("points").EnumerateArray()
                .Select(x => x.GetProperty("payload").GetProperty("chunk").GetString()!).ToArray();
        }
        var recall = await ProbeCorpus.CheckRetrievalAsync(chunks, cases, c => QueryAsync(c), observations);
        await ProbeCorpus.CheckBroadRankingAsync(chunks, cases, c => QueryAsync(c), observations);
        var number = cases.Single(x => x.CaseId == "exact-number");
        observations.Add(new("identifier", (await QueryAsync(number)).Contains("ticket-1", StringComparer.Ordinal), "synthetic exact identifier retrieved"));
        var id = Array.FindIndex(chunks, x => x.ChunkId == "ticket-1") + 1;
        async Task DeletePointAsync()
        {
            using var delete = await SendAsync(client, HttpMethod.Post, $"collections/{collection}/points/delete?wait=true",
                new JsonObject { ["points"] = new JsonArray(JsonValue.Create(id)) });
        }
        await DeletePointAsync();
        observations.Add(new("delete", (await QueryAsync(number)).Length == 0, "deleted point absent"));
        await DeletePointAsync();
        observations.Add(new("delete-idempotent", (await QueryAsync(number)).Length == 0, "repeat delete succeeds"));
        // 删除整个派生集合后按权威合成数据重建；不能以旧索引作为恢复的唯一来源。
        using (await SendAsync(client, HttpMethod.Delete, $"collections/{collection}", null)) { }
        await CreateCollectionAsync();
        await UpsertAsync(2);
        observations.Add(new("rebuild", (await QueryAsync(number, 2)).Contains("ticket-1", StringComparer.Ordinal)
            && (await QueryAsync(number, 1)).Length == 0, "deleted derived index rebuilt under new generation"));
        await CheckSlowTransportAsync(observations, false);
        await CheckSlowTransportAsync(observations, true);
        await CheckSlowTransportAsync(observations, false, slowBody: true);
        await CheckSlowTransportAsync(observations, true, slowBody: true);
        using (await SendAsync(client, HttpMethod.Get, "", null)) { }
        observations.Add(new("connection-recovery", true, "real server remains reachable after transport cancellation"));
        return (version, recall);
    }

    private static JsonObject Match(string key, JsonObject match) => new() { ["key"] = key, ["match"] = match };
    private static JsonArray Floats(float[] vector) => new(vector.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());

    private static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string uri, JsonNode? body,
        CancellationToken cancellationToken = default, Action? bodyReadStarted = null)
    {
        // ResponseHeadersRead 后 HttpClient.Timeout 不覆盖正文，必须另设贯穿整个响应的截止时间。
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout);
        cancellationToken = deadline.Token;
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        bodyReadStarted?.Invoke();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + read > 256 * 1024) throw new InvalidDataException("实验响应超过上限。");
            output.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(output.ToArray());
    }

    private static async Task CheckSlowTransportAsync(List<ProbeObservation> observations, bool cancel, bool slowBody = false)
    {
        // 真实 Qdrant 查询太快，慢传输夹具仅证明同一 HTTP 适配器的在途取消/截止时间。
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var accepted = listener.AcceptTcpClientAsync();
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{endpoint.Port}/"),
            Timeout = cancel ? TimeSpan.FromSeconds(5) : slowBody ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(200)
        };
        using var cancellation = new CancellationTokenSource();
        var bodyReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var elapsed = Stopwatch.StartNew();
        var call = SendAsync(client, HttpMethod.Get, "", null, cancellation.Token, () => bodyReadStarted.TrySetResult());
        using var socket = await accepted;
        if (slowBody)
        {
            await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 20\r\nConnection: close\r\n\r\n"));
            // 等待客户端完成响应头并进入正文读取，不能将 header 取消误记为 body 取消。
            await bodyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        if (cancel) cancellation.Cancel();
        var scenario = (cancel ? "cancel" : "timeout") + (slowBody ? "-body" : "-headers");
        try { using var result = await call; observations.Add(new(scenario, false, "slow transport unexpectedly completed")); }
        catch (OperationCanceledException) { observations.Add(new(scenario,
            elapsed.Elapsed < TimeSpan.FromSeconds(3) && cancellation.IsCancellationRequested == cancel
                && (!slowBody || bodyReadStarted.Task.IsCompletedSuccessfully),
            "bounded in-flight HTTP transport; controlled slow peer; body scenarios await client read boundary")); }
    }
}
