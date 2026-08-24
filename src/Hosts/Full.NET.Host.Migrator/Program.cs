using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Host.Migrator;
using Full.NET.Hosting.Observability;
using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
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
builder.Services.AddFullNetApplicationModules(
    builder.Configuration,
    FullNetHostProfile.Migrator);
builder.Services.AddScoped<MigratorWorkflow>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Full.NET.Host.Migrator");
try
{
    await host.StartAsync();
    var applicationStopping = host.Services
        .GetRequiredService<IHostApplicationLifetime>()
        .ApplicationStopping;
    await using var scope = host.Services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
    var result = await scope.ServiceProvider
        .GetRequiredService<MigratorWorkflow>()
        .RunAsync(args, applicationStopping);

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

    await host.StopAsync();
    return 0;
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
    return 1;
}
catch (OperationCanceledException exception)
{
    logger.LogCritical(
        exception,
        "Migrator workflow failed with {ErrorCode}",
        MigratorErrorCodes.ExecutionCancelled);
    Console.Error.WriteLine(MigratorErrorCodes.ExecutionCancelled);
    return 1;
}
catch (Exception exception)
{
    logger.LogCritical(
        exception,
        "Migrator workflow failed with {ErrorCode}",
        MigratorErrorCodes.ExecutionFailed);
    Console.Error.WriteLine(MigratorErrorCodes.ExecutionFailed);
    return 1;
}

/// <summary>
/// Full.NET Migrator 宿主入口；一次性进程，无 HTTP 管道。
/// </summary>
/// <remarks>
/// 装配顺序：ServiceDefaults → Dapper/Caching/MemoryPack → Migrations/Seeding →
/// <see cref="FullNetHostProfile.Migrator"/> 模块迁移能力（仅 <c>AddMigrationServices</c>）→ <c>MigratorWorkflow</c>。
/// <para>工作流顺序：迁移 Profile 最小闭包装配 → DbUp 执行迁移脚本 → 迁移成功后才按 CLI 运行可选 Seed Profile →
/// 输出执行脚本数与 Seed 摘要审计；失败以稳定错误码退出。</para>
/// </remarks>
public partial class Program;
