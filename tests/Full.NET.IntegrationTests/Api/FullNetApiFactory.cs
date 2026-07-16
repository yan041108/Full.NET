using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

internal sealed class FullNetApiFactory(
    DatabaseProvider provider,
    string connectionString) : WebApplicationFactory<Program>
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hosts",
            "Full.NET.Host.Api"));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            }));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            using var bootstrapClient = CreateClient();
            await using var scope = Services.CreateAsyncScope();
            var currentTenant = scope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>();
            currentTenant.SetHost();
            try
            {
                await scope.ServiceProvider
                    .GetRequiredService<IDatabaseMigrationRunner>()
                    .MigrateAsync(cancellationToken);
                var result = await scope.ServiceProvider
                    .GetRequiredService<ITenantProvisioningService>()
                    .ProvisionAsync(
                        new ProvisionTenantRequest(
                            "acme",
                            "Acme Corporation",
                            "acme.localhost"),
                        cancellationToken);
                if (!result.IsSuccess
                    && result.Error?.Code is not "tenancy.identifier-exists"
                    && result.Error?.Code is not "tenancy.domain-exists")
                {
                    throw new InvalidOperationException(
                        $"Test tenant provisioning failed: {result.Error?.Code} - "
                        + result.Error?.Message);
                }
            }
            finally
            {
                currentTenant.Clear();
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public HttpClient CreateClientForHost(string host)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _initializationLock.Dispose();
        }

        base.Dispose(disposing);
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

        throw new DirectoryNotFoundException(
            "Could not locate the Full.NET repository root.");
    }
}
