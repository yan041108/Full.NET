using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.ManageKnowledgeOperations;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>查询等待后撤权必须丢弃结果；最后授权等待后的归属和当前聚合必须重新读取。</summary>
[TestClass]
public sealed class AiKnowledgeQualityAuthorizationTests
{
    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task Permission_revocation_during_query_does_not_deliver_finance_or_observations(bool finance)
    {
        var f=new Fixture();f.AfterRead=()=>f.Revoked=true;
        Assert.IsFalse(await f.Read(finance));Assert.AreEqual(1,f.Reads);
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task Ownership_or_ledger_changes_during_last_authorization_are_read_again(bool finance)
    {
        var f=new Fixture();var checks=0;f.Authorization.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>()).Returns(_=>
        {if(++checks==2)f.Removed=true;return f.Actor;});
        var passed=await f.Read(finance);Assert.AreEqual(finance,passed);Assert.AreEqual(2,f.Reads);
    }
    private sealed class Fixture : IQueryExecutor
    {
        internal readonly ICurrentSessionAuthorization Authorization=Substitute.For<ICurrentSessionAuthorization>();
        internal readonly AuthorizedSessionActor Actor=new(Guid.CreateVersion7(),null,Guid.CreateVersion7());
        internal readonly Guid Knowledge=Guid.CreateVersion7();internal readonly AiKnowledgeQualityService Service;
        internal bool Revoked,Removed;internal int Reads;internal Action? AfterRead;
        internal Fixture()
        {
            var tenant=Substitute.For<ICurrentTenant>();tenant.IsHost.Returns(true);
            var clock=Substitute.For<IClock>();clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-30T12:00:00Z"));
            Authorization.AuthorizeAllAsync(Arg.Any<IReadOnlyCollection<string>>(),Arg.Any<CancellationToken>()).Returns(_=>Revoked?null:Actor);
            Service=new(this,Substitute.For<IDataTransactionState>(),tenant,Authorization,clock,Options.Create(new DatabaseOptions{Provider=DatabaseProvider.SqlServer}));
        }
        internal async Task<bool> Read(bool finance)
        {
            if(!finance)return (await Service.ObservationsAsync(Knowledge,Actor.UserId,null,null,default)).IsSuccess;
            var result=await Service.FinanceAsync(Actor.UserId,null,null,null,default);
            if(result.IsSuccess&&Removed)Assert.AreEqual(0,result.Value!.Items.Count);return result.IsSuccess;
        }
        // 内部聚合行使用显式执行器，不放大生产类型可见性以迁就动态代理。
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)
        {
            Reads++;IReadOnlyList<AiKnowledgeFinanceRow> rows=Removed?[]:[new(){Month="2026-10",Currency="USD",Requests=1,Known=1,RecordedCost=0.125m}];
            AfterRead?.Invoke();return Task.FromResult((IReadOnlyList<T>)(object)rows);
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)
        {Reads++;var row=Removed?null:new AiKnowledgeQualityObservationRow{KnowledgeBaseId=Knowledge};AfterRead?.Invoke();return Task.FromResult((T?)(object?)row);}
    }
}
