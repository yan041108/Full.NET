using Full.NET.Hosting.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Full.NET.UnitTests.Hosting;

/// <summary>本地文件日志：目录租约、双通道滚动、脱敏快照与 Production DeliveryMode 联校。</summary>
[TestClass]
[DoNotParallelize]
public sealed class LocalFileLoggingTests
{
    [TestMethod]
    public void Production_local_with_file_storage_writes_safe_json_and_flushes_on_shutdown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-local-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production,
                ApplicationName = "Full.NET.Host.Api",
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Logging:DeliveryMode"] = "Local",
                ["FullNet:Logging:ExpectedDeliveryMode"] = "Local",
                ["FullNet:Logging:LocalFile:Enabled"] = "true",
                ["FullNet:Logging:LocalFile:Directory"] = directory,
            });
            builder.AddFullNetServiceDefaults();
            using (var host = builder.Build())
            {
                var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LocalFileTest");
                logger.LogInformation("Local probe {Count}", 7);
                logger.LogError("Priority probe");
                logger.LogInformation("Authorization={Authorization}", "Bearer local-file-secret-probe");
            }
            var files = Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories);
            Assert.AreEqual(2, files.Length);
            StringAssert.Contains(File.ReadAllText(files.Single(p => Path.GetFileName(p).Contains("general"))), "Local probe");
            StringAssert.Contains(File.ReadAllText(files.Single(p => Path.GetFileName(p).Contains("priority"))), "Priority probe");
            foreach (var line in files.SelectMany(File.ReadAllLines))
            {
                using var json = System.Text.Json.JsonDocument.Parse(line);
                Assert.IsTrue(json.RootElement.TryGetProperty("LogEventId", out _));
            }
            Assert.IsFalse(files.Any(p => File.ReadAllText(p).Contains("local-file-secret-probe", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void Relative_directory_uses_content_root_and_process_lease_is_exclusive()
    {
        WithDirectory(root =>
        {
            var options = new LocalFileLoggingOptions { Directory = "logs" };
            var store = new LocalFileLogStore(options, root, "Api");
            Assert.AreEqual(Path.Combine(root, "logs", "Api"), store.DirectoryPath);
            Assert.ThrowsExactly<IOException>(() => new LocalFileLogStore(options, root, "Api"));
            store.General.Dispose(); store.Priority.Dispose();
            var restarted = new LocalFileLogStore(options, root, "Api");
            restarted.General.Dispose(); restarted.Priority.Dispose();
        });
    }

    [TestMethod]
    public void Rolls_by_size_and_utc_date_and_keeps_two_streams_separate()
    {
        WithDirectory(root =>
        {
            var clock = new LocalClock();
            var store = new LocalFileLogStore(new LocalFileLoggingOptions { Directory = root, FileSizeLimitBytes = 256 }, root, "Api", clock);
            store.General.Emit(Line(180)); store.General.Emit(Line(180));
            store.Priority.Emit(Line(30));
            clock.Advance(TimeSpan.FromDays(1));
            store.General.Emit(Line(20));
            store.General.Dispose(); store.Priority.Dispose();
            var files = Directory.GetFiles(store.DirectoryPath, "*.txt");
            Assert.AreEqual(4, files.Length);
            Assert.IsTrue(files.All(p => new FileInfo(p).Length <= 256));
            Assert.IsTrue(files.Any(p => Path.GetFileName(p).Contains("20261011")));
        });
    }

    [TestMethod]
    public void Retention_deletes_only_valid_owned_files_and_budget_removes_oldest_closed_file()
    {
        WithDirectory(root =>
        {
            var scope = Path.Combine(root, "Api"); Directory.CreateDirectory(scope);
            var expired = Path.Combine(scope, $"fullnet-general-20260901-{Guid.NewGuid():N}.txt");
            File.WriteAllText(expired, "expired");
            var foreign = Path.Combine(scope, "notes.txt"); File.WriteAllText(foreign, "keep");
            var malformed = Path.Combine(scope, $"fullnet-general-20269999-{Guid.NewGuid():N}.txt"); File.WriteAllText(malformed, "keep");
            var store = new LocalFileLogStore(new LocalFileLoggingOptions { Directory=root, FileSizeLimitBytes=256, MaxDirectoryBytes=512 }, root, "Api", new LocalClock());
            Assert.IsFalse(File.Exists(expired));
            for (var i=0; i<12; i++) { store.General.Emit(Line(200)); store.Priority.Emit(Line(100)); }
            store.General.Dispose(); store.Priority.Dispose();
            Assert.IsTrue(File.Exists(foreign)); Assert.IsTrue(File.Exists(malformed));
            Assert.IsTrue(Directory.GetFiles(scope, "*.txt").Where(p => p != foreign && p != malformed).Sum(p => new FileInfo(p).Length) <= 512);
        });
    }

    [TestMethod]
    public void Idle_flush_makes_buffered_record_visible_and_runtime_failure_recovers()
    {
        WithDirectory(root =>
        {
            var clock = new LocalClock();
            var store = new LocalFileLogStore(new LocalFileLoggingOptions { Directory=root, FileSizeLimitBytes=256 }, root, "Api", clock);
            store.General.Emit(Line(30));
            clock.Advance(TimeSpan.FromSeconds(2)); store.General.FlushIfDue();
            var path = Directory.GetFiles(store.DirectoryPath, "fullnet-general-*.txt").Single();
            Assert.AreEqual(30, new FileInfo(path).Length);
            Assert.ThrowsExactly<IOException>(() => store.General.Emit(Line(300)));
            store.General.Emit(Line(40));
            store.General.Dispose(); store.Priority.Dispose();
            Assert.AreEqual(70, Directory.GetFiles(store.DirectoryPath,"fullnet-general-*.txt").Sum(p => new FileInfo(p).Length));
        });
    }

    [TestMethod]
    public void Wrong_mode_and_invalid_storage_budgets_are_rejected_before_opening_files()
    {
        foreach (var mode in new string?[] {null, "Collector", "ApplicationKafka"})
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["FullNet:Logging:DeliveryMode"]=mode, ["FullNet:Logging:ExpectedDeliveryMode"]=mode,
                ["FullNet:Logging:LocalFile:Enabled"]="true",
            });
            Assert.ThrowsExactly<OptionsValidationException>(() => builder.AddFullNetServiceDefaults());
        }
        foreach (var options in new[] {
            new LocalFileLoggingOptions { Enabled=true, Directory="" },
            new LocalFileLoggingOptions { Enabled=true, FileSizeLimitBytes=128 },
            new LocalFileLoggingOptions { Enabled=true, MaxDirectoryBytes=1 },
            new LocalFileLoggingOptions { Enabled=true, RetentionDays=0 },
            new LocalFileLoggingOptions { Enabled=true, PriorityReservePercent=-1 },
            new LocalFileLoggingOptions { Enabled=true, PriorityReservePercent=51 },
            new LocalFileLoggingOptions { Enabled=true, FlushInterval=TimeSpan.Zero },
        }) Assert.ThrowsExactly<OptionsValidationException>(() => options.Validate(new LoggingOptions { DeliveryMode=LoggingDeliveryMode.Local }));
    }

    [TestMethod]
    public void Existing_file_in_directory_path_is_a_startup_error()
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root,"not-a-directory"); File.WriteAllText(path,"keep");
            // Windows 与 Linux 可以抛出不同的 IOException 子类；两者都必须拒绝启动且保留既有文件。
            Assert.Throws<IOException>(() => new LocalFileLogStore(new LocalFileLoggingOptions {Directory=path},root,"Api"));
            Assert.AreEqual("keep",File.ReadAllText(path));
        });
    }

    [TestMethod]
    public void Disabled_file_storage_has_no_filesystem_side_effect_and_local_options_cannot_change_during_build()
    {
        WithDirectory(root =>
        {
            var path=Path.Combine(root,"disabled");
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings {EnvironmentName=Environments.Development});
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
                ["FullNet:Logging:DeliveryMode"]="Local", ["FullNet:Logging:ExpectedDeliveryMode"]="Local",
                ["FullNet:Logging:LocalFile:Directory"]=path,
            });
            builder.AddFullNetServiceDefaults();
            using (var host=builder.Build()) host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Probe").LogInformation("Disabled file probe");
            Assert.IsFalse(Directory.Exists(path));
            var changed = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings {EnvironmentName=Environments.Development});
            changed.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
                ["FullNet:Logging:DeliveryMode"]="Local", ["FullNet:Logging:ExpectedDeliveryMode"]="Local",
            });
            changed.AddFullNetServiceDefaults();
            changed.Configuration["FullNet:Logging:LocalFile:Enabled"]="true";
            Assert.ThrowsExactly<OptionsValidationException>(() => changed.Build());
        });
    }

    [TestMethod]
    public void File_count_bound_reclaims_closed_files_and_restart_reserves_two_active_slots()
    {
        WithDirectory(root =>
        {
            var options = new LocalFileLoggingOptions {Directory=root, FileSizeLimitBytes=256};
            var clock=new LocalClock();
            var store=new LocalFileLogStore(options,root,"Api",clock,maxManagedFiles:4);
            for(var i=0;i<12;i++) store.General.Emit(Line(200));
            store.General.Dispose();store.Priority.Dispose();
            Assert.IsTrue(Directory.GetFiles(store.DirectoryPath,"*.txt").Length<=4);
            var restarted=new LocalFileLogStore(options,root,"Api",clock,maxManagedFiles:4);
            restarted.Priority.Emit(Line(200));restarted.General.Dispose();restarted.Priority.Dispose();
            Assert.IsTrue(Directory.GetFiles(store.DirectoryPath,"*.txt").Length<=4);
        });
    }

    [TestMethod]
    public void Startup_rejects_directory_link_without_removing_target_files()
    {
        WithDirectory(root =>
        {
            var target=Path.Combine(root,"target");Directory.CreateDirectory(target);
            var foreign=Path.Combine(target,"keep.txt");File.WriteAllText(foreign,"keep");
            var link=Path.Combine(root,"link");Directory.CreateSymbolicLink(link,target);
            Assert.ThrowsExactly<IOException>(()=>new LocalFileLogStore(new LocalFileLoggingOptions {Directory=link},root,"Api"));
            Assert.AreEqual("keep",File.ReadAllText(foreign));
            Directory.Delete(link);
        });
    }

    [TestMethod]
    public void General_storm_preserves_closed_priority_history_within_default_reserve()
    {
        WithDirectory(root =>
        {
            var store = new LocalFileLogStore(new LocalFileLoggingOptions
                { Directory = root, FileSizeLimitBytes = 256, MaxDirectoryBytes = 8192 }, root, "Api", new LocalClock());
            try
            {
                store.Priority.Emit(Line(200)); store.Priority.Emit(Line(200)); store.Priority.Emit(Line(100));
                for (var index = 0; index < 100; index++) store.General.Emit(Line(200));
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.AreEqual(500L, Directory.GetFiles(store.DirectoryPath, "fullnet-priority-*.txt")
                .Sum(path => new FileInfo(path).Length));
            Assert.IsTrue(Directory.GetFiles(store.DirectoryPath, "*.txt").Sum(path => new FileInfo(path).Length) <= 8192);
        });
    }

    [TestMethod]
    public void Expiry_maintenance_is_bounded_and_progresses_without_advancing_the_clock_again()
    {
        WithDirectory(root =>
        {
            var directory = Path.Combine(root, "Api"); Directory.CreateDirectory(directory);
            for (var index = 0; index < 80; index++)
                File.WriteAllText(Path.Combine(directory, $"fullnet-general-20261010-{Guid.NewGuid():N}.txt"), "expired");
            var clock = new LocalClock();
            var store = new LocalFileLogStore(new LocalFileLoggingOptions { Directory = root, RetentionDays = 1 }, root, "Api", clock);
            try
            {
                store.Priority.Emit(Line(100));
                clock.Advance(TimeSpan.FromDays(2));
                store.Maintain();
                var remaining = Directory.GetFiles(directory, "fullnet-general-20261010-*.txt").Length - 1;
                Assert.IsTrue(remaining is >= 64 and < 80, "单次维护应有进展且最多删除 16 个闭合文件。");
                for (var pass = 0; pass < 8; pass++) store.Maintain();
                Assert.AreEqual(1, Directory.GetFiles(directory, "fullnet-general-20261010-*.txt").Length);
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.AreEqual(100L, Directory.GetFiles(directory, "fullnet-priority-*.txt").Sum(path => new FileInfo(path).Length));
        });
    }

    [TestMethod]
    public void Disabling_priority_reserve_allows_normal_age_based_quota_reclamation()
    {
        WithDirectory(root =>
        {
            var store = new LocalFileLogStore(new LocalFileLoggingOptions
                { Directory = root, FileSizeLimitBytes = 256, MaxDirectoryBytes = 8192, PriorityReservePercent = 0 }, root, "Api", new LocalClock());
            try
            {
                store.Priority.Emit(Line(200)); store.Priority.Emit(Line(200)); store.Priority.Emit(Line(100));
                for (var index = 0; index < 100; index++) store.General.Emit(Line(200));
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.AreEqual(100L, Directory.GetFiles(store.DirectoryPath, "fullnet-priority-*.txt").Sum(path => new FileInfo(path).Length));
        });
    }

    [TestMethod]
    public void Priority_reserve_does_not_extend_expiry_and_priority_writer_can_replace_its_own_history()
    {
        WithDirectory(root =>
        {
            var directory = Path.Combine(root, "Api"); Directory.CreateDirectory(directory);
            var old = Path.Combine(directory, $"fullnet-priority-20261010-{Guid.NewGuid():N}.txt");
            File.WriteAllText(old, "expired");
            var clock = new LocalClock();
            var store = new LocalFileLogStore(new LocalFileLoggingOptions
                { Directory = root, FileSizeLimitBytes = 256, MaxDirectoryBytes = 512, RetentionDays = 1, PriorityReservePercent = 50 }, root, "Api", clock);
            try
            {
                clock.Advance(TimeSpan.FromDays(2)); store.Maintain();
                Assert.IsFalse(File.Exists(old));
                for (var index = 0; index < 30; index++) store.Priority.Emit(Line(200));
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.IsTrue(Directory.GetFiles(directory, "*.txt").Sum(path => new FileInfo(path).Length) <= 512);
            Assert.AreEqual(400L, Directory.GetFiles(directory, "fullnet-priority-*.txt").Sum(path => new FileInfo(path).Length));
        });
    }

    [TestMethod]
    public void Priority_file_count_reserve_leaves_slots_for_general_rollover()
    {
        WithDirectory(root =>
        {
            var store = new LocalFileLogStore(new LocalFileLoggingOptions
                { Directory = root, FileSizeLimitBytes = 256, PriorityReservePercent = 50 }, root, "Api", new LocalClock(), maxManagedFiles: 4);
            try
            {
                for (var index = 0; index < 12; index++) store.Priority.Emit(Line(200));
                for (var index = 0; index < 12; index++) store.General.Emit(Line(200));
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.IsTrue(Directory.GetFiles(store.DirectoryPath, "*.txt").Length <= 4);
            Assert.AreEqual(2, Directory.GetFiles(store.DirectoryPath, "fullnet-priority-*.txt").Length);
            Assert.AreEqual(2, Directory.GetFiles(store.DirectoryPath, "fullnet-general-*.txt").Length);
        });
    }

    [TestMethod]
    public void Bounded_quota_reclaim_rejects_current_event_then_recovers_without_exceeding_directory_budget()
    {
        WithDirectory(root =>
        {
            var directory = Path.Combine(root, "Api"); Directory.CreateDirectory(directory);
            for (var index = 0; index < 100; index++)
                File.WriteAllText(Path.Combine(directory, $"fullnet-general-20261010-{Guid.NewGuid():N}.txt"), "seed");
            var store = new LocalFileLogStore(new LocalFileLoggingOptions
                { Directory = root, FileSizeLimitBytes = 256, MaxDirectoryBytes = 512 }, root, "Api", new LocalClock());
            try
            {
                store.General.Emit(Line(100));
                Assert.ThrowsExactly<IOException>(() => store.General.Emit(Line(200)));
                var written = false;
                for (var attempt = 0; attempt < 10 && !written; attempt++)
                {
                    try { store.General.Emit(Line(200)); written = true; }
                    catch (IOException) { }
                }
                Assert.IsTrue(written, "有限清理必须逐次释放空间，不能永久拒绝后续事件。");
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.IsTrue(Directory.GetFiles(directory, "*.txt").Sum(path => new FileInfo(path).Length) <= 512);
        });
    }

    [TestMethod]
    public void Changing_priority_reserve_after_logging_registration_requires_restart()
    {
        WithDirectory(root =>
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Logging:DeliveryMode"] = "Local", ["FullNet:Logging:ExpectedDeliveryMode"] = "Local",
                ["FullNet:Logging:LocalFile:Enabled"] = "true", ["FullNet:Logging:LocalFile:Directory"] = root,
            });
            builder.AddFullNetServiceDefaults();
            builder.Configuration["FullNet:Logging:LocalFile:PriorityReservePercent"] = "40";
            Assert.ThrowsExactly<OptionsValidationException>(() => builder.Build());
            Assert.AreEqual(0, Directory.GetDirectories(root).Length);
        });
    }

    [TestMethod]
    public void Whole_file_priority_protection_still_leaves_room_for_one_maximum_general_event()
    {
        WithDirectory(root =>
        {
            var store = new LocalFileLogStore(new LocalFileLoggingOptions
                { Directory = root, FileSizeLimitBytes = 256, MaxDirectoryBytes = 512, PriorityReservePercent = 50 }, root, "Api", new LocalClock());
            try
            {
                store.Priority.Emit(Line(200)); store.Priority.Emit(Line(200));
                store.General.Emit(Line(256));
            }
            finally { store.General.Dispose(); store.Priority.Dispose(); }
            Assert.AreEqual(256L, Directory.GetFiles(store.DirectoryPath, "fullnet-general-*.txt").Sum(path => new FileInfo(path).Length));
            Assert.AreEqual(200L, Directory.GetFiles(store.DirectoryPath, "fullnet-priority-*.txt").Sum(path => new FileInfo(path).Length));
        });
    }

    private static LogEnvelope Line(int length) => new(Encoding.UTF8.GetBytes(new string('x', length-1)+"\n"));
    private static void WithDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(),"fullnet-file-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root,true); }
    }
    private sealed class LocalClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026,10,10,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _now.UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
