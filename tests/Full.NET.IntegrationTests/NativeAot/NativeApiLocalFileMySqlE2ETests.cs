using System.Net;
using System.Text.Json;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>用真实原生 API 验证 Local 文件配置绑定、受限快照与正常停机关闭。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiLocalFileMySqlE2ETests
{
    [TestMethod]
    public async Task Production_native_api_writes_local_txt_and_releases_directory_on_stop()
    {
        if (!NativeApiArtifactLocator.TryResolve(out var artifact, out var skipReason))
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        var connection = await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        await NativeApiDatabaseBootstrap.BootstrapAsync(DatabaseProvider.MySql, connection).ConfigureAwait(false);
        var root=Path.Combine(Path.GetTempPath(),"fullnet-native-local-"+Guid.NewGuid().ToString("N"));
        using var assets=new ProductionLogHostTestAssets();
        var settings=new Dictionary<string,string?>
        {
            ["FullNet:Logging:DeliveryMode"]="Local",
            ["FullNet:Logging:ExpectedDeliveryMode"]="Local",
            ["FullNet:Logging:LocalFile:Enabled"]="true",
            ["FullNet:Logging:LocalFile:Directory"]=root,
            ["Observability:HttpOperation:SuccessSampleRate"]="1",
            ["Observability:HttpOperation:SlowRequestThreshold"]="00:00:00.0000001",
        };
        await assets.ApplyToAsync(settings).ConfigureAwait(false);
        try
        {
            await using (var host=await NativeApiProcessHost.StartAsync(artifact,DatabaseProvider.MySql,connection,settings,NativeAotTestTimeouts.ProcessStartup).ConfigureAwait(false))
            {
                using var client=host.CreateClient();
                using var missing=await client.GetAsync("/api/v1/native-local-missing").ConfigureAwait(false);
                Assert.AreEqual(HttpStatusCode.NotFound,missing.StatusCode);
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await host.StopGracefullyAsync(stopTimeout.Token).ConfigureAwait(false);
                Assert.AreEqual(0, host.ExitCode, $"Native Host.Api 退出异常；进程日志：{host.LogFilePath}");
                host.AssertNoFatalMarkersInLogs();
            }
            var files=Directory.GetFiles(root,"*.txt",SearchOption.AllDirectories);
            Assert.IsTrue(files.Any(p=>Path.GetFileName(p).Contains("general",StringComparison.Ordinal)));
            Assert.IsTrue(files.Any(p=>Path.GetFileName(p).Contains("priority",StringComparison.Ordinal)));
            var generalLines = files.Where(p => Path.GetFileName(p).Contains("general", StringComparison.Ordinal))
                .SelectMany(File.ReadAllLines).ToArray();
            var priorityLines = files.Where(p => Path.GetFileName(p).Contains("priority", StringComparison.Ordinal))
                .SelectMany(File.ReadAllLines).ToArray();
            var resourceLine = generalLines.Single(line => line.Contains("LoggingResource Application=", StringComparison.Ordinal));
            using var resourceJson = JsonDocument.Parse(resourceLine);
            Assert.AreEqual("Production", resourceJson.RootElement.GetProperty("Environment").GetString());
            Assert.IsTrue(priorityLines.Any(IsRequestedHttpOperation));
            var lines = generalLines.Concat(priorityLines).ToArray();
            foreach(var line in lines)
            {
                using var json=JsonDocument.Parse(line);
                Assert.IsTrue(json.RootElement.TryGetProperty("LogEventId",out _));
                Assert.IsFalse(json.RootElement.TryGetProperty("ClientIp",out _));
            }
            // 正常 SIGTERM 后目录排他锁必须释放；不以 SIGKILL 行为替代停机刷新验收。
            using var lease=new FileStream(Path.Combine(root,"Full.NET.Host.Api",".fullnet-local.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    private static bool IsRequestedHttpOperation(string line)
    {
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        return root.TryGetProperty("EventName", out var eventName)
            && eventName.GetString() == "HttpOperationCompleted"
            && root.TryGetProperty("http.method", out var method) && method.GetString() == "GET"
            && root.TryGetProperty("http.route", out var route) && route.GetString() == "<unmatched>"
            && root.TryGetProperty("http.status_code", out var status) && status.GetInt32() == 404;
    }
}
