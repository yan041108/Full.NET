using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ChangeTenantContext;

internal sealed class Handler(
    ITenantResolver tenantResolver,
    IIdentitySessionContextService identitySessionContextService)
    : ICommandHandler<Command, TenantContextTokenResponse>
{
    public async Task<Result<TenantContextTokenResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        VerifiedTenantContext? verifiedTenant = null;
        if (command.TenantId.HasValue)
        {
            var tenant = await tenantResolver.ResolveByIdAsync(
                    command.TenantId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            if (tenant is not { IsActive: true })
            {
                return Result<TenantContextTokenResponse>.Failure(new Error(
                    Code: TenancyErrorCodes.ContextNotFound,
                    Message: "The requested tenant context was not found.",
                    Type: ErrorType.NotFound));
            }

            verifiedTenant = new VerifiedTenantContext(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                tenant.Domain);
        }

        return await identitySessionContextService.ChangeAsync(
                command.Principal,
                verifiedTenant,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
