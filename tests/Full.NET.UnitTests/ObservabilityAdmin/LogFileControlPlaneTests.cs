using System.Text;
using System.Diagnostics;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.ObservabilityAdmin;

/// <summary>单元测试（可观测管理）：Log File Control Plane。</summary>
[TestClass]
public sealed class LogFileControlPlaneTests
{
    [TestMethod]
    public async Task Local_writer_and_all_read_operations_share_the_same_application_directory()
    {
        using var directory = new TemporaryLogDirectory();
        var logging = new LoggingOptions { DeliveryMode = LoggingDeliveryMode.Local,
            LocalFile = new LocalFileLoggingOptions { Enabled = true, Directory = "local-output" } };
        var environment = new TestHostEnvironment { ContentRootPath = directory.Path, ApplicationName = "Full.NET.Host.Api" };
        using (var store = new LocalFileLogStore(logging.LocalFile, directory.Path, environment.ApplicationName))
        {
            store.General.Emit(new LogEnvelope(Encoding.UTF8.GetBytes("{\"message\":\"local-file-proof\"}\n")));
            store.General.Dispose();
            store.Priority.Dispose();
        }
        var control = new LogFileControlPlane(Options.Create(new ObservabilityAdminOptions()), environment, logging);
        var listing = control.List();
        Assert.IsFalse(listing.IsTruncated);
        Assert.HasCount(2, listing.Items);
        var general = listing.Items.Single(item => item.FileName.Contains("-general-", StringComparison.Ordinal));
        var tail = await control.ReadTailAsync(general.Id, null, null, CancellationToken.None);
        StringAssert.Contains(tail!.Content, "local-file-proof");
        var download = control.OpenDownload(general.Id)!;
        using (download.Content)
        using (var reader = new StreamReader(download.Content))
            StringAssert.Contains(await reader.ReadToEndAsync(), "local-file-proof");
        var timeline = await control.ReadTimelineTailsAsync(CancellationToken.None);
        Assert.IsTrue(timeline.Any(item => item.Content.Contains("local-file-proof", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Directory_scan_counts_non_log_entries_and_observes_cancellation()
    {
        using var directory = new TemporaryLogDirectory();
        for (var i = 0; i < 20; i++) directory.Write($"other-{i}.json", "ignored");
        var control = CreateControlPlane(directory.Path, maximumScanEntries: 3);
        var result = control.List();
        Assert.AreEqual(3, result.ScannedEntries);
        Assert.IsTrue(result.IsTruncated);
        Assert.IsEmpty(result.Items);
        Assert.ThrowsExactly<OperationCanceledException>(() => control.List(new CancellationToken(true)));
    }

    [TestMethod]
    public void Disabled_local_without_explicit_read_directory_does_not_create_or_scan_directories()
    {
        using var directory = new TemporaryLogDirectory();
        var environment = new TestHostEnvironment { ContentRootPath = directory.Path };
        var control = new LogFileControlPlane(Options.Create(new ObservabilityAdminOptions()), environment, new LoggingOptions());
        Assert.IsEmpty(control.List().Items);
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "App_Data")));
    }

    [TestMethod]
    public async Task Timeline_tail_accepts_local_txt_and_limits_opened_files()
    {
        using var directory = new TemporaryLogDirectory();
        for (var index = 0; index < 12; index++) directory.Write($"fullnet-general-20261017-{index:x32}.txt", "structured-line");
        directory.Write("ignored.json", "not-a-log");
        var controlPlane = CreateControlPlane(directory.Path);
        var tails = await controlPlane.ReadTimelineTailsAsync(CancellationToken.None);
        Assert.HasCount(10, tails);
        Assert.IsTrue(tails.All(tail => tail.FileName.EndsWith(".txt", StringComparison.Ordinal)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await controlPlane.ReadTimelineTailsAsync(cancellation.Token));
    }

    [TestMethod]
    public void List_returns_only_bounded_top_level_log_files_with_stable_ids()
    {
        using var directory = new TemporaryLogDirectory();
        directory.Write("api.log", "api");
        directory.Write("worker.log", "worker");
        directory.Write("ignored.txt", "secret");
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        File.WriteAllText(Path.Combine(directory.Path, "nested", "nested.log"), "nested");

        var controlPlane = CreateControlPlane(directory.Path, maximumListFiles: 1);
        var first = controlPlane.List().Items;
        var second = controlPlane.List().Items;

        Assert.HasCount(1, first);
        Assert.IsTrue(first[0].FileName.EndsWith(".log", StringComparison.Ordinal));
        Assert.AreEqual(first[0].Id, second[0].Id);
        Assert.IsFalse(first[0].Id.Contains(directory.Path, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Tail_honors_line_and_byte_limits_while_file_is_open_for_writing()
    {
        using var directory = new TemporaryLogDirectory();
        var filePath = directory.Write("api.log", string.Empty);
        await using var writer = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        await writer.WriteAsync(Encoding.UTF8.GetBytes("one\ntwo\nthree\nfour\n"));
        await writer.FlushAsync();

        var controlPlane = CreateControlPlane(directory.Path, maximumTailBytes: 16);
        var files = controlPlane.List().Items;
        Assert.HasCount(1, files);
        var file = files[0];
        var tail = await controlPlane.ReadTailAsync(file.Id, 2, 16, CancellationToken.None);

        Assert.IsNotNull(tail);
        Assert.AreEqual("three\nfour", tail.Content);
        Assert.IsLessThanOrEqualTo(tail.BytesRead, 16);
    }

    [TestMethod]
    public void Unknown_or_path_like_ids_never_resolve_outside_the_configured_root()
    {
        using var directory = new TemporaryLogDirectory();
        directory.Write("api.log", "api");
        var controlPlane = CreateControlPlane(directory.Path);

        Assert.IsNull(controlPlane.OpenDownload("../api.log"));
        Assert.IsNull(controlPlane.OpenDownload("api.log"));
        Assert.IsNull(controlPlane.OpenDownload(new string('a', 64)));
    }

    [TestMethod]
    public async Task Missing_or_rotated_files_return_empty_results_instead_of_throwing()
    {
        using var directory = new TemporaryLogDirectory();
        var path = directory.Write("api.log", "api");
        var controlPlane = CreateControlPlane(directory.Path);
        var id = controlPlane.List().Items.Single().Id;
        File.Delete(path);

        Assert.IsNull(await controlPlane.ReadTailAsync(id, null, null, CancellationToken.None));
        Assert.IsNull(controlPlane.OpenDownload(id));

        var missingRoot = Path.Combine(directory.Path, "missing");
        var missingControlPlane = CreateControlPlane(missingRoot);
        Assert.IsEmpty(missingControlPlane.List().Items);
    }

    [TestMethod]
    public void Symbolic_links_are_not_exposed_when_the_platform_allows_creating_them()
    {
        using var directory = new TemporaryLogDirectory();
        var target = directory.Write("target.txt", "secret");
        var link = Path.Combine(directory.Path, "linked.log");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Inconclusive("当前运行账户无权创建符号链接。");
        }
        catch (IOException)
        {
            Assert.Inconclusive("当前文件系统不支持符号链接测试。");
        }

        Assert.IsEmpty(CreateControlPlane(directory.Path).List().Items);
    }

    [TestMethod]
    public void File_replaced_by_symbolic_link_after_validation_is_not_opened()
    {
        using var directory = new TemporaryLogDirectory();
        var path = directory.Write("api.log", "safe");
        var target = directory.Write("target.txt", "secret");
        var replaced = false;
        var controlPlane = CreateControlPlane(
            directory.Path,
            beforeOpenTestHook: candidatePath =>
            {
                File.Delete(candidatePath);
                try
                {
                    File.CreateSymbolicLink(candidatePath, target);
                    replaced = true;
                }
                catch (UnauthorizedAccessException)
                {
                    Assert.Inconclusive("当前运行账户无权创建符号链接。");
                }
                catch (IOException)
                {
                    Assert.Inconclusive("当前文件系统不支持符号链接测试。");
                }
            });
        var id = controlPlane.List().Items.Single(file => file.FileName == Path.GetFileName(path)).Id;

        Assert.IsNull(controlPlane.OpenDownload(id));
        Assert.IsTrue(replaced);
    }

    [TestMethod]
    public void Root_replaced_by_symbolic_link_after_enumeration_cannot_redirect_an_open()
    {
        using var directory = new TemporaryLogDirectory();
        directory.Write("api.log", "safe");
        var outside = Path.Combine(directory.BasePath, "outside");
        var original = Path.Combine(directory.BasePath, "logs-original");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "api.log"), "secret", Encoding.UTF8);
        var replaced = false;
        var controlPlane = CreateControlPlane(
            directory.Path,
            beforeOpenTestHook: _ =>
            {
                Directory.Move(directory.Path, original);
                try
                {
                    Directory.CreateSymbolicLink(directory.Path, outside);
                    replaced = true;
                }
                catch (UnauthorizedAccessException)
                {
                    Directory.Move(original, directory.Path);
                    Assert.Inconclusive("当前运行账户无权创建目录符号链接。");
                }
                catch (IOException)
                {
                    Directory.Move(original, directory.Path);
                    Assert.Inconclusive("当前文件系统不支持目录符号链接测试。");
                }
            });
        var id = controlPlane.List().Items.Single().Id;

        try
        {
            Assert.IsNull(controlPlane.OpenDownload(id));
            Assert.IsTrue(replaced);
        }
        finally
        {
            if (replaced)
            {
                Directory.Delete(directory.Path);
                Directory.Move(original, directory.Path);
            }
        }
    }

    [TestMethod]
    public void Linux_file_replaced_by_fifo_after_validation_is_rejected_without_blocking()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("FIFO 句柄回归仅在 Linux 执行。");
        }

        using var directory = new TemporaryLogDirectory();
        directory.Write("api.log", "safe");
        var controlPlane = CreateControlPlane(
            directory.Path,
            beforeOpenTestHook: candidatePath =>
            {
                File.Delete(candidatePath);
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "mkfifo",
                        UseShellExecute = false,
                    },
                };
                process.StartInfo.ArgumentList.Add(candidatePath);
                if (!process.Start() || !process.WaitForExit(5_000) || process.ExitCode != 0)
                {
                    Assert.Inconclusive("当前 Linux 环境无法创建 FIFO。");
                }
            });
        var id = controlPlane.List().Items.Single().Id;

        Assert.IsNull(controlPlane.OpenDownload(id));
    }

    [TestMethod]
    [DataRow(101, 5_000, 1024 * 1024)]
    [DataRow(100, 5_001, 1024 * 1024)]
    [DataRow(100, 5_000, 1024 * 1024 + 1)]
    public void Options_validator_rejects_values_above_the_approved_static_ceiling(
        int maximumListFiles,
        int maximumTailLines,
        int maximumTailBytes)
    {
        var validator = new ObservabilityAdminOptionsValidator();
        var result = validator.Validate(
            null,
            new ObservabilityAdminOptions
            {
                MaximumListFiles = maximumListFiles,
                MaximumTailLines = maximumTailLines,
                MaximumTailBytes = maximumTailBytes,
            });

        Assert.IsTrue(result.Failed);
    }

    private static LogFileControlPlane CreateControlPlane(
        string root,
        int maximumListFiles = 100,
        int maximumTailBytes = 1024 * 1024,
        Action<string>? beforeOpenTestHook = null,
        int maximumScanEntries = 1000)
    {
        var contentRoot = Directory.Exists(root)
            ? root
            : Path.GetDirectoryName(root)
                ?? throw new AssertFailedException("测试日志目录缺少父目录。");
        var environment = new TestHostEnvironment
        {
            ContentRootPath = contentRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
        };
        return new LogFileControlPlane(
            Options.Create(new ObservabilityAdminOptions
            {
                LogRootPath = root,
                MaximumListFiles = maximumListFiles,
                MaximumScanEntries = maximumScanEntries,
                MaximumTailBytes = maximumTailBytes,
            }),
            environment, new Full.NET.Hosting.Observability.LoggingOptions(),
            beforeOpenTestHook);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Full.NET.UnitTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class TemporaryLogDirectory : IDisposable
    {
        public TemporaryLogDirectory()
        {
            BasePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fullnet-observability-{Guid.NewGuid():N}");
            Path = System.IO.Path.Combine(BasePath, "logs");
            Directory.CreateDirectory(Path);
        }

        public string BasePath { get; }

        public string Path { get; }

        public string Write(string fileName, string content)
        {
            var path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }

        public void Dispose() => Directory.Delete(BasePath, recursive: true);
    }
}
