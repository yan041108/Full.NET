using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>独立 RP、真实 Kestrel、真实 Worker 进程及本轮证书的资源归属。</summary>
internal sealed class IdentityOidcNetworkFixture(DatabaseProvider provider) : IAsyncDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fullnet-oidc-network-").FullName;
    private readonly string _workerName = "fullnet-oidc-worker-" + Guid.NewGuid().ToString("N");
    private readonly string _network = "fullnet-oidc-network-" + Guid.NewGuid().ToString("N");
    private readonly string _redis = "fullnet-oidc-redis-" + Guid.NewGuid().ToString("N");
    private bool _networkCreated, _redisStarted;
    private readonly Dictionary<string, string?> _settings = IdentityBrowserTestSupport.Settings();
    private readonly string _controlKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, string> _secrets = new() { ["a"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), ["b"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
    private FullNetApiFactory? _api;
    private Process? _rp, _worker;
    private Task<string>? _rpOut, _rpError, _workerOut, _workerError;
    private X509Certificate2? _certificate;
    private string _connection = "";
    private string _repository = "";
    public string Root => _root;
    public string Issuer { get; private set; } = "";
    public string RpOrigin { get; private set; } = "";
    public bool Passed { get; set; }
    internal string Network => _network;
    internal string SuiteOrigin { get; private set; } = "";
    internal int SuiteHttpPort { get; private set; }
    internal IReadOnlyDictionary<string, string> Secrets => _secrets;

    public async Task InitializeAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) directory = directory.Parent;
        _repository = directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
        var opPort = FreePort(); var rpPort = FreePort();
        var suiteTlsPort = FreePort(); SuiteHttpPort = FreePort(); SuiteOrigin = $"https://host.docker.internal:{suiteTlsPort}";
        Issuer = $"https://host.docker.internal:{opPort}/identity"; RpOrigin = $"https://127.0.0.1:{rpPort}";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddDnsName("host.docker.internal"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build()); request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        await File.WriteAllTextAsync(Path.Combine(_root, "tls.pem"), _certificate.ExportCertificatePem());
        await File.WriteAllTextAsync(Path.Combine(_root, "tls.key"), rsa.ExportPkcs8PrivateKeyPem());
        foreach (var key in _settings.Keys.Where(key => key.StartsWith("Identity:Oidc:Clients:", StringComparison.Ordinal)).ToArray()) _settings.Remove(key);
        _settings["Identity:Browser:Enable"] = "false"; _settings["Identity:Oidc:Issuer"] = Issuer;
        _settings["Identity:AllowedOrigins:0"] = $"https://host.docker.internal:{opPort}";
        _settings["Identity:AllowedOrigins:1"] = RpOrigin;
        _settings["Tenancy:HostDomains:0"] = "host.docker.internal";
        _settings["TrustedProxy:Enabled"] = "true"; _settings["TrustedProxy:KnownProxies:0"] = "127.0.0.1";
        _settings["DataProtection:KeyRingPath"] = Path.Combine(_root, "keys");
        _settings["Identity:Oidc:BackchannelAllowedOrigins:0"] = $"https://host.docker.internal:{rpPort}";
        _settings["Logging:LogLevel:Default"] = "Warning"; _settings["Serilog:MinimumLevel:Default"] = "Warning";
        var index = 0;
        foreach (var name in new[] { "a", "b" })
        {
            var prefix = $"Identity:Oidc:Clients:{index++}:";
            _settings[prefix + "ClientId"] = "interop-" + name; _settings[prefix + "ClientSecret"] = _secrets[name];
            _settings[prefix + "RedirectUris:0"] = $"{RpOrigin}/{name}/callback";
            _settings[prefix + "Scopes:0"] = "openid"; _settings[prefix + "Scopes:1"] = "profile"; _settings[prefix + "Scopes:2"] = "offline_access";
            _settings[prefix + "IsFirstParty"] = "true";
            _settings[prefix + "BackchannelLogoutUri"] = $"https://host.docker.internal:{rpPort}/{name}/backchannel";
        }
        foreach (var name in new[] { "suite-primary", "suite-secondary", "suite-post" })
        {
            var prefix = $"Identity:Oidc:Clients:{index++}:"; _secrets[name] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _settings[prefix + "ClientId"] = name; _settings[prefix + "ClientSecret"] = _secrets[name];
            _settings[prefix + "RedirectUris:0"] = SuiteOrigin + "/test/a/fullnet-interop/callback";
            _settings[prefix + "Scopes:0"] = "openid"; _settings[prefix + "Scopes:1"] = "profile";
            _settings[prefix + "IsFirstParty"] = "true";
        }
        _connection = await IdentityBrowserTestSupport.DatabaseAsync(provider);
        await RunDockerAsync("network", "create", _network); _networkCreated = true;
        var redisPort = FreePort();
        await RunDockerAsync("run", "-d", "--rm", "--name", _redis, "--network", _network, "--network-alias", "redis", "--memory", "256m",
            "-p", $"127.0.0.1:{redisPort}:6379", "redis:8.6", "redis-server", "--save", "", "--appendonly", "no"); _redisStarted = true;
        await WaitUntilAsync(async () => (await RunDockerAsync("exec", _redis, "redis-cli", "ping")).Trim() == "PONG", TimeSpan.FromSeconds(15));
        _settings["Cache:RedisConnectionString"] = $"127.0.0.1:{redisPort}";
        _settings["Realtime:RedisBackplaneConnectionString"] = $"127.0.0.1:{redisPort}";
        _settings["Realtime:AllowSharedRedisInDevelopment"] = "true";
        _api = new FullNetApiFactory(provider, _connection, _settings);
        _api.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await _api.InitializeAsync();
        var backend = _api.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var configuration = new { certificate = Path.Combine(_root, "tls.pem"), privateKey = Path.Combine(_root, "tls.key"),
            issuer = Issuer, rpOrigin = RpOrigin, opPort, rpPort, suiteTlsPort, suiteHttpPort = SuiteHttpPort, backend, controlKey = _controlKey, secrets = _secrets,
            readyFile = Path.Combine(_root, "ready.json"), protocolLog = Path.Combine(_root, "discovery.log") };
        await File.WriteAllTextAsync(Path.Combine(_root, "rp.json"), JsonSerializer.Serialize(configuration));
        var start = new ProcessStartInfo("node") { WorkingDirectory = _repository, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("tests/e2e/admin-real-stack/scripts/oidc-network-rp.mjs");
        start.Environment["FULLNET_INTEROP_CONFIG"] = Path.Combine(_root, "rp.json"); start.Environment["NODE_EXTRA_CA_CERTS"] = Path.Combine(_root, "tls.pem");
        _rp = Process.Start(start)!; _rpOut = _rp.StandardOutput.ReadToEndAsync(); _rpError = _rp.StandardError.ReadToEndAsync();
        await WaitUntilAsync(() => Task.FromResult(File.Exists(Path.Combine(_root, "ready.json")) || _rp.HasExited), TimeSpan.FromSeconds(30));
        var ready = File.Exists(Path.Combine(_root, "ready.json")) ? await File.ReadAllTextAsync(Path.Combine(_root, "ready.json")) : "process exited";
        Assert.IsTrue(ready.Contains("\"ready\":true", StringComparison.Ordinal), "Independent RP discovery failed: " + ready + "\n" + ReadIfPresent("discovery.log"));
        await WriteWorkerSettingsAsync();
    }

    public HttpClient Browser()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == _certificate?.Thumbprint },
            ConnectCallback = async (context, token) =>
            {
                if (context.DnsEndPoint.Host is not ("host.docker.internal" or "127.0.0.1")) throw new InvalidOperationException("Unexpected fixture origin.");
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                try { await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, token); return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            } };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("X-Fixture-Key", _controlKey);
        return client;
    }
    public string Op(string path) => new Uri(new Uri(Issuer), path).AbsoluteUri;
    public string Rp(string client, string operation) => $"{RpOrigin}/{client}/{operation}";
    public async Task<int> ScalarAsync(string sql)
    {
        await using var scope = _api!.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(new SqlStatement("test.oidc_network_observer", sql, SqlDataScope.HostOnly));
    }
    public async Task StartWorkerAsync()
    {
        var previousNodes = await ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_signing_node WHERE RoleKey = 'worker'");
        var arguments = new[] { "run", "--rm", "--name", _workerName, "--network", _network, "--cpus", "2", "--memory", "2g", "--mount", $"type=bind,source={_repository},target=/src,readonly",
            "--mount", $"type=bind,source={_root},target=/run/oidc,readonly", "-w", "/run/oidc", "-e", "ASPNETCORE_ENVIRONMENT=Testing", "-e", "DOTNET_ENVIRONMENT=Testing",
            "-e", "ASPNETCORE_CONTENTROOT=/run/oidc", "-e", "SSL_CERT_FILE=/run/oidc/tls.pem", "-e", "DOTNET_PROCESSOR_COUNT=2",
            "fullnet-native-aot-publish-sdk:10.0.404", "dotnet", "/src/src/Hosts/Full.NET.Host.Worker/bin/Release/net10.0/Full.NET.Host.Worker.dll" };
        var start = StartInfo("docker", arguments); _worker = Process.Start(start)!;
        _workerOut = _worker.StandardOutput.ReadToEndAsync(); _workerError = _worker.StandardError.ReadToEndAsync();
        try { await WaitUntilAsync(async () => !_worker.HasExited && await ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_signing_node WHERE RoleKey = 'worker'") > previousNodes, TimeSpan.FromSeconds(40)); }
        catch { await StopWorkerAsync(); throw new InvalidOperationException("Worker did not become ready. " + ReadIfPresent("worker.log")); }
    }
    public async Task StopWorkerAsync()
    {
        if (_worker is null) return;
        using var stop = Process.Start(StartInfo("docker", ["stop", "--time", "10", _workerName]))!;
        var stopped = stop.StandardOutput.ReadToEndAsync(); var stopError = stop.StandardError.ReadToEndAsync();
        await stop.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); await stopped; await stopError;
        await _worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await File.AppendAllTextAsync(Path.Combine(_root, "worker.log"), Sanitize(await _workerOut! + await _workerError!));
        _worker.Dispose(); _worker = null;
    }
    private async Task WriteWorkerSettingsAsync()
    {
        var settings = new Dictionary<string, string?>(_settings) { ["Database:Provider"] = provider.ToString(),
            ["Database:ConnectionString"] = _connection.Replace("127.0.0.1", "host.docker.internal", StringComparison.Ordinal).Replace("localhost", "host.docker.internal", StringComparison.Ordinal),
            ["Database:ConnectionName"] = "fullnet", ["Database:MySqlGuidStorageMode"] = "Binary16", ["FullNet:Modules:Preset"] = "Minimal",
            ["DataProtection:KeyRingPath"] = "/run/oidc/keys", ["OutboxWorker:PollMilliseconds"] = "100", ["OutboxWorker:BatchSize"] = "20",
            ["OutboxWorker:MaximumIdlePollMilliseconds"] = "1000",
            ["Cache:RedisConnectionString"] = "redis:6379", ["Realtime:RedisBackplaneConnectionString"] = "redis:6379",
            ["Realtime:AllowSharedRedisInDevelopment"] = "true",
            ["Messaging:Worker:Mode"] = "LegacyPolling", ["Urls"] = "http://127.0.0.1:0" };
        await File.WriteAllTextAsync(Path.Combine(_root, "appsettings.Testing.json"), JsonSerializer.Serialize(settings));
    }
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout) { if (await condition()) return; await Task.Delay(100); }
        Assert.Fail("OIDC interop condition did not complete within " + timeout);
    }
    private static ProcessStartInfo StartInfo(string file, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument); return start;
    }
    internal static async Task<string> RunDockerAsync(params string[] arguments)
    {
        using var process = Process.Start(StartInfo("docker", arguments))!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); var text = await output; var details = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("Owned Docker fixture failed: " + details);
        return text;
    }
    private string Sanitize(string text)
    {
        foreach (var secret in _secrets.Values.Append(_controlKey).Append(_connection).Where(value => value.Length > 0)) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return text;
    }
    private string ReadIfPresent(string name) => File.Exists(Path.Combine(_root, name)) ? File.ReadAllText(Path.Combine(_root, name)) : "";
    private static int FreePort() { using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private async Task DeleteOwnedDatabaseAsync()
    {
        if (string.IsNullOrEmpty(_connection)) return;
        // 连接串仅来自本夹具刚创建的随机库；不得按前缀批量删除共享实例中的其他数据库。
        if (provider == DatabaseProvider.SqlServer)
        {
            var settings = new SqlConnectionStringBuilder(_connection); var name = settings.InitialCatalog;
            Assert.IsTrue(Regex.IsMatch(name, "^fullnet_it_[0-9a-f]{32}$", RegexOptions.CultureInvariant));
            await File.WriteAllTextAsync(Path.Combine(_root, "owned-database.json"), JsonSerializer.Serialize(new { provider = provider.ToString(), name }));
            settings.InitialCatalog = "master"; settings.Pooling = false;
            await using var database = new SqlConnection(settings.ConnectionString);
            await database.ExecuteAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];", commandTimeout: 20);
        }
        else
        {
            var settings = new MySqlConnectionStringBuilder(_connection); var name = settings.Database;
            Assert.IsTrue(Regex.IsMatch(name, "^fullnet_it_[0-9a-f]{32}$", RegexOptions.CultureInvariant));
            await File.WriteAllTextAsync(Path.Combine(_root, "owned-database.json"), JsonSerializer.Serialize(new { provider = provider.ToString(), name }));
            settings.Database = ""; settings.Pooling = false;
            settings.UserID = "root"; settings.Password = SharedDatabaseFixture.MySqlRootPassword;
            await using var database = new MySqlConnection(settings.ConnectionString);
            await database.ExecuteAsync($"DROP DATABASE `{name}`;", commandTimeout: 20);
            await database.ExecuteAsync($"REVOKE ALL PRIVILEGES ON `{name}`.* FROM '{SharedDatabaseFixture.MySqlApplicationUserName}'@'%';", commandTimeout: 20);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (!Passed && _api is not null)
        {
            try
            {
                using var observer = Browser();
                var stats = new Dictionary<string, string>();
                foreach (var name in new[] { "a", "b" }) { using var response = await observer.GetAsync(Rp(name, "metrics")); stats[name] = await response.Content.ReadAsStringAsync(); }
                await using var scope = _api.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
                var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
                var queue = await queries.QueryAsync<string>(new SqlStatement("test.oidc_network_queue_diagnostics",
                    "SELECT CONCAT(ClientId, ':', StatusKey, ':', COALESCE(LastErrorCode, '-'), ':', AttemptCount) FROM fn_identity_oidc_logout_delivery", SqlDataScope.HostOnly));
                var outbox = await ScalarAsync("SELECT COUNT(1) FROM fn_outbox_message WHERE MessageType = 'fullnet.identity.oidc_logout.requested'");
                var revoked = await ScalarAsync("SELECT COUNT(1) FROM fn_identity_oidc_center_session WHERE RevokedAtUtc IS NOT NULL");
                var text = JsonSerializer.Serialize(new { outbox, revoked, queue, rp = stats });
                await File.WriteAllTextAsync(Path.Combine(_root, "diagnostics.json"), text); Console.WriteLine("OIDC_NETWORK_DIAGNOSTICS " + text);
            }
            catch (Exception error) { Console.WriteLine("OIDC diagnostics unavailable: " + error.GetType().Name); }
        }
        var cleanupErrors = new List<string>();
        async Task CleanupAsync(Func<Task> cleanup)
        {
            try { await cleanup(); }
            catch (Exception error) { cleanupErrors.Add(error.GetType().Name); }
        }
        // 单项容器清理失败不能阻止后续进程停止及临时秘密清理。
        await CleanupAsync(StopWorkerAsync);
        await CleanupAsync(async () =>
        {
            if (_rp is null) return;
            if (!_rp.HasExited) { _rp.Kill(entireProcessTree: true); await _rp.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(_root, "rp.log"), Sanitize(await _rpOut! + await _rpError!)); _rp.Dispose();
        });
        await CleanupAsync(() => { _api?.Dispose(); _certificate?.Dispose(); return Task.CompletedTask; });
        if (_redisStarted) await CleanupAsync(async () => { await RunDockerAsync("stop", "--time", "5", _redis); });
        if (_networkCreated) await CleanupAsync(async () => { await RunDockerAsync("network", "rm", _network); });
        if (cleanupErrors.Count == 0) await CleanupAsync(DeleteOwnedDatabaseAsync);
        // 本轮拥有且进程已退出的临时秘密必须清理；失败只留下协议状态、度量和去敏日志。
        foreach (var name in new[] { "rp.json", "appsettings.Testing.json", "tls.key", "tls.pem" }) File.Delete(Path.Combine(_root, name));
        if (Directory.Exists(Path.Combine(_root, "keys"))) Directory.Delete(Path.Combine(_root, "keys"), recursive: true);
        if (Passed && cleanupErrors.Count == 0) Directory.Delete(_root, recursive: true); else Console.WriteLine("OIDC interop diagnostics: " + _root);
        Assert.IsEmpty(cleanupErrors, "Owned OIDC fixture cleanup failed: " + string.Join(", ", cleanupErrors));
    }
}
