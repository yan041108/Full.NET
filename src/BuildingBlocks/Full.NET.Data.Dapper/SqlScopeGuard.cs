using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

internal static class SqlScopeGuard
{
    public static void Validate(SqlStatement statement, ICurrentTenant currentTenant)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(currentTenant);

        switch (statement.Scope)
        {
            case SqlDataScope.TenantRequired:
                if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
                {
                    throw new TenantContextMissingException(statement.Name);
                }

                if (statement.TenantBinding != SqlTenantBinding.CurrentTenantId
                    || !statement.Text.Contains("@TenantId", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TenantScopeViolationException(statement.Name);
                }

                break;

            case SqlDataScope.HostOnly when !currentTenant.IsAvailable || !currentTenant.IsHost:
                throw new HostContextRequiredException(statement.Name);

            case SqlDataScope.Global:
            case SqlDataScope.HostOnly:
                if (statement.TenantBinding != SqlTenantBinding.None)
                {
                    throw new TenantScopeViolationException(statement.Name);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(statement), statement.Scope, "Unknown SQL data scope.");
        }
    }
}
