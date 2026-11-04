using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>固定办理操作的当前主体及凭据身份，不把入口权限声明复用为提交授权。</summary>
internal sealed record WorkflowTodoActor(Guid UserId,Guid? TenantId,Guid CredentialId,bool IsApiKey);
internal interface IWorkflowTodoActorAuthorization
{
    Task<WorkflowTodoActor?> AuthorizeAsync(Guid userId,IReadOnlyList<string> permissions,CancellationToken cancellationToken=default);
}

/// <summary>交互会话与现有 Host API Key 分别通过 Identity 合同独立读取，不加入 Workflow 本地事务。</summary>
internal sealed class WorkflowTodoActorAuthorization(IIndependentCurrentSessionAuthorization sessions,
    ICurrentTenant tenant,IHttpContextAccessor http,IServiceScopeFactory scopes,IDatabaseAuthorizationReadScope capacity):IWorkflowTodoActorAuthorization
{
    public async Task<WorkflowTodoActor?> AuthorizeAsync(Guid userId,IReadOnlyList<string> permissions,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(permissions);var required=permissions.ToArray();
        var context=http.HttpContext;var principal=context?.User;var tenantId=tenant.Id;var host=tenant.IsHost;
        if(userId==Guid.Empty||required.Length==0||required.Any(string.IsNullOrWhiteSpace)||principal?.Identity?.IsAuthenticated!=true
            ||!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject),out var subject)||subject!=userId
            ||tenantId==Guid.Empty||tenantId.HasValue&&host||!tenantId.HasValue&&!host)return null;
        bool Current()=>ReferenceEquals(http.HttpContext,context)&&ReferenceEquals(context!.User,principal)&&tenant.Id==tenantId&&tenant.IsHost==host
            &&Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.Subject),out var current)&&current==userId;
        if(!principal.HasClaim(claim=>claim.Type==FullNetIdentityClaimTypes.ApiKeyId))
        {
            var actor=await sessions.AuthorizeAllAsync(required,cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Current()&&actor?.UserId==userId&&actor.TenantId==tenantId?new(userId,tenantId,actor.SessionId,false):null;
        }
        if(!Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApiKeyId),out var keyId)||keyId==Guid.Empty)return null;
        if(!capacity.HasIndependentReadCapacity)throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected,TimeSpan.FromMilliseconds(250));
        await using var scope=scopes.CreateAsyncScope();
        if(scope.ServiceProvider.GetRequiredService<IDataTransactionState>().HasTransaction
            ||ReferenceEquals(scope.ServiceProvider.GetRequiredService<ICurrentTenant>(),tenant))
            throw new InvalidOperationException("Workflow credential authorization requires an independent data and tenant scope.");
        using var admission=scope.ServiceProvider.GetRequiredService<IDatabaseAuthorizationReadScope>().Enter();
        var writer=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        if(tenantId is { } id)writer.SetTenant(new(id,tenant.Identifier??id.ToString("N"),tenant.Name??""));else writer.SetHost();
        try
        {
            var current=await scope.ServiceProvider.GetRequiredService<ICurrentApiKeyAuthorization>().AuthorizeAsync(required,cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Current()&&current==userId&&Guid.TryParse(principal.FindFirstValue(FullNetIdentityClaimTypes.ApiKeyId),out var stillKey)&&stillKey==keyId?
                new(userId,tenantId,keyId,true):null;
        }
        finally{writer.Clear();}
    }
}
