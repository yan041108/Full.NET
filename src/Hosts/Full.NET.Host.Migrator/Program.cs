using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Migrations.DbUp;
using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetModularity();
builder.Services.AddFullNetDapper(builder.Configuration);
builder.Services.AddFullNetMessagePack();
builder.Services.AddFullNetMigrations();
builder.Services.AddFullNetModule<IdentityModule>(builder.Configuration);
builder.Services.AddFullNetModule<TenancyModule>(builder.Configuration);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Full.NET.Host.Migrator");
try
{
    await host.StartAsync();
    await using var scope = host.Services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
    var migration = await scope.ServiceProvider
        .GetRequiredService<IDatabaseMigrationRunner>()
        .MigrateAsync();
    logger.LogInformation(
        "Database migration completed with {ExecutedScriptCount} executed scripts",
        migration.ExecutedScriptCount);

    if (args.Contains("--seed-local", StringComparer.OrdinalIgnoreCase))
    {
        var result = await scope.ServiceProvider
            .GetRequiredService<ITenantProvisioningService>()
            .ProvisionAsync(new ProvisionTenantRequest(
                "local",
                "Full.NET Local",
                "localhost"));
        if (!result.IsSuccess
            && result.Error?.Code is not "tenancy.identifier-exists"
            && result.Error?.Code is not "tenancy.domain-exists")
        {
            throw new InvalidOperationException(
                $"Local tenant seed failed: {result.Error?.Code} - {result.Error?.Message}");
        }

        logger.LogInformation(
            result.IsSuccess
                ? "Local tenant seed completed"
                : "Local tenant already exists; seed skipped");
    }

    await host.StopAsync();
    return 0;
}
catch (Exception exception)
{
    logger.LogCritical(exception, "Database migration failed");
    return 1;
}
