using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenancySaasDefaultsBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<TenancyCommercialOptions> commercialOptions) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var targetPhase = commercialOptions.Value.BootstrapEntitlementEnforcementPhase?.Trim();
        if (string.IsNullOrEmpty(targetPhase)
            || !string.Equals(
                targetPhase,
                TenantEntitlementEnforcementPhases.Enforced,
                StringComparison.Ordinal))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(
                TenantEntitlementSql.GetEnforcementPhase,
                Persistence.TenancySqlParameters.Create(
                    ("SettingsId", TenancySettingsConstants.SettingsId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null
            || !string.Equals(
                row.Phase,
                TenantEntitlementEnforcementPhases.Compatibility,
                StringComparison.Ordinal))
        {
            return;
        }

        await commandExecutor.ExecuteAsync(
                TenantEntitlementSql.UpdateEnforcementPhase,
                Persistence.TenancySqlParameters.Create(
                    ("SettingsId", TenancySettingsConstants.SettingsId),
                    ("Phase", targetPhase),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}