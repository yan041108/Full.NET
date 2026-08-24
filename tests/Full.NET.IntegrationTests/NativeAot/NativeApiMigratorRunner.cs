using System.Diagnostics;
using Full.NET.Data.Abstractions;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 通过 JIT Migrator 子进程准备数据库 schema，满足 Native E2E 的数据库前置条件。
/// </summary>
internal static class NativeApiMigratorRunner
{
    public static async Task MigrateAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = NativeApiArtifactLocator.FindRepositoryRoot();
        var migratorProject = Path.Combine(
            repositoryRoot,
            "src",
            "Hosts",
            "Full.NET.Host.Migrator",
            "Full.NET.Host.Migrator.csproj");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repositoryRoot,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(migratorProject);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("migrate");
        startInfo.ArgumentList.Add("--seed");
        startInfo.ArgumentList.Add(SeedProfile.Development.ToCanonicalName());

        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment[$"{DatabaseOptions.SectionName}__Provider"] =
            provider.ToString();
        startInfo.Environment[$"{DatabaseOptions.SectionName}__ConnectionString"] =
            connectionString;
        startInfo.Environment[$"{DatabaseOptions.SectionName}__CommandTimeoutSeconds"] =
            "300";
        startInfo.Environment[$"{DatabaseOptions.SectionName}__MySqlGuidStorageMode"] =
            "Binary16";
        startInfo.Environment["Identity__Bootstrap__Username"] = "admin";
        startInfo.Environment["Identity__Bootstrap__Password"] =
            NativeApiE2EAssertions.AdminPassword;
        startInfo.Environment["Identity__Bootstrap__DisplayName"] = "系统管理员";
        startInfo.Environment["Identity__AllowDevelopmentEphemeralSigningKey"] = "true";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 JIT Migrator。");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"JIT Migrator 退出码 {process.ExitCode}。stderr: {stderr}\nstdout: {stdout}");
        }
    }
}
