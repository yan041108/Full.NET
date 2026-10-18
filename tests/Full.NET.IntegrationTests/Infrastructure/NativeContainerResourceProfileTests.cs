using Docker.DotNet.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.IntegrationTests.Infrastructure;

/// <summary>只验证创建参数与所有权边界，不连接 Docker，不改变进程环境。</summary>
[TestClass]
public sealed class NativeContainerResourceProfileTests
{
    private static readonly Guid Session = Guid.Parse("184f35b9-9ca0-440d-a013-026e9c432460");

    private static Dictionary<string, string?> Settings() => new()
    {
        ["FULLNET_NATIVE_RESOURCE_PROFILE"] = "bounded-v1",
        ["FULLNET_NATIVE_RESOURCE_OWNER"] = "c6f146bacf654fe5a2fbd5f6bece8221",
        ["FULLNET_NATIVE_RESOURCE_SESSION"] = Session.ToString("D"),
        ["FULLNET_NATIVE_FIXTURE_MANIFEST_SHA256"] = new string('a', 64),
        ["FULLNET_NATIVE_RESOURCE_LEDGER"] = "/results/native-container-resources.jsonl",
        ["FULLNET_TESTCONTAINERS_REUSE"] = "0",
        ["TESTCONTAINERS_RYUK_DISABLED"] = "true",
    };

    private static NativeContainerResourceProfile Profile(List<string>? journal = null)
    {
        var settings = Settings();
        return NativeContainerResourceProfile.Parse(
            key => settings.GetValueOrDefault(key), Session, line => journal?.Add(line))!;
    }

    [TestMethod]
    public void AbsentProfilePreservesDefaultBehavior() => Assert.IsNull(
        NativeContainerResourceProfile.Parse(_ => null, Guid.Empty, _ => { }));

