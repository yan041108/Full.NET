using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Containers;

namespace Full.NET.IntegrationTests.Infrastructure;

/// <summary>仅供显式准入的 Native 验收使用；创建前固定资源和身份，不接管共享容器。</summary>
internal sealed class NativeContainerResourceProfile
{
    internal const string SessionLabel = "org.testcontainers.resource-reaper-session";
    internal const string OwnerLabel = "fullnet.owner-nonce";
    internal const string RoleLabel = "fullnet.native-role";
    internal const string ClaimLabel = "fullnet.native-claim";
    private const string ProfileLabel = "fullnet.native-profile";
    private const string SqlServerMemoryPrefix = "MSSQL_MEMORY_LIMIT_MB=";
    private const string SqlServerMemoryBudget = SqlServerMemoryPrefix + "2048";
    private readonly string _owner;
    private readonly string _session;
    private readonly string _manifest;
    private readonly Action<string> _journal;
    private readonly HashSet<string> _activeRoles = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private static readonly Lazy<NativeContainerResourceProfile?> CurrentProfile = new(() =>
    {
        if (Environment.GetEnvironmentVariable("FULLNET_NATIVE_RESOURCE_PROFILE") is null) return null;
        return Parse(Environment.GetEnvironmentVariable, ResourceReaper.DefaultSessionId, line =>
        {
            // 固定在控制器挂载的结果目录；每条创建意图须落盘后才允许发起 Docker create。
            using var stream = new FileStream("/results/native-container-resources.jsonl", FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        });
    });

    internal static NativeContainerResourceProfile? Current => CurrentProfile.Value;

    private NativeContainerResourceProfile(string owner, string session, string manifest, Action<string> journal)
        => (_owner, _session, _manifest, _journal) = (owner, session, manifest, journal);

    internal static NativeContainerResourceProfile? Parse(Func<string, string?> read, Guid actualSession, Action<string> journal)
    {
        var mode = read("FULLNET_NATIVE_RESOURCE_PROFILE");
        if (mode is null) return null;
        var owner = read("FULLNET_NATIVE_RESOURCE_OWNER");
        var manifest = read("FULLNET_NATIVE_FIXTURE_MANIFEST_SHA256");
        if (mode != "bounded-v1" || !IsLowerHex(owner, 32) || owner == new string('0', 32) || !IsLowerHex(manifest, 64)
            || actualSession == Guid.Empty || read("FULLNET_NATIVE_RESOURCE_SESSION") != actualSession.ToString("D")
            || read("FULLNET_TESTCONTAINERS_REUSE") != "0" || read("TESTCONTAINERS_RYUK_DISABLED") != "true"
            || read("FULLNET_NATIVE_RESOURCE_LEDGER") != "/results/native-container-resources.jsonl")
            throw new InvalidOperationException("Native bounded profile requires exact controller ownership, session, ledger and non-reuse cleanup settings.");
        return new(owner!, actualSession.ToString("D"), manifest!, journal);
    }

    private static bool IsLowerHex(string? value, int length)
        => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal Lease Acquire(string role)
    {
        var limits = role switch
        {
            "sqlserver" => (Memory: 3L << 30, Cpu: 1_000_000_000L, Image: "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"),
            "mysql" => (Memory: 1536L << 20, Cpu: 1_000_000_000L, Image: "mysql:8.0"),
            "kafka" => (Memory: 2L << 30, Cpu: 1_000_000_000L, Image: "apache/kafka:4.1.2"),
            "redis" => (Memory: 256L << 20, Cpu: 250_000_000L, Image: "redis:8.6"),
            _ => throw new InvalidOperationException("Unknown Native container role."),
        };
        lock (_gate)
        {
            if (!_activeRoles.Add(role)) throw new InvalidOperationException("Native container role already active: " + role);
            return new Lease(this, role, limits.Memory, limits.Cpu, limits.Image);
        }
    }

    internal sealed class Lease : IDisposable
    {
        private readonly NativeContainerResourceProfile _profile;
        private readonly string _role;
        private readonly long _memory;
        private readonly long _cpu;
        private readonly string _image;
        private readonly string _claim = Guid.NewGuid().ToString("N");
        private bool _applied;
        private bool _disposed;
        private string? _observedId;
        private bool _absenceConfirmed;

        internal Lease(NativeContainerResourceProfile profile, string role, long memory, long cpu, string image)
            => (_profile, _role, _memory, _cpu, _image) = (profile, role, memory, cpu, image);

        internal void Apply(CreateContainerParameters parameters)
        {
            lock (_profile._gate)
            {
                if (_disposed || _applied) throw new InvalidOperationException("Native claim permits exactly one create before release.");
                parameters.Image = _image;
                parameters.HostConfig ??= new HostConfig();
                parameters.HostConfig.Memory = _memory;
                parameters.HostConfig.MemorySwap = _memory;
                parameters.HostConfig.NanoCPUs = _cpu;
                if (_role == "sqlserver")
                {
                    // CU14 不识别 cgroup v2 预算；内部保留 2GiB，给 3GiB 容器中的其余内存留空间。
                    parameters.Env = (parameters.Env ?? [])
                        .Where(value => !value.StartsWith(SqlServerMemoryPrefix, StringComparison.Ordinal))
                        .Append(SqlServerMemoryBudget).ToList();
                }
                parameters.Labels ??= new Dictionary<string, string>();
                parameters.Labels[SessionLabel] = _profile._session;
                parameters.Labels[OwnerLabel] = _profile._owner;
                parameters.Labels[RoleLabel] = _role;
                parameters.Labels[ClaimLabel] = _claim;
                parameters.Labels[ProfileLabel] = "bounded-v1";
                Write("intent", null);
                _applied = true;
            }
        }

        internal void ValidateReadback(ContainerInspectResponse observed)
        {
            var labels = observed.Config?.Labels;
            string? Label(string key) => labels is not null && labels.TryGetValue(key, out var value) ? value : null;
            var sqlMemoryMatches = _role != "sqlserver" || observed.Config?.Env?
                .Where(value => value.StartsWith(SqlServerMemoryPrefix, StringComparison.Ordinal))
                .SequenceEqual([SqlServerMemoryBudget]) == true;
            if (!_applied || _disposed || !IsLowerHex(observed.ID, 64)
                || observed.Config?.Image != _image || observed.Image?.StartsWith("sha256:", StringComparison.Ordinal) != true
                || !IsLowerHex(observed.Image[7..], 64)
                || Label(SessionLabel) != _profile._session
                || Label(OwnerLabel) != _profile._owner
                || Label(RoleLabel) != _role
                || Label(ClaimLabel) != _claim
                || Label(ProfileLabel) != "bounded-v1"
                || observed.HostConfig?.Memory != _memory || observed.HostConfig.MemorySwap != _memory
                || observed.HostConfig.NanoCPUs != _cpu || !sqlMemoryMatches)
                throw new InvalidOperationException("Native create readback does not match the recorded ownership and hard limits.");
        }

        private static async Task<ContainerInspectResponse> InspectActualAsync(string containerId)
        {
            if (!IsLowerHex(containerId, 64)) throw new InvalidOperationException("Exact created container ID required.");
            // 此分支只在 Linux Native 进程内启用，不使用远端或全局 Docker 配置。
            if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Native bounded containers require the admitted Linux process.");
            using var client = new DockerClientBuilder().WithEndpoint(new Uri("unix:///var/run/docker.sock")).Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await client.Containers.InspectContainerAsync(containerId, timeout.Token).ConfigureAwait(false);
        }

        private void RecordObserved(ContainerInspectResponse observed)
        {
            lock (_profile._gate)
            {
                ValidateReadback(observed);
                if (_observedId is not null && _observedId != observed.ID)
                    throw new InvalidOperationException("One Native claim cannot change its observed container identity.");
                Write("observed", observed);
                _observedId = observed.ID;
            }
        }

        internal async Task CaptureAsync(string containerId)
            => RecordObserved(await InspectActualAsync(containerId).ConfigureAwait(false));

        private static async Task<bool> ConfirmAbsentAsync(string containerId)
        {
            try { _ = await InspectActualAsync(containerId).ConfigureAwait(false); return false; }
            catch (DockerContainerNotFoundException) { return true; }
        }

        internal ValueTask DisposeContainerAsync(IContainer container)
            => DisposeCoreAsync(() => container.Id, container.DisposeAsync, InspectActualAsync, ConfirmAbsentAsync);

        internal async ValueTask DisposeCoreAsync(Func<string> readId, Func<ValueTask> dispose,
            Func<string, Task<ContainerInspectResponse>> inspect, Func<string, Task<bool>> confirmAbsent)
        {
            try
            {
                var id = readId();
                if (!string.IsNullOrEmpty(id)) RecordObserved(await inspect(id).ConfigureAwait(false));
            }
            finally
            {
                // Testcontainers 的 Dispose 成功可能只是未知 ID 时的空操作，不能直接释放已创建的角色。
                await dispose().ConfigureAwait(false);
                if (_applied)
                {
                    var knownId = _observedId;
                    if (knownId is null || !await confirmAbsent(knownId).ConfigureAwait(false))
                        throw new InvalidOperationException("Native create identity/deletion unconfirmed; retain role for independent cleanup.");
                    _absenceConfirmed = true;
                }
                Dispose();
            }
        }

        private void Write(string kind, ContainerInspectResponse? observed)
            => _profile._journal(JsonSerializer.Serialize(new
            {
                kind, at = DateTimeOffset.UtcNow, sessionId = _profile._session, ownerNonce = _profile._owner,
                fixtureManifestSha256 = _profile._manifest, role = _role, claim = _claim,
                memory = _memory, memorySwap = _memory, nanoCpus = _cpu, image = _image,
                sqlServerMemoryLimitMb = _role == "sqlserver" ? 2048 : (int?)null,
                id = observed?.ID ?? (kind == "released" ? _observedId : null), imageId = observed?.Image,
                deletionConfirmed = kind == "released" && _absenceConfirmed,
                volumes = observed?.Mounts?.Where(m => m.Type == "volume").Select(m => m.Name).ToArray(),
            }));

        public void Dispose()
        {
            lock (_profile._gate)
            {
                if (_disposed) return;
                if (_applied && !_absenceConfirmed) return;
                if (_applied) Write("released", null);
                _disposed = true;
                _profile._activeRoles.Remove(_role);
            }
        }
    }
}
