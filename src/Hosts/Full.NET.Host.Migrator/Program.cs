using Full.NET.Abstractions.Tenancy;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Host.Migrator;
using Full.NET.Hosting.Observability;
using Full.NET.Migrations.DbUp;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetMessagePack();
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
