using System.Text;
using System.Diagnostics;
using Full.NET.Modules.ObservabilityAdmin.Configuration;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.ObservabilityAdmin;

[TestClass]
public sealed class LogFileControlPlaneTests
{
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
        var first = controlPlane.List();
        var second = controlPlane.List();

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
        var files = controlPlane.List();
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
        var id = controlPlane.List().Single().Id;
        File.Delete(path);

        Assert.IsNull(await controlPlane.ReadTailAsync(id, null, null, CancellationToken.None));
        Assert.IsNull(controlPlane.OpenDownload(id));

        var missingRoot = Path.Combine(directory.Path, "missing");
        var missingControlPlane = CreateControlPlane(missingRoot);
        Assert.IsEmpty(missingControlPlane.List());
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

        Assert.IsEmpty(CreateControlPlane(directory.Path).List());
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
        var id = controlPlane.List().Single(file => file.FileName == Path.GetFileName(path)).Id;

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
        var id = controlPlane.List().Single().Id;

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
        var id = controlPlane.List().Single().Id;

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
        Action<string>? beforeOpenTestHook = null)
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
                MaximumTailBytes = maximumTailBytes,
            }),
            environment,
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
