using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>凭据复核保留交互与现有 API Key 两类身份，并冻结原主体、上下文和全部精确权限。</summary>
[TestClass]
public sealed class WorkflowTodoActorAuthorizationTests
{
    [TestMethod]
    [DataRow("ok")][DataRow("denied")][DataRow("wrong_user")][DataRow("context")][DataRow("principal")]
    public async Task Interactive_actor_uses_one_bulk_authority_read_and_rejects_changed_identity(string scenario)
    {
        var user=Guid.CreateVersion7();var session=Guid.CreateVersion7();var tenant=new CurrentTenantAccessor();tenant.SetHost();
        var http=new HttpContextAccessor{HttpContext=new DefaultHttpContext{User=Principal(user,null)}};
        var authority=Substitute.For<IIndependentCurrentSessionAuthorization>();string[] permissions=["workflow.todos.approve","workflow.todos.sign"];
        authority.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>()).Returns(_=>{
            if(scenario=="context")tenant.SetTenant(new(Guid.CreateVersion7(),"changed","Changed"));
            if(scenario=="principal")http.HttpContext.User=Principal(user,null);
            return scenario=="denied"?null:new AuthorizedSessionActor(scenario=="wrong_user"?Guid.CreateVersion7():user,null,session);
        });
        var scopes=Substitute.For<IServiceScopeFactory>();var service=new WorkflowTodoActorAuthorization(authority,tenant,http,scopes,Capacity(true));
        var result=await service.AuthorizeAsync(user,permissions);
        Assert.AreEqual(scenario=="ok",result is not null);
        if(result is not null){Assert.AreEqual(user,result.UserId);Assert.AreEqual(session,result.CredentialId);Assert.IsFalse(result.IsApiKey);}
        await authority.Received(1).AuthorizeAllAsync(Arg.Is<IReadOnlyCollection<string>>(value=>value!=null&&value.SequenceEqual(permissions)),Arg.Any<CancellationToken>());
        await authority.DidNotReceive().AuthorizeAsync(Arg.Any<string>(),Arg.Any<CancellationToken>());scopes.DidNotReceive().CreateScope();
    }

    [TestMethod]
    [DataRow("ok")][DataRow("revoked")][DataRow("key_changed")][DataRow("context")][DataRow("transaction")][DataRow("capacity")]
    public async Task Api_key_uses_its_own_independent_authority_without_requiring_an_interactive_session(string scenario)
    {
        var user=Guid.CreateVersion7();var key=Guid.CreateVersion7();var tenant=new CurrentTenantAccessor();tenant.SetHost();
        var http=new HttpContextAccessor{HttpContext=new DefaultHttpContext{User=Principal(user,key)}};var sessions=Substitute.For<IIndependentCurrentSessionAuthorization>();
        var services=new ServiceCollection();CurrentTenantAccessor? child=null;var reads=0;
        services.AddScoped<CurrentTenantAccessor>();services.AddScoped<ICurrentTenant>(sp=>sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(sp=>sp.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<IDataTransactionState>(_=>{var state=Substitute.For<IDataTransactionState>();state.HasTransaction.Returns(scenario=="transaction");return state;});
        services.AddScoped<IDatabaseAuthorizationReadScope>(_=>Capacity(true));
        services.AddScoped<ICurrentApiKeyAuthorization>(sp=>{
            child=sp.GetRequiredService<CurrentTenantAccessor>();var authority=Substitute.For<ICurrentApiKeyAuthorization>();
            authority.AuthorizeAsync(Arg.Any<IReadOnlyList<string>>(),Arg.Any<CancellationToken>()).Returns(call=>{
                reads++;Assert.AreNotSame(tenant,child);Assert.IsTrue(child.IsHost);CollectionAssert.AreEqual(new[]{"workflow.todos.approve","workflow.todos.sign"},call.ArgAt<IReadOnlyList<string>>(0).ToArray());
                if(scenario=="key_changed")
                {var identity=(ClaimsIdentity)http.HttpContext.User.Identity!;identity.RemoveClaim(identity.FindFirst(FullNetIdentityClaimTypes.ApiKeyId)!);identity.AddClaim(new(FullNetIdentityClaimTypes.ApiKeyId,Guid.CreateVersion7().ToString("D")));}
                if(scenario=="context")tenant.SetTenant(new(Guid.CreateVersion7(),"changed","Changed"));
                return scenario=="revoked"?(Guid?)null:user;
            });return authority;
        });
        using var provider=services.BuildServiceProvider();var service=new WorkflowTodoActorAuthorization(sessions,tenant,http,provider.GetRequiredService<IServiceScopeFactory>(),Capacity(scenario!="capacity"));
        if(scenario=="capacity")await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(()=>service.AuthorizeAsync(user,["workflow.todos.approve","workflow.todos.sign"]));
        else if(scenario=="transaction")await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>service.AuthorizeAsync(user,["workflow.todos.approve","workflow.todos.sign"]));
        else
        {
            var result=await service.AuthorizeAsync(user,["workflow.todos.approve","workflow.todos.sign"]);Assert.AreEqual(scenario=="ok",result is not null);
            if(result is not null){Assert.IsTrue(result.IsApiKey);Assert.AreEqual(key,result.CredentialId);Assert.AreEqual(user,result.UserId);}
            Assert.AreEqual(1,reads);Assert.IsFalse(child!.IsAvailable);
        }
        await sessions.DidNotReceive().AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>());
        if(scenario!="context")Assert.IsTrue(tenant.IsHost);
    }

    [TestMethod]
    public async Task Missing_or_mismatched_actor_never_starts_an_authority_read()
    {
        var user=Guid.CreateVersion7();var tenant=new CurrentTenantAccessor();tenant.SetHost();var http=new HttpContextAccessor{HttpContext=new DefaultHttpContext{User=Principal(user,null)}};
        var sessions=Substitute.For<IIndependentCurrentSessionAuthorization>();var scopes=Substitute.For<IServiceScopeFactory>();var service=new WorkflowTodoActorAuthorization(sessions,tenant,http,scopes,Capacity(true));
        Assert.IsNull(await service.AuthorizeAsync(Guid.Empty,["workflow.todos.approve"]));Assert.IsNull(await service.AuthorizeAsync(Guid.CreateVersion7(),["workflow.todos.approve"]));Assert.IsNull(await service.AuthorizeAsync(user,[]));
        await sessions.DidNotReceive().AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>());scopes.DidNotReceive().CreateScope();
    }
    private static ClaimsPrincipal Principal(Guid user,Guid? key)
    {
        var claims=new List<Claim>{new(FullNetIdentityClaimTypes.Subject,user.ToString("D"))};if(key is { } id)claims.Add(new(FullNetIdentityClaimTypes.ApiKeyId,id.ToString("D")));
        return new(new ClaimsIdentity(claims,key.HasValue?"FullNET.ApiKey":"Bearer"));
    }
    private static IDatabaseAuthorizationReadScope Capacity(bool available)
    {var capacity=Substitute.For<IDatabaseAuthorizationReadScope>();capacity.HasIndependentReadCapacity.Returns(available);capacity.Enter().Returns(_=>new Lease());return capacity;}
    private sealed class Lease:IDisposable{public void Dispose(){}}
}
