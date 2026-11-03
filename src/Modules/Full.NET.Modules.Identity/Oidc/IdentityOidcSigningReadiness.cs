using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>就绪探针验证材料并续租；未知或过期节点声明不参与签名轮转门禁。</summary>
internal sealed class IdentityOidcSigningReadiness(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            await scope.ServiceProvider.GetRequiredService<IdentityOidcKeyGovernance>().ReportNodeAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return HealthCheckResult.Unhealthy("OIDC signing material or governance state is unavailable."); }
        finally { tenant.Clear(); }
    }
}
