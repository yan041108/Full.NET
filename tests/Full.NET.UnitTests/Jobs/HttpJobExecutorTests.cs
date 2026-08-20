using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Execution.Handlers;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HttpJobExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_RejectsPersistedPlaintextSensitiveHeader_BeforeSending()
    {
        var clientFactory = new RecordingHttpClientFactory();
        var executor = new HttpJobExecutor(
            clientFactory,
            new UnusedSecretValueResolver(),
            Options.Create(new JobsHttpOptions()));
        var argsJson = JsonSerializer.Serialize(
            new HttpJobArgs(
                "https://example.com/health",
                "GET",
                new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer persisted-secret",
                }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var context = new JobExecutionContext(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "ops.health",
            JobHandlerKinds.Http,
            argsJson,
            JobTriggerKinds.Manual);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(context, CancellationToken.None));
        Assert.AreEqual(0, clientFactory.CreateCount);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public int CreateCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateCount++;
            return new HttpClient();
        }
    }

    private sealed class UnusedSecretValueResolver : ISettingsSecretValueResolver
    {
        public Task<Result<string>> ResolveSecretValueAsync(
            string configKey,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("不应解析密钥。");
    }
}
