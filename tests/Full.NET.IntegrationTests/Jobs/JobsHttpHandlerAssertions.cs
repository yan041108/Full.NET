using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>HTTP 任务定义与执行纵向切片；依赖 AllowPrivateNetwork 测试配置访问环回探针。</summary>
internal static class JobsHttpHandlerAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        await using var listener = await LoopbackProbe.StartAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await JobsHostDefinitionAssertions.LoginAsHostAdminAsync(
            client,
            cancellationToken);
        var secretKey = $"jobs.http.secrets.{Guid.NewGuid():N}"[..28];

        using (var secretRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
                   HttpMethod.Post,
                   "/api/v1/settings/config-entries",
                   adminToken,
                   new CreateConfigEntryRequest(
                       secretKey,
                       "HTTP 密钥",
                       null,
                       null,
                       ConfigValueKinds.Secret,
                       "Bearer integration-token",
                       1)))
        using (var secretResponse = await client.SendAsync(secretRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Created, secretResponse.StatusCode);
        }

        var jobKey = $"ops.http.{Guid.NewGuid():N}"[..20];
        var httpArgs = new HttpJobArgs(
            listener.Url,
            "GET",
            new Dictionary<string, string> { ["X-Trace-Source"] = "fullnet-jobs" },
            new Dictionary<string, HttpJobSecretHeaderRef>
            {
                ["Authorization"] = new(secretKey),
            });
        using var createRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            adminToken,
            new CreateHostJobDefinitionRequest(
                jobKey,
                JobHandlerKinds.Http,
                httpArgs,
                "HTTP 集成任务",
                null,
                null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostJobDefinitionResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(JobHandlerKinds.Http, created.HandlerKind);
        Assert.IsNotNull(created.Args);
        var authorization = created.Args.SecretHeaders!
            .Single(pair => string.Equals(
                pair.Key,
                "Authorization",
                StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(secretKey, authorization.Value.ConfigKey);

        using var authRejectRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            adminToken,
            new CreateHostJobDefinitionRequest(
                $"ops.bad.{Guid.NewGuid():N}"[..18],
                JobHandlerKinds.Http,
                new HttpJobArgs(
                    "https://example.com",
                    "GET",
                    new Dictionary<string, string> { ["Authorization"] = "plain" }),
                "拒绝明文 Authorization",
                null,
                null));
        using var authRejectResponse = await client.SendAsync(authRejectRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, authRejectResponse.StatusCode);

        using var triggerRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{created.Id:D}/trigger",
            adminToken,
            new { });
        using var triggerResponse = await client.SendAsync(triggerRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, triggerResponse.StatusCode);
        var execution = await triggerResponse.Content.ReadFromJsonAsync<HostJobExecutionResponse>(
            cancellationToken);
        Assert.IsNotNull(execution);
        Assert.AreEqual(JobExecutionStatuses.Succeeded, execution.Status);
        Assert.IsTrue(listener.ReceivedAuthorization);

        using var privateRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{created.Id:D}",
            adminToken,
            new UpdateHostJobDefinitionRequest(
                created.DisplayName,
                null,
                null,
                JobHandlerKinds.Http,
                new HttpJobArgs("http://127.0.0.1/", "GET"),
                false,
                created.Version));
        using var privateUpdateResponse = await client.SendAsync(privateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, privateUpdateResponse.StatusCode);
        var updated = await privateUpdateResponse.Content.ReadFromJsonAsync<HostJobDefinitionResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);

        using var privateTriggerRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{created.Id:D}/trigger",
            adminToken,
            new { });
        using var privateTriggerResponse = await client.SendAsync(
            privateTriggerRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, privateTriggerResponse.StatusCode);
        var privateExecution = await privateTriggerResponse.Content
            .ReadFromJsonAsync<HostJobExecutionResponse>(cancellationToken);
        Assert.IsNotNull(privateExecution);
        Assert.AreEqual(JobExecutionStatuses.Failed, privateExecution.Status);
    }

    private sealed class LoopbackProbe : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        private LoopbackProbe(HttpListener listener, string url)
        {
            _listener = listener;
            Url = url;
            _loop = RunAsync(_cts.Token);
        }

        public string Url { get; }

        public bool ReceivedAuthorization { get; private set; }

        public static async Task<LoopbackProbe> StartAsync(CancellationToken cancellationToken)
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            var prefix = $"http://127.0.0.1:{port}/probe/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new LoopbackProbe(listener, $"http://127.0.0.1:{port}/probe");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (context.Request.Headers["Authorization"] == "Bearer integration-token")
                {
                    ReceivedAuthorization = true;
                }

                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _cts.Dispose();
        }
    }
}
