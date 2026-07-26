namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class TrustedProxyBoundaryTests
{
    [TestMethod]
    public void Production_code_does_not_parse_forwarding_headers_outside_hosting_boundary()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("X-Forwarded-For", StringComparison.Ordinal)
                    || source.Contains("X-Forwarded-Proto", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(FindRepositoryRoot(), path)
                .Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length > 0)
        {
            Assert.Fail(
                "生产代码不得直接解析 X-Forwarded-*；统一读取可信代理中间件规范化后的 "
                + "Connection 信息。违规文件: "
                + string.Join(", ", violations));
        }
    }

    [TestMethod]
    public void Trusted_proxy_forwarding_runs_before_address_and_scheme_consumers()
    {
        var programPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hosts",
            "Full.NET.Host.Api",
            "Program.cs");
        var source = File.ReadAllText(programPath);
        var forwardingIndex = source.IndexOf(
            "app.UseFullNetTrustedProxyForwarding();",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(
            0,
            forwardingIndex,
            "API 必须启用可信代理转发中间件。");

        foreach (var consumer in new[]
                 {
                     "app.UseFullNetLocalization();",
                     "app.UseFullNetRequestLogging();",
                     "app.UseCors(",
                     "app.UseRateLimiter();",
                     "app.UseAuthentication();",
                     "app.UseAuthorization();",
                     "app.MapFullNetOpenApi();",
                     "app.MapScalarApiReference(",
                     "app.MapFullNetHealthEndpoints();",
                     "app.MapFullNetRealtime();",
                     "app.MapFullNetModules();",
                 })
        {
            var consumerIndex = source.IndexOf(consumer, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(
                0,
                consumerIndex,
                $"API 管道缺少预期消费者: {consumer}");
            Assert.IsLessThan(
                consumerIndex,
                forwardingIndex,
                $"可信代理转发必须位于 {consumer} 之前。");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 Full.NET 仓库根目录。");
    }
}
