using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Migrator;

/// <summary>复用一次性迁移宿主生命周期；模块由应用自己的Composition显式装配。</summary>
/// <remarks>只允许Migrator消费，API和Worker不得获得迁移或播种执行能力。</remarks>
public static class FullNetMigratorHost
{
    /// <summary>创建已装配迁移基础设施的构建器，不自动装配业务模块。</summary>
    /// <param name="arguments">宿主及播种命令行参数。</param>
    /// <returns>由应用继续装配Migrator Profile的构建器。</returns>
    public static HostApplicationBuilder CreateBuilder(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var builder = Host.CreateApplicationBuilder(arguments);
        builder.AddFullNetServiceDefaults();
        builder.Services.AddFullNetDapper(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        builder.Services.AddRouting();
        builder.Services.AddFullNetCaching(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        builder.Services.AddFullNetMemoryPack();
        builder.Services.AddFullNetMigrations(builder.Configuration);
        builder.Services.AddFullNetSeeding(builder.Configuration);
        builder.Services.AddScoped<MigratorWorkflow>();
        return builder;
    }

    /// <summary>拥有一次性宿主，先迁移再显式播种，最终停止服务并释放作用域和宿主。</summary>
    /// <param name="builder">已装配Migrator Profile的构建器。</param>
    /// <param name="arguments">既有播种命令行参数。</param>
    /// <param name="cancellationToken">调用方停止令牌。</param>
    /// <remarks>构建器单次使用；参数或DI构建异常保留异常语义，不作为工作流错误吞掉。</remarks>
    /// <returns>成功为0；工作流失败或运行期取消为1，并向标准错误输出稳定机器码。</returns>
    public static async Task<int> RunAsync(HostApplicationBuilder builder,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(arguments);
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Full.NET.Host.Migrator");
        var stopRequired = false;
        var exitCode = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 部分服务可能在StartAsync失败前已经启动，同样必须进入停机流程。
            stopRequired = true;
            await host.StartAsync(cancellationToken);
            var applicationStopping = host.Services
                .GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping;
            // 同时响应进程停止与调用方取消，不能只传播其中一个令牌。
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, applicationStopping);
            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var result = await scope.ServiceProvider
                .GetRequiredService<MigratorWorkflow>()
                .RunAsync(arguments, stopping.Token);

            logger.LogInformation(
                "Database migration completed with {ExecutedScriptCount} executed scripts",
                result.ExecutedScriptCount);
            if (result.UsesLegacyAlias)
            {
                logger.LogWarning(
                    "Seed CLI alias --seed-local is deprecated; use --seed development");
            }

            if (result.SeedProfile.HasValue)
            {
                logger.LogInformation(
                    "Seed profile {SeedProfile} completed",
                    result.SeedProfile.Value.ToCanonicalName());
            }

        }
        catch (MigratorWorkflowException exception)
        {
            if (exception.InnerException is null)
            {
                logger.LogCritical(
                    "Migrator workflow failed with {ErrorCode}",
                    exception.Code);
            }
            else
            {
                logger.LogCritical(
                    exception.InnerException,
                    "Migrator workflow failed with {ErrorCode}",
                    exception.Code);
            }

            Console.Error.WriteLine(exception.Code);
            exitCode = 1;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogCritical(
                exception,
                "Migrator workflow failed with {ErrorCode}",
                MigratorErrorCodes.ExecutionCancelled);
            Console.Error.WriteLine(MigratorErrorCodes.ExecutionCancelled);
            exitCode = 1;
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Migrator workflow failed with {ErrorCode}",
                MigratorErrorCodes.ExecutionFailed);
            Console.Error.WriteLine(MigratorErrorCodes.ExecutionFailed);
            exitCode = 1;
        }
        finally
        {
            if (stopRequired)
            {
                try
                {
                    // 工作流取消不能取消停机清理；宿主自身的ShutdownTimeout仍然有效。
                    await host.StopAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogCritical(exception, "Migrator host shutdown failed");
                    // 已有工作流错误优先，停机异常不得覆盖其稳定机器码。
                    if (exitCode == 0)
                    {
                        Console.Error.WriteLine(MigratorErrorCodes.ExecutionFailed);
                        exitCode = 1;
                    }
                }
            }
        }

        return exitCode;
    }
}
