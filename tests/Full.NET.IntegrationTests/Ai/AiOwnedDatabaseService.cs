using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Full.NET.Data.Abstractions;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>服务故障测试只允许中断与恢复本批准确登记的独立数据库容器。</summary>
internal sealed class AiOwnedDatabaseService : IAsyncDisposable
{
    private const string OwnerLabel = "fullnet.a06.service-fault.owner";
    private readonly IContainer _container;
    private readonly string _name;
    private readonly string _owner;
    private readonly string _image;
    private readonly long _memory;
    private readonly long _cpu;
    private readonly string _registry;
    private string? _createdId;

    private AiOwnedDatabaseService(IContainer container, string name, string owner, string image, long memory, long cpu, string registry)
    {
        _container = container; _name = name; _owner = owner; _image = image; _memory = memory; _cpu = cpu; _registry = registry;
    }

    internal string Id => _container.Id;
    internal string ConnectionString => _container switch
    {
        MsSqlContainer sql => sql.GetConnectionString(),
        MySqlContainer mysql => mysql.GetConnectionString(),
        _ => throw new InvalidOperationException("未知数据库容器。")
    };

    internal static async Task<AiOwnedDatabaseService> StartAsync(DatabaseProvider provider, string registry)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("服务故障验收只允许在受控 Linux 驱动内执行。");
        var owner = Guid.CreateVersion7().ToString("N");
        var name = "fullnet-a06-owned-db-" + owner;
        var sql = provider == DatabaseProvider.SqlServer;
        if (!sql && provider != DatabaseProvider.MySql) throw new ArgumentOutOfRangeException(nameof(provider));
        var image = sql ? "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04" : "mysql:8.0";
        var memory = sql ? 3L << 30 : 1536L << 20;
        var cpu = sql ? 2_000_000_000L : 1_000_000_000L;
        // 创建前先登记唯一名称与所有权；不复用任何共享容器。
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { phase = "intent", provider = provider.ToString(), name, owner, image, memory, cpu }) + "\n");
        IContainer container = sql
            ? new MsSqlBuilder(image).WithPassword(SharedDatabaseFixture.MySqlRootPassword)
                .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
                .WithName(name).WithLabel(OwnerLabel, owner).WithReuse(false).WithCleanUp(false).WithImagePullPolicy(PullPolicy.Never)
                .WithCreateParameterModifier(p => Limit(p, memory, cpu)).Build()
            : new MySqlBuilder(image).WithDatabase("fullnet").WithUsername("fullnet")
                .WithPassword(SharedDatabaseFixture.MySqlRootPassword)
                .WithCommand("--log-bin-trust-function-creators=1")
                .WithName(name).WithLabel(OwnerLabel, owner).WithReuse(false).WithCleanUp(false).WithImagePullPolicy(PullPolicy.Never)
                .WithCreateParameterModifier(p => Limit(p, memory, cpu)).Build();
        var service = new AiOwnedDatabaseService(container, name, owner, image, memory, cpu, registry);
        try
        {
            await container.StartAsync();
            service._createdId = container.Id;
            await service.VerifyAsync(expectedRunning: true);
            await service.RecordAsync("started");
            return service;
        }
        catch
        {
            // 启动失败可能已经创建容器；仅登记已核证的准确 ID，不猜测归属或删除资源。
            try
            {
                service._createdId = container.Id;
                await service.VerifyAsync();
                await service.RecordAsync("start-failed-owned");
            }
            catch { await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { phase = "start-failed-unverified", name, owner }) + "\n"); }
            throw;
        }
    }

    private static void Limit(CreateContainerParameters parameters, long memory, long cpu)
    {
        var host = parameters.HostConfig ??= new(); host.Memory = memory; host.MemorySwap = memory; host.NanoCPUs = cpu;
    }

    private async Task VerifyAsync(bool? expectedRunning = null)
    {
        var id = Id;
        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) || id != _createdId)
            throw new InvalidOperationException("准确容器 ID 缺失或发生变化。");
        using var client = new DockerClientBuilder().WithEndpoint(new Uri("unix:///var/run/docker.sock")).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var observed = await client.Containers.InspectContainerAsync(id, timeout.Token);
        if (observed.ID != id || observed.Name != "/" + _name || observed.Config?.Image != _image
            || observed.Config.Labels is null || !observed.Config.Labels.TryGetValue(OwnerLabel, out var label) || label != _owner
            || observed.HostConfig?.Memory != _memory || observed.HostConfig.MemorySwap != _memory || observed.HostConfig.NanoCPUs != _cpu
            || (expectedRunning is not null && observed.State?.Running != expectedRunning.Value))
            throw new InvalidOperationException("数据库容器身份、所有权或资源上限不匹配。");
    }

    private Task RecordAsync(string phase) => File.AppendAllTextAsync(_registry,
        JsonSerializer.Serialize(new { phase, name = _name, owner = _owner, id = Id }) + "\n");

    internal async Task StopAsync()
    {
        await VerifyAsync(expectedRunning: true); await RecordAsync("stop-intent");
        await _container.StopAsync(); await VerifyAsync(expectedRunning: false); await RecordAsync("stopped");
    }

    internal async Task KillAsync()
    {
        await VerifyAsync(expectedRunning: true); await RecordAsync("kill-intent");
        using var client = new DockerClientBuilder().WithEndpoint(new Uri("unix:///var/run/docker.sock")).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.Containers.KillContainerAsync(_createdId!, new ContainerKillParameters { Signal = "SIGKILL" }, timeout.Token);
        await client.Containers.WaitContainerAsync(_createdId!, timeout.Token);
        await VerifyAsync(expectedRunning: false);
        var observed = await client.Containers.InspectContainerAsync(_createdId!, timeout.Token);
        if (observed.State is null || observed.State.ExitCode != 137 || observed.State.OOMKilled)
            throw new InvalidOperationException("独立数据库容器未确认以 SIGKILL 退出或发生 OOM。");
        await RecordAsync("killed-137");
    }

    internal async Task RestartAsync()
    {
        await VerifyAsync(expectedRunning: false); await RecordAsync("restart-intent");
        await _container.StartAsync(); await VerifyAsync(expectedRunning: true); await RecordAsync("restarted");
    }

    public async ValueTask DisposeAsync()
    {
        await VerifyAsync(); await RecordAsync("dispose-intent");
        await _container.DisposeAsync(); await RecordAsync("disposed");
    }
}