    [TestMethod]
    [DataRow("FULLNET_NATIVE_RESOURCE_PROFILE", "other")]
    [DataRow("FULLNET_NATIVE_RESOURCE_OWNER", "")]
    [DataRow("FULLNET_NATIVE_RESOURCE_OWNER", "not-a-nonce")]
    [DataRow("FULLNET_NATIVE_RESOURCE_OWNER", "00000000000000000000000000000000")]
    [DataRow("FULLNET_NATIVE_RESOURCE_SESSION", "00000000-0000-0000-0000-000000000000")]
    [DataRow("FULLNET_NATIVE_RESOURCE_SESSION", "184f35b9-9ca0-440d-a013-026e9c432461")]
    [DataRow("FULLNET_TESTCONTAINERS_REUSE", "1")]
    [DataRow("FULLNET_TESTCONTAINERS_REUSE", "")]
    [DataRow("TESTCONTAINERS_RYUK_DISABLED", "false")]
    [DataRow("FULLNET_NATIVE_FIXTURE_MANIFEST_SHA256", "")]
    [DataRow("FULLNET_NATIVE_RESOURCE_LEDGER", "/tmp/other.jsonl")]
    public void RejectsInvalidOwnershipAndCleanupConfiguration(string key, string value)
    {
        var settings = Settings();
        settings[key] = value;
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeContainerResourceProfile.Parse(
            name => settings.GetValueOrDefault(name), Session, _ => { }));
    }

    [TestMethod]
    public void RejectsEmptyActualSession()
    {
        var settings = Settings();
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeContainerResourceProfile.Parse(
            name => settings.GetValueOrDefault(name), Guid.Empty, _ => { }));
    }

    [TestMethod]
    [DataRow("sqlserver", 3221225472L, 1000000000L)]
    [DataRow("mysql", 1610612736L, 1000000000L)]
    [DataRow("kafka", 2147483648L, 1000000000L)]
    [DataRow("redis", 268435456L, 250000000L)]
    public void SetsHardLimitsAndIdentityBeforeCreate(string role, long memory, long cpu)
    {
        var journal = new List<string>();
        using var lease = Profile(journal).Acquire(role);
        var parameters = new CreateContainerParameters();
        lease.Apply(parameters);
        Assert.AreEqual(memory, parameters.HostConfig!.Memory);
        Assert.AreEqual(memory, parameters.HostConfig!.MemorySwap);
        Assert.AreEqual(cpu, parameters.HostConfig!.NanoCPUs);
        Assert.AreEqual(Session.ToString("D"), parameters.Labels![NativeContainerResourceProfile.SessionLabel]);
        Assert.AreEqual("c6f146bacf654fe5a2fbd5f6bece8221", parameters.Labels![NativeContainerResourceProfile.OwnerLabel]);
        Assert.AreEqual(role, parameters.Labels![NativeContainerResourceProfile.RoleLabel]);
        Assert.HasCount(1, journal);
        StringAssert.Contains(journal[0], "intent");
        using var intent = System.Text.Json.JsonDocument.Parse(journal[0]);
        var sqlBudget = intent.RootElement.GetProperty("sqlServerMemoryLimitMb");
        if (role == "sqlserver") Assert.AreEqual(2048, sqlBudget.GetInt32());
        else Assert.AreEqual(System.Text.Json.JsonValueKind.Null, sqlBudget.ValueKind);
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.Apply(new CreateContainerParameters()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SqlServerSetsInternalMemoryBudgetWithoutLosingOtherEnvironment(bool existingEnvironment)
    {
        using var lease = Profile().Acquire("sqlserver");
        var parameters = new CreateContainerParameters();
        if (existingEnvironment)
            parameters.Env = ["ACCEPT_EULA=Y", "MSSQL_MEMORY_LIMIT_MB=12000", "MSSQL_MEMORY_LIMIT_MB=4096"];
        lease.Apply(parameters);
        var expected = existingEnvironment
            ? new[] { "ACCEPT_EULA=Y", "MSSQL_MEMORY_LIMIT_MB=2048" }
            : ["MSSQL_MEMORY_LIMIT_MB=2048"];
        Assert.IsNotNull(parameters.Env);
        CollectionAssert.AreEquivalent(expected, parameters.Env.ToArray());
        lease.ValidateReadback(Observed(parameters));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("MSSQL_MEMORY_LIMIT_MB=12000")]
    [DataRow("MSSQL_MEMORY_LIMIT_MB=2048|MSSQL_MEMORY_LIMIT_MB=4096")]
    [DataRow("MSSQL_MEMORY_LIMIT_MB=2048|MSSQL_MEMORY_LIMIT_MB=2048")]
    public void SqlServerReadbackRejectsMissingChangedOrDuplicateInternalMemoryBudget(string? budget)
    {
        using var lease = Profile().Acquire("sqlserver");
        var parameters = new CreateContainerParameters();
        lease.Apply(parameters);
        var observed = Observed(parameters);
        observed.Config!.Env = budget?.Split('|').ToList() ?? [];
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateReadback(observed));
    }

    [TestMethod]
    [DataRow("mysql")]
    [DataRow("kafka")]
    [DataRow("redis")]
    public void OtherRolesPreserveEnvironment(string role)
    {
        using var lease = Profile().Acquire(role);
        var parameters = new CreateContainerParameters { Env = ["EXISTING_SETTING=value"] };
        lease.Apply(parameters);
        CollectionAssert.AreEqual(new[] { "EXISTING_SETTING=value" }, parameters.Env.ToArray());
        lease.ValidateReadback(Observed(parameters));
    }

    [TestMethod]
    public void RejectsConcurrentSameRoleAndAllowsRelease()
    {
        var profile = Profile();
        var lease = profile.Acquire("mysql");
        Assert.ThrowsExactly<InvalidOperationException>(() => profile.Acquire("mysql"));
        using var other = profile.Acquire("redis");
        lease.Dispose();
        lease.Dispose();
        using var next = profile.Acquire("mysql");
        Assert.ThrowsExactly<InvalidOperationException>(() => profile.Acquire("unknown"));
    }

    [TestMethod]
    public void RejectsCreateAfterRelease()
    {
        var lease = Profile().Acquire("redis");
        lease.Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.Apply(new CreateContainerParameters()));
    }

    [TestMethod]
    public void JournalFailurePreventsCreateAndParallelRoleClaimsCannotOverlap()
    {
        var settings = Settings();
        var profile = NativeContainerResourceProfile.Parse(
            key => settings.GetValueOrDefault(key), Session, _ => throw new IOException("ledger unavailable"))!;
        using var failed = profile.Acquire("redis");
        Assert.ThrowsExactly<IOException>(() => failed.Apply(new CreateContainerParameters()));
        var parallelProfile = Profile();
        var accepted = new System.Collections.Concurrent.ConcurrentBag<NativeContainerResourceProfile.Lease>();
        Parallel.For(0, 32, _ =>
        {
            try { accepted.Add(parallelProfile.Acquire("kafka")); }
            catch (InvalidOperationException) { }
        });
        Assert.HasCount(1, accepted);
        foreach (var lease in accepted) lease.Dispose();
    }

    [TestMethod]
    [DataRow("org.testcontainers.resource-reaper-session")]
    [DataRow("fullnet.owner-nonce")]
    [DataRow("fullnet.native-role")]
    [DataRow("fullnet.native-claim")]
    [DataRow("fullnet.native-profile")]
    public void EveryOwnershipLabelIsRequired(string label)
    {
        using var lease = Profile().Acquire("mysql");
        var parameters = new CreateContainerParameters();
        lease.Apply(parameters);
        var observed = new ContainerInspectResponse
        {
            ID = new string('b', 64), Image = "sha256:" + new string('c', 64),
            Config = new ContainerConfig { Image = parameters.Image, Labels = parameters.Labels },
            HostConfig = parameters.HostConfig,
        };
        observed.Config!.Labels!.Remove(label);
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateReadback(observed));
    }

    [TestMethod]
    public void RejectsForeignLabelsAndChangedLimitsDuringReadback()
    {
        using var lease = Profile().Acquire("redis");
        var parameters = new CreateContainerParameters();
        lease.Apply(parameters);
        var observed = new ContainerInspectResponse
        {
            ID = new string('b', 64), Image = "sha256:" + new string('c', 64),
            Config = new ContainerConfig { Image = parameters.Image, Labels = new Dictionary<string, string>(parameters.Labels!) },
            HostConfig = parameters.HostConfig, Mounts = [],
        };
        lease.ValidateReadback(observed);
        observed.Config!.Labels![NativeContainerResourceProfile.OwnerLabel] = new string('d', 32);
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateReadback(observed));
        observed.Config!.Labels![NativeContainerResourceProfile.OwnerLabel] = parameters.Labels![NativeContainerResourceProfile.OwnerLabel];
        observed.HostConfig!.MemorySwap = -1;
        Assert.ThrowsExactly<InvalidOperationException>(() => lease.ValidateReadback(observed));
    }

    private static ContainerInspectResponse Observed(CreateContainerParameters parameters) => new()
    {
        ID = new string('b', 64), Image = "sha256:" + new string('c', 64),
        Config = new ContainerConfig { Image = parameters.Image, Labels = parameters.Labels, Env = parameters.Env },
        HostConfig = parameters.HostConfig, Mounts = [],
    };

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnknownCreateIdentityAndNoOpDisposeMustRetainRole(bool idReadFails)
    {
        var journal = new List<string>();
        var profile = Profile(journal);
        var lease = profile.Acquire("mysql");
        lease.Apply(new CreateContainerParameters());
        var disposals = 0;
        Exception? failure = null;
        try
        {
            await lease.DisposeCoreAsync(
                () => idReadFails ? throw new IOException("identity unavailable") : new string('b', 64),
                () => { disposals++; return ValueTask.CompletedTask; },
                _ => Task.FromException<ContainerInspectResponse>(new IOException("first inspect unavailable")),
                _ => Task.FromResult(true));
        }
        catch (Exception error) { failure = error; }
        Assert.IsNotNull(failure);
        Assert.AreEqual(1, disposals);
        Assert.ThrowsExactly<InvalidOperationException>(() => profile.Acquire("mysql"));
        Assert.IsFalse(journal.Any(line => line.Contains("\"released\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task KnownContainerStillPresentMustRetainRole()
    {
        var profile = Profile();
        var lease = profile.Acquire("redis");
        var parameters = new CreateContainerParameters();
        lease.Apply(parameters);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => lease.DisposeCoreAsync(
            () => new string('b', 64), () => ValueTask.CompletedTask,
            _ => Task.FromResult(Observed(parameters)), _ => Task.FromResult(false)).AsTask());
        Assert.ThrowsExactly<InvalidOperationException>(() => profile.Acquire("redis"));
    }

    [TestMethod]
    public async Task OnlyConfirmedExactIdDeletionCanReleaseCreatedRole()
    {
        var journal = new List<string>();
        var profile = Profile(journal);
        var lease = profile.Acquire("kafka");
        var parameters = new CreateContainerParameters();
        lease.Apply(parameters);
        var confirmations = 0;
        await lease.DisposeCoreAsync(() => new string('b', 64), () => ValueTask.CompletedTask,
            _ => Task.FromResult(Observed(parameters)), id =>
            {
                Assert.AreEqual(new string('b', 64), id);
                confirmations++;
                return Task.FromResult(true);
            });
        Assert.AreEqual(1, confirmations);
        using var replacement = profile.Acquire("kafka");
        Assert.AreEqual(1, journal.Count(line => line.Contains("\"released\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void PlainDisposeCannotReleaseAnUnconfirmedCreateIntent()
    {
        var profile = Profile();
        var lease = profile.Acquire("redis");
        lease.Apply(new CreateContainerParameters());
        lease.Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => profile.Acquire("redis"));
    }
}
