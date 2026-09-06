using System.Diagnostics;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Full.NET.Modules.ObservabilityAdmin.Features.MonitorServer;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.ObservabilityAdmin;

[TestClass]
public sealed class ServerMonitorServiceTests
{
    [TestMethod]
    public void ListInstances_always_includes_current_instance_and_marks_remote_entries_as_catalog_only()
    {
        var service = CreateService(
            new ObservabilityAdminOptions
            {
                InstanceKey = "api-primary",
                InstanceDisplayName = "API 主实例",
                HostRole = "Api",
                Instances =
                [
                    new ObservabilityInstanceCatalogEntryOptions
                    {
                        InstanceKey = "worker-1",
                        DisplayName = "Worker 1",
                        HostRole = "Worker",
                    },
                ],
            });

        var instances = service.ListInstances();

        Assert.HasCount(2, instances);
        var current = instances.Single(entry => entry.IsCurrent);
        Assert.AreEqual("api-primary", current.InstanceKey);
        Assert.AreEqual(ServerInstanceRuntimeQueryability.Local, current.RuntimeQueryability);

        var remote = instances.Single(entry => entry.InstanceKey == "worker-1");
        Assert.IsFalse(remote.IsCurrent);
        Assert.AreEqual(ServerInstanceRuntimeQueryability.CatalogOnly, remote.RuntimeQueryability);
    }

    [TestMethod]
    public async Task GetRuntime_returns_null_for_catalog_only_instances()
    {
        var service = CreateService(
            new ObservabilityAdminOptions
            {
                InstanceKey = "api-primary",
                Instances =
                [
                    new ObservabilityInstanceCatalogEntryOptions
                    {
                        InstanceKey = "worker-1",
                        DisplayName = "Worker 1",
                        HostRole = "Worker",
                    },
                ],
            });

        var runtime = await service.GetRuntimeAsync("worker-1", CancellationToken.None);

        Assert.IsNull(runtime);
    }

    [TestMethod]
    public async Task GetRuntime_for_current_instance_does_not_expose_connection_strings_or_environment_variables()
    {
        Environment.SetEnvironmentVariable("FULLNET_TEST_SECRET", "Server=secret.database;Password=secret");
        var service = CreateService(
            new ObservabilityAdminOptions
            {
                InstanceKey = "api-primary",
                InstanceDisplayName = "API 主实例",
                HostRole = "Api",
            });

        try
        {
            var runtime = await service.GetRuntimeAsync("api-primary", CancellationToken.None);

            Assert.IsNotNull(runtime);
            Assert.AreEqual("api-primary", runtime.InstanceKey);
            Assert.IsTrue(runtime.UptimeSeconds >= 0);
            Assert.IsFalse(string.IsNullOrWhiteSpace(runtime.FrameworkDescription));
            Assert.IsFalse(string.IsNullOrWhiteSpace(runtime.ApplicationVersion));

            var serialized = System.Text.Json.JsonSerializer.Serialize(runtime);
            Assert.DoesNotContain("ConnectionString", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FULLNET_TEST_SECRET", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("Password=", serialized, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FULLNET_TEST_SECRET", null);
        }
    }

    [TestMethod]
    public void Options_validator_rejects_more_than_fifty_catalog_entries()
    {
        var validator = new ObservabilityAdminOptionsValidator();
        var instances = Enumerable.Range(0, 51)
            .Select(index => new ObservabilityInstanceCatalogEntryOptions
            {
                InstanceKey = $"instance-{index}",
                DisplayName = $"Instance {index}",
                HostRole = "Worker",
            })
            .ToArray();

        var result = validator.Validate(
            null,
            new ObservabilityAdminOptions { Instances = instances });

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Options_validator_rejects_overlong_instance_keys()
    {
        var validator = new ObservabilityAdminOptionsValidator();
        var result = validator.Validate(
            null,
            new ObservabilityAdminOptions
            {
                Instances =
                [
                    new ObservabilityInstanceCatalogEntryOptions
                    {
                        InstanceKey = new string('a', 129),
                        DisplayName = "Worker",
                        HostRole = "Worker",
                    },
                ],
            });

        Assert.IsTrue(result.Failed);
    }

    private static ServerMonitorService CreateService(ObservabilityAdminOptions options)
    {
        return new ServerMonitorService(
            Options.Create(options),
            new ServerRuntimeReader(new FixedClock(DateTimeOffset.UtcNow.AddMinutes(5))));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
